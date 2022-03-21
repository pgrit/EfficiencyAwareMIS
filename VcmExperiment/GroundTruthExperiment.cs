using System.IO;
using System.Text.Json;

namespace EfficiencyAwareMIS.VcmExperiment;

class GroundTruthExperiment : Experiment {
    public int NumIterations = 128;

    int width, height;

    float[] numLightPathCandidates = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 2.0f };
    int[] numConnectionsCandidates = new[] { 0, 1, 2, 4, 8, 16 };

    List<Candidate> candidates;

    public override List<Method> MakeMethods() {
        List<Method> methods = new() { };

        // Generate equivalent methods for each candidate
        // foreach (var c in candidates) {
        //     methods.Add(new(c.ToString(), new PathLengthEstimatingVcm() {
        //         NumIterations = NumIterations,
        //         NumConnections = c.NumConnections,
        //         NumLightPaths = c.NumLightPaths,
        //         EnableMerging = c.Merge
        //     }));
        // }

        // Run our method with long pilot iteration to estimate converged moments
        // methods.Add(new("MomentEstimator", new AdaptiveVcm() {
        //     NumIterations = NumIterations,
        //     MaxNumUpdates = int.MaxValue,
        //     NumConnections = 4,
        //     NumLightPaths = null,
        //     EnableMerging = true,
        //     WriteDebugInfo = true,
        //     OnlyAccumulate = true,
        //     NumConnectionsCandidates = numConnectionsCandidates,
        //     NumLightPathCandidates = numLightPathCandidates,
        // }));

        return methods;
    }

    public override void OnStartScene(Scene scene, string dir) {
        width = scene.FrameBuffer.Width;
        height = scene.FrameBuffer.Height;

        candidates = new();
        foreach (float lightRatio in numLightPathCandidates) {
            int numLightPaths = (int)(lightRatio * width * height);
            foreach (int numConnect in numConnectionsCandidates) {
                foreach (bool merge in new[] { true, false }) {
                    candidates.Add(new(numLightPaths, numConnect, merge));
                }
            }
        }
        candidates.Add(new(0, 0, false)); // Path tracer
    }

    MonochromeImage ComputeVarianceImage(RgbImage render, RgbImage reference) {
        MonochromeImage varImg = new(width, height);
        for (int row = 0; row < height; ++row) {
            for (int col = 0; col < width; ++col) {
                float delta = (render.GetPixel(col, row) - reference.GetPixel(col, row)).Average;
                // Squared error is an estimate of the variance with n iterations, assumes variance
                // reduces by 1/n over the iterations (ignores PM bias).
                float variance = delta * delta * NumIterations;
                varImg.SetPixel(col, row, variance);
            }
        }
        return varImg;
    }

    Dictionary<Candidate, MonochromeImage> DenoiseErrors(Dictionary<Candidate, MonochromeImage> errors,
                                                         RgbImage normals, RgbImage albedo, RgbImage reference) {
        Dictionary<Candidate, MonochromeImage> denoisedErrors = new();
        Denoiser denoiser = new();
        foreach (var (c, img) in errors) {
            RgbImage rgb = new(width, height);
            for (int row = 0; row < height; ++row) {
                for (int col = 0; col < width; ++col) {
                    rgb.SetPixel(col, row, img.GetPixel(col, row) * RgbColor.White);
                }
            }
            var denoised = denoiser.Denoise(rgb, albedo, normals);
            for (int row = 0; row < height; ++row) {
                for (int col = 0; col < width; ++col) {
                    // Fix denoiser hallucinations in completely black regions
                    if (reference.GetPixel(col, row) == RgbColor.Black)
                        denoised.SetPixel(col, row, RgbColor.Black);
                }
            }
            denoisedErrors.Add(c, new MonochromeImage(denoised, MonochromeImage.RgbConvertMode.Average));
        }
        return denoisedErrors;
    }

    public override void OnDoneScene(Scene scene, string dir) {
        // Gather variance estimates
        Dictionary<Candidate, MonochromeImage> varianceImages = new();
        RgbImage normals = null;
        RgbImage albedo = new(width, height);
        albedo.Fill(1, 1, 1);
        RgbImage reference = new(Path.Join(dir, "Reference.exr"));
        foreach (var c in candidates) {
            // Read the images
            RgbImage render = new(Path.Join(dir, c.ToString(), "Render.exr"));

            if (normals == null) {
                normals = Layers.LoadFromFile(Path.Join(dir, c.ToString(), "Render.exr"))["normal"] as RgbImage;
            }

            // Compute variance and add to the dictionary
            var variance = ComputeVarianceImage(render, reference);
            varianceImages.Add(new(c.NumLightPaths, c.NumConnections, c.Merge), variance);
        }
        var denoisedVariances = DenoiseErrors(varianceImages, normals, albedo, reference);

        // Save variances in a layered .exr
        var layers = new (string, ImageBase)[denoisedVariances.Count];
        int i = 0;
        foreach (var (c, img) in denoisedVariances) {
            layers[i++] = (c.ToString(), img);
        }
        Layers.WriteToExr(Path.Join(dir, "Variances.exr"), layers);

        // Gather moment estimates
        var momentLayers = Layers.LoadFromFile(Path.Join(dir, "MomentEstimator", "RenderMoments.exr"));
        Dictionary<Candidate, MonochromeImage> momentImages = new();
        foreach (var c in candidates) {
            momentImages[c] = momentLayers[c.ToString()] as MonochromeImage;
        }
        var denoisedMoments = DenoiseErrors(momentImages, normals, albedo, reference);

        // Retrieve the path length statistics for the cost heuristic from one of the candidates
        string path = Path.Join(dir, new Candidate(width * height, 4, true).ToString(), "Render.json");
        var json = JsonDocument.Parse(File.ReadAllText(path));
        float avgCamLen = json.RootElement.GetProperty("AverageCameraPathLength").GetSingle();
        float avgLightLen = json.RootElement.GetProperty("AverageLightPathLength").GetSingle();
        float avgPhoton = json.RootElement.GetProperty("AveragePhotonsPerQuery").GetSingle();
        CostHeuristic costHeuristic = new();
        costHeuristic.UpdateStats(width * height, width * height, avgCamLen, avgLightLen, avgPhoton);

        // Compute merge and connect masks
        VcmOptimizer.OptimizePerPixel(denoisedVariances, costHeuristic, true, true,
            out var mergeMaskVar, out var connectMaskVar);
        VcmOptimizer.OptimizePerPixel(denoisedMoments, costHeuristic, true, true,
            out var mergeMaskMoment, out var connectMaskMoment);
        Layers.WriteToExr(Path.Join(dir, "Masks.exr"),
            ("merge-var", mergeMaskVar), ("connect-var", connectMaskVar),
            ("merge-moment", mergeMaskMoment), ("connect-moment", connectMaskMoment)
        );

        // Optimize global counts and write to file
        // ...

        // Repeat the same test but with merges disabled (are connections fine if merges are off?)
        // ...

        // Compare relative and absolute moment / variances in global outcome
        // ...

        // Compare cost heuristic values to actual render times (plot)
        // ...

        // Ablation study: optimization with different values for the cost heuristic hyperparameters
        // ...
    }

    public override void OnDone(string workingDirectory) {
        // Generate overview figure ...
    }
}