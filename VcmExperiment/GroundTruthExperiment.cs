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
        //     NumLightPathCandidates = numLightPathCandidates
        // }));

        // Repeat, but without correlation-aware MIS
        // foreach (var c in candidates) {
        //     methods.Add(new("NoCAMIS-" + c.ToString(), new PathLengthEstimatingVcm() {
        //         NumIterations = NumIterations,
        //         NumConnections = c.NumConnections.Value,
        //         NumLightPaths = c.NumLightPaths,
        //         EnableMerging = c.Merge.Value,
        //         DisableCorrelAware = true
        //     }));
        // }
        // methods.Add(new("NoCAMIS-" + "MomentEstimator", new AdaptiveVcm() {
        //     NumIterations = NumIterations,
        //     MaxNumUpdates = int.MaxValue,
        //     NumConnections = 4,
        //     NumLightPaths = null,
        //     EnableMerging = true,
        //     WriteDebugInfo = true,
        //     OnlyAccumulate = true,
        //     NumConnectionsCandidates = numConnectionsCandidates,
        //     NumLightPathCandidates = numLightPathCandidates,
        //     DisableCorrelAware = true
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

    void Optimize(CostHeuristic costHeuristic, Dictionary<Candidate, MonochromeImage> variances,
                  Dictionary<Candidate, MonochromeImage> moments, RgbImage reference, string dir,
                  string suffix, string prefix) {
        VcmOptimizer.OptimizePerPixel(variances, costHeuristic, true, true,
            out var mergeMaskVar, out var connectMaskVar);
        VcmOptimizer.OptimizePerPixel(moments, costHeuristic, true, true,
            out var mergeMaskMoment, out var connectMaskMoment);
        Layers.WriteToExr(Path.Join(dir, $"{prefix}Masks{suffix}.exr"),
            ("merge-var", mergeMaskVar), ("connect-var", connectMaskVar),
            ("merge-moment", mergeMaskMoment), ("connect-moment", connectMaskMoment)
        );

        // Optimize global counts and write to file
        MonochromeImage pixelIntensities = new(reference, MonochromeImage.RgbConvertMode.Average);
        var globalVar = VcmOptimizer.OptimizePerImage(variances, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskVar.GetPixel, connectMaskVar.GetPixel, true, true);
        var globalMoment = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskMoment.GetPixel, connectMaskMoment.GetPixel, true, true);

        // Compare to purely global optimization
        var pureGlobalVar = VcmOptimizer.OptimizePerImage(variances, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, null, null, false, false);
        var pureGlobalMoment = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, null, null, false, false);

        // Compare relative and absolute moment / variances in global outcome
        pixelIntensities.Fill(1); // set all pixels to one so we effectively don't use relative moments / vars
        var globalVarAbs = VcmOptimizer.OptimizePerImage(variances, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskVar.GetPixel, connectMaskVar.GetPixel, true, true);
        var globalMomentAbs = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
            numConnectionsCandidates, costHeuristic, mergeMaskMoment.GetPixel, connectMaskMoment.GetPixel, true, true);

        System.IO.File.WriteAllText(Path.Join(dir, $"{prefix}GlobalCounts{suffix}.txt"),
            $"{globalVar.Item1}\n{(globalVar.Item3.HasValue ? 0 : 1)}\n" +
            $"{globalMoment.Item1}\n{(globalMoment.Item3.HasValue ? 0 : 1)}\n" +
            $"{globalVarAbs.Item1}\n{(globalVarAbs.Item3.HasValue ? 0 : 1)}\n" +
            $"{globalMomentAbs.Item1}\n{(globalMomentAbs.Item3.HasValue ? 0 : 1)}\n" +
            $"{pureGlobalVar.Item1}\n" + $"{pureGlobalVar.Item2.Value}\n" + $"{pureGlobalVar.Item3.Value}\n" +
            $"{pureGlobalMoment.Item1}\n" + $"{pureGlobalMoment.Item2.Value}\n" + $"{pureGlobalMoment.Item3.Value}\n");
    }

    class Images {
        public readonly Dictionary<Candidate, MonochromeImage> VarianceImages;
        public readonly Dictionary<Candidate, MonochromeImage> MomentImages;
        public readonly Dictionary<Candidate, MonochromeImage> DenoisedVariances;
        public readonly Dictionary<Candidate, MonochromeImage> DenoisedMoments;
        public readonly RgbImage Reference;
        public readonly CostHeuristic CostHeuristic;
        public readonly float AvgCamLen;
        public readonly float AvgLightLen;
        public readonly float AvgPhoton;

        static MonochromeImage ComputeVarianceImage(RgbImage render, RgbImage reference, int numIterations) {
            MonochromeImage varImg = new(render.Width, render.Height);
            for (int row = 0; row < render.Height; ++row) {
                for (int col = 0; col < render.Width; ++col) {
                    float delta = (render.GetPixel(col, row) - reference.GetPixel(col, row)).Average;
                    // Squared error is an estimate of the variance with n iterations, assumes variance
                    // reduces by 1/n over the iterations (ignores PM bias).
                    float variance = delta * delta * numIterations;
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
                RgbImage rgb = new(reference.Width, reference.Height);
                for (int row = 0; row < reference.Height; ++row) {
                    for (int col = 0; col < reference.Width; ++col) {
                        rgb.SetPixel(col, row, img.GetPixel(col, row) * RgbColor.White);
                    }
                }
                var denoised = denoiser.Denoise(rgb, albedo, normals);
                for (int row = 0; row < reference.Height; ++row) {
                    for (int col = 0; col < reference.Width; ++col) {
                        // Fix denoiser hallucinations in completely black regions
                        if (reference.GetPixel(col, row) == RgbColor.Black)
                            denoised.SetPixel(col, row, RgbColor.Black);
                    }
                }
                denoisedErrors.Add(c, new MonochromeImage(denoised, MonochromeImage.RgbConvertMode.Average));
            }
            return denoisedErrors;
        }

        public Images(List<Candidate> candidates, int numIterations, string dir, string prefix) {
            // Read reference image and auxiliary features for denoising
            RgbImage normals = Layers.LoadFromFile(Path.Join(dir, candidates.First().ToString(), "Render.exr"))["normal"] as RgbImage;
            int width = normals.Width;
            int height = normals.Height;
            RgbImage albedo = new(width, height);
            albedo.Fill(1, 1, 1);
            Reference = new(Path.Join(dir, "Reference.exr"));

            // Retrieve the path length statistics for the cost heuristic from one of the candidates
            string path = Path.Join(dir, prefix + new Candidate(width * height, 4, true).ToString(), "Render.json");
            var json = JsonDocument.Parse(File.ReadAllText(path));
            AvgCamLen = json.RootElement.GetProperty("AverageCameraPathLength").GetSingle();
            AvgLightLen = json.RootElement.GetProperty("AverageLightPathLength").GetSingle();
            AvgPhoton = json.RootElement.GetProperty("AveragePhotonsPerQuery").GetSingle();
            CostHeuristic = new();
            CostHeuristic.UpdateStats(width * height, width * height, AvgCamLen, AvgLightLen, AvgPhoton);

            // Compute variance estimates from each rendered image
            VarianceImages = new();
            Parallel.ForEach(candidates, c => {
                RgbImage render = new(Path.Join(dir, prefix + c.ToString(), "Render.exr"));
                var variance = ComputeVarianceImage(render, Reference, numIterations);
                lock (VarianceImages) VarianceImages.Add(new(c.NumLightPaths, c.NumConnections, c.Merge), variance);
            });
            DenoisedVariances = DenoiseErrors(VarianceImages, normals, albedo, Reference);

            // Save variances in a layered .exr
            var layers = new (string, ImageBase)[DenoisedVariances.Count];
            int i = 0;
            foreach (var (c, img) in DenoisedVariances) {
                layers[i++] = (c.ToString(), img);
            }
            Layers.WriteToExr(Path.Join(dir, prefix + "Variances.exr"), layers);

            // Gather moment estimates
            var momentLayers = Layers.LoadFromFile(Path.Join(dir, prefix + "MomentEstimator", "RenderMoments.exr"));
            MomentImages = new();
            foreach (var c in candidates) {
                MomentImages[c] = momentLayers[c.ToString()] as MonochromeImage;
            }
            DenoisedMoments = DenoiseErrors(MomentImages, normals, albedo, Reference);

            // Save denoised moments in a layered .exr
            layers = new (string, ImageBase)[DenoisedMoments.Count];
            i = 0;
            foreach (var (c, img) in DenoisedMoments) {
                layers[i++] = (c.ToString(), img);
            }
            Layers.WriteToExr(Path.Join(dir, prefix + "Moments.exr"), layers);
        }
    }

    void EvaluateGroundTruth(string prefix, string dir, Images data) {
        // Compute merge and connect masks
        Optimize(data.CostHeuristic, data.DenoisedVariances, data.DenoisedMoments, data.Reference, dir, "", prefix);

        // Repeat the same test but with merges disabled (are connections fine if merges are off?)
        Dictionary<Candidate, MonochromeImage> noMergeVars = new();
        Dictionary<Candidate, MonochromeImage> noMergeMoments = new();
        foreach (var (c, v) in data.DenoisedVariances) {
            if (c.Merge == false) noMergeVars[c] = v;
        }
        foreach (var (c, v) in data.DenoisedMoments) {
            if (c.Merge == false) noMergeMoments[c] = v;
        }
        Optimize(data.CostHeuristic, noMergeVars, noMergeMoments, data.Reference, dir, "BDPT", prefix);
    }

    void EvaluateCostHeuristic(string prefix, string dir, Images data) {
        // Gather cost heuristic values and actual render times and write them to a .json for plotting
        Dictionary<string, float[]> times = new();
        foreach (var c in candidates) {
            var heuristic = data.CostHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections.Value,
                c.Merge.Value ? 1 : 0, !c.Merge.Value); // We compute the cost heuristic for global merging decisions

            var meta = JsonDocument.Parse(File.ReadAllText(Path.Join(dir, prefix + c.ToString(), "Render.json")));
            long renderTimeMs = meta.RootElement.GetProperty("RenderTime").GetInt64();
            float renderTimeSec = renderTimeMs / 1000.0f;
            times.Add(c.ToString(), new [] { heuristic, renderTimeSec });
        }
        File.WriteAllText(Path.Join(dir, $"{prefix}Costs.json"), JsonSerializer.Serialize(times));

        // Ablation study: optimization with different values for the cost heuristic hyperparameters
        var costScales = new float[] { 0.1f, 0.5f, 2.0f, 10.0f };
        foreach (var scale in costScales) {
            CostHeuristic ch = new();
            ch.UpdateStats(width * height, width * height, data.AvgCamLen, data.AvgLightLen, data.AvgPhoton);
            ch.CostCamera *= scale;
            Optimize(ch, data.DenoisedVariances, data.DenoisedMoments, data.Reference, dir, $"CostCam{scale}", prefix);

            ch = new(); ch.UpdateStats(width * height, width * height, data.AvgCamLen, data.AvgLightLen, data.AvgPhoton);
            ch.CostLight *= scale;
            Optimize(ch, data.DenoisedVariances, data.DenoisedMoments, data.Reference, dir, $"CostLight{scale}", prefix);

            ch = new(); ch.UpdateStats(width * height, width * height, data.AvgCamLen, data.AvgLightLen, data.AvgPhoton);
            ch.CostPhotonBuild *= scale;
            ch.CostQuery *= scale;
            ch.CostShade *= scale;
            Optimize(ch, data.DenoisedVariances, data.DenoisedMoments, data.Reference, dir, $"CostMerge{scale}", prefix);

            ch = new(); ch.UpdateStats(width * height, width * height, data.AvgCamLen, data.AvgLightLen, data.AvgPhoton);
            ch.CostConnect *= scale;
            Optimize(ch, data.DenoisedVariances, data.DenoisedMoments, data.Reference, dir, $"CostConnect{scale}", prefix);
        }
    }

    public override void OnDoneScene(Scene scene, string dir) {
        // Images data = new(candidates, NumIterations, dir, "");
        // EvaluateGroundTruth("", dir, data);
        // EvaluateCostHeuristic("", dir, data);

        Images dataNoCAMIS = new(candidates, NumIterations, dir, "NoCAMIS-");
        EvaluateGroundTruth("NoCAMIS-", dir, dataNoCAMIS);
    }

    public override void OnDone(string workingDirectory) {
        // Generate overview figure ...
    }
}