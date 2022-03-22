using System.IO;
using System.Linq;
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

    void Optimize(CostHeuristic costHeuristic, Dictionary<Candidate, MonochromeImage> variances,
                  Dictionary<Candidate, MonochromeImage> moments, RgbImage reference, string dir, string suffix) {
        VcmOptimizer.OptimizePerPixel(variances, costHeuristic, true, true,
            out var mergeMaskVar, out var connectMaskVar);
        VcmOptimizer.OptimizePerPixel(moments, costHeuristic, true, true,
            out var mergeMaskMoment, out var connectMaskMoment);
        Layers.WriteToExr(Path.Join(dir, $"Masks{suffix}.exr"),
            ("merge-var", mergeMaskVar), ("connect-var", connectMaskVar),
            ("merge-moment", mergeMaskMoment), ("connect-moment", connectMaskMoment)
        );

        // Optimize global counts and write to file
        MonochromeImage pixelIntensities = new(reference, MonochromeImage.RgbConvertMode.Average);
        var globalVar = VcmOptimizer.OptimizePerImage(variances, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskVar.GetPixel, connectMaskVar.GetPixel, true, true);
        var globalMoment = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskMoment.GetPixel, connectMaskMoment.GetPixel, true, true);

        // Compare relative and absolute moment / variances in global outcome
        pixelIntensities.Fill(1); // set all pixels to one so we effectively don't use relative moments / vars
        var globalVarAbs = VcmOptimizer.OptimizePerImage(variances, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskVar.GetPixel, connectMaskVar.GetPixel, true, true);
        var globalMomentAbs = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskMoment.GetPixel, connectMaskMoment.GetPixel, true, true);
        System.IO.File.WriteAllText(Path.Join(dir, $"GlobalCounts{suffix}.txt"),
            $"{globalVar.Item1}\n{globalMoment.Item1}\n{globalVarAbs.Item1}\n{globalMomentAbs.Item1}");
    }

    public override void OnDoneScene(Scene scene, string dir) {
        // Read reference image and auxiliary features for denoising
        RgbImage normals = Layers.LoadFromFile(Path.Join(dir, candidates.First().ToString(), "Render.exr"))["normal"] as RgbImage;;
        RgbImage albedo = new(width, height);
        albedo.Fill(1, 1, 1);
        RgbImage reference = new(Path.Join(dir, "Reference.exr"));

        // Compute variance estimates from each rendered image
        Dictionary<Candidate, MonochromeImage> varianceImages = new();
        Parallel.ForEach(candidates, c => {
            RgbImage render = new(Path.Join(dir, c.ToString(), "Render.exr"));
            var variance = ComputeVarianceImage(render, reference);
            lock (varianceImages) varianceImages.Add(new(c.NumLightPaths, c.NumConnections, c.Merge), variance);
        });
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
        Optimize(costHeuristic, denoisedVariances, denoisedMoments, reference, dir, "");

        // Repeat the same test but with merges disabled (are connections fine if merges are off?)
        Dictionary<Candidate, MonochromeImage> noMergeVars = new();
        Dictionary<Candidate, MonochromeImage> noMergeMoments = new();
        foreach (var (c, v) in denoisedVariances) {
            if (c.Merge == false) noMergeVars[c] = v;
        }
        foreach (var (c, v) in denoisedMoments) {
            if (c.Merge == false) noMergeMoments[c] = v;
        }
        Optimize(costHeuristic, noMergeVars, noMergeMoments, reference, dir, "BDPT");

        // Gather cost heuristic values and actual render times and write them to a .json for plotting
        Dictionary<string, float[]> times = new();
        foreach (var c in candidates) {
            var heuristic = costHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections, c.Merge ? 1 : 0);

            var meta = JsonDocument.Parse(File.ReadAllText(Path.Join(dir, c.ToString(), "Render.json")));
            long renderTimeMs = meta.RootElement.GetProperty("RenderTime").GetInt64();
            float renderTimeSec = renderTimeMs / 1000.0f;
            times.Add(c.ToString(), new [] { heuristic, renderTimeSec });
        }
        File.WriteAllText(Path.Join(dir, $"Costs.json"), JsonSerializer.Serialize(times));

        // Ablation study: optimization with different values for the cost heuristic hyperparameters
        float costLightBase = 1.0f;
        float costCameraBase = 1.0f;
        float costMergeBase = 1.5f;
        float costConnectBase = 0.4f;
        var costScales = new float[] { 0.1f, 0.5f, 2.0f, 10.0f };
        foreach (var scale in costScales) {
            costHeuristic.CostCamera = costCameraBase * scale;
            costHeuristic.CostLight = costLightBase;
            costHeuristic.CostMerge = costMergeBase;
            costHeuristic.CostConnect = costConnectBase;
            Optimize(costHeuristic, denoisedVariances, denoisedMoments, reference, dir, $"CostCam{scale}");

            costHeuristic.CostCamera = costCameraBase;
            costHeuristic.CostLight = costLightBase * scale;
            costHeuristic.CostMerge = costMergeBase;
            costHeuristic.CostConnect = costConnectBase;
            Optimize(costHeuristic, denoisedVariances, denoisedMoments, reference, dir, $"CostLight{scale}");

            costHeuristic.CostCamera = costCameraBase;
            costHeuristic.CostLight = costLightBase;
            costHeuristic.CostMerge = costMergeBase * scale;
            costHeuristic.CostConnect = costConnectBase;
            Optimize(costHeuristic, denoisedVariances, denoisedMoments, reference, dir, $"CostMerge{scale}");

            costHeuristic.CostCamera = costCameraBase;
            costHeuristic.CostLight = costLightBase;
            costHeuristic.CostMerge = costMergeBase;
            costHeuristic.CostConnect = costConnectBase * scale;
            Optimize(costHeuristic, denoisedVariances, denoisedMoments, reference, dir, $"CostConnect{scale}");
        }
    }

    public override void OnDone(string workingDirectory) {
        // Generate overview figure ...
    }
}