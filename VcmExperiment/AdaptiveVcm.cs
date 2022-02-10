namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Adapts the number of light paths and number of connections during rendering, and disables merging on
/// a per-pixel basis.
/// </summary>
class AdaptiveVcm : MomentEstimatingVcm {
    /// <summary>
    /// Candidates for the number of light subpaths, as a fraction of the number of pixels.
    /// </summary>
    public float[] NumLightPathCandidates = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 2.0f };

    /// <summary>
    /// Candidates for the number of bidirectional connections per camera subpath vertex
    /// </summary>
    public int[] NumConnectionsCandidates = new[] { 0, 1, 2, 4, 8, 16 };

    record struct Candidate(int NumLightPaths, int NumConnections, bool Merge) {
        public override string ToString() => $"n={NumLightPaths:000000},c={NumConnections:00},m={(Merge ? 1 : 0)}";
    }

    CostHeuristic CostHeuristic { get; set; } = new();

    Dictionary<Candidate, MonochromeImage> momentImages;
    RgbImage denoisedImage;

    protected override void OnStartIteration(uint iteration) {
        base.OnStartIteration(iteration);

        if (iteration == 0) { // Initialize the candidates at the start of the first iteration
            int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
            momentImages = new();
            foreach (float nRel in NumLightPathCandidates) {
                foreach (int c in NumConnectionsCandidates) {
                    int n = (int)(numPixels * nRel);
                    momentImages.Add(new(n, c, true), new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height));
                    momentImages.Add(new(n, c, false), new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height));
                }
            }
            momentImages.Add(new(0, 0, false), new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height));
        }

        CostHeuristic.UpdateStats(Scene.FrameBuffer.Width * Scene.FrameBuffer.Height, NumLightPaths.Value,
            AverageCameraPathLength, AverageLightPathLength, AveragePhotonsPerQuery);

        if (iteration > 0 && iteration < 2) { // Only update the denoised ground truth twice (it's expensive)
            DenoiseBuffers.Denoise();
            denoisedImage = Scene.FrameBuffer.GetLayer("denoised").Image as RgbImage;
        }
    }

    protected override void OnAfterRender() {
        base.OnAfterRender();

        var layers = new (string, ImageBase)[momentImages.Count];
        int i = 0;
        foreach (var (c, img) in momentImages) {
            img.Scale(1.0f / Scene.FrameBuffer.CurIteration);
            layers[i++] = (c.ToString(), img);
        }
        Layers.WriteToExr(Scene.FrameBuffer.Basename + "Moments.exr", layers);
    }

    protected override void OnMomentSample(RgbColor weight, float kernelWeight, int pathLength,
                                           ProxyWeights proxyWeights, Vector2 pixel) {
        // We compute the second moment of the average value across all color channels.
        float w2 = weight.Average * weight.Average;

        // Precompute terms where possible
        float lt = proxyWeights.LightTracing / NumLightPathsProxyStrategy;
        float con = proxyWeights.Connections / NumConnectionsProxy;
        float curMerge = MergeProbability(pixel);

        // Update the second moment estimates of all candidates.
        foreach (var (candidate, img) in momentImages) {
            // Proxy weights multiplied by pilot sample count, divided by proxy sample count
            float a = proxyWeights.PathTracing
                + lt * NumLightPaths.Value
                + con * NumConnections
                + proxyWeights.Merges * curMerge;

            // Same, but multiplied with correl-aware correction factors
            float b = proxyWeights.PathTracing
                + lt * NumLightPaths.Value
                + con * NumConnections
                + proxyWeights.MergesTimesCorrelAware * curMerge;

            // Proxy weights multiplied by candidate sample count, divided by proxy count
            float c = proxyWeights.PathTracing
                + lt * candidate.NumLightPaths
                + con * candidate.NumConnections
                + proxyWeights.Merges * (candidate.Merge ? 1.0f : 0.0f);

            // Same, but multiplied with correl-aware correction factors
            float d = proxyWeights.PathTracing
                + lt * candidate.NumLightPaths
                + con * candidate.NumConnections
                + proxyWeights.MergesTimesCorrelAware * (candidate.Merge ? 1.0f : 0.0f);

            float correctionFactor = (a * a * d) / (c * c * b);
            float cost = CostHeuristic.EvaluatePerPixel(candidate.NumLightPaths, candidate.NumConnections,
                (candidate.Merge ? 1.0f : 0.0f));

            Debug.Assert(float.IsFinite(cost));
            Debug.Assert(float.IsFinite(correctionFactor));
            Debug.Assert(cost > 0);
            Debug.Assert(correctionFactor > 0);

            img.AtomicAdd((int)pixel.X, (int)pixel.Y, correctionFactor * w2 * cost);
        }
    }
}