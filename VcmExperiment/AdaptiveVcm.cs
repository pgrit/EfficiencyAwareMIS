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

    /// <summary>
    /// If set to true, writes all candidate moment images to a layered .exr file after rendering is done.
    /// </summary>
    public bool WriteMomentImages = false;

    /// <summary>
    /// If set to false, connections are optimized over the whole image instead of per-pixel
    /// </summary>
    public bool UserPerPixelConnections = true;

    record struct Candidate(int NumLightPaths, int NumConnections, bool Merge) {
        public override string ToString() => $"n={NumLightPaths:000000},c={NumConnections:00},m={(Merge ? 1 : 0)}";
    }

    CostHeuristic CostHeuristic { get; set; } = new();

    Dictionary<Candidate, IFilteredEstimates> momentImages;
    RgbImage denoisedImage;

    void InitCandidates() {
        int width = Scene.FrameBuffer.Width;
        int height = Scene.FrameBuffer.Height;
        int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
        momentImages = new();
        foreach (float nRel in NumLightPathCandidates) {
            foreach (int c in NumConnectionsCandidates) {
                int n = (int)(numPixels * nRel);
                momentImages.Add(new(n, c, true), new TiledEstimates(width, height, 8));
                momentImages.Add(new(n, c, false), new TiledEstimates(width, height, 8));
            }
        }
        momentImages.Add(new(0, 0, false), new TiledEstimates(width, height, 8));
    }

    void OptimizePerPixel() {
        // Reduce noise in the second moments via a simple lowpass filter
        foreach (var (candidate, moment) in momentImages) {
            moment.Prepare();
        }

        // Make per-pixel decisions
        MonochromeImage mergeMask = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
        MonochromeImage connectMask = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
        Parallel.For(0, Scene.FrameBuffer.Height, row => {
            for (int col = 0; col < Scene.FrameBuffer.Width; ++col) {
                float bestWorkNorm = float.MaxValue;
                Candidate bestCandidate = new();
                foreach (var (candidate, moment) in momentImages) {
                    float cost = CostHeuristic.EvaluatePerPixel(candidate.NumLightPaths,
                        candidate.NumConnections, (candidate.Merge ? 1.0f : 0.0f));
                    Debug.Assert(float.IsFinite(cost));
                    Debug.Assert(cost > 0);

                    float workNorm = moment.Query(col, row) * cost;

                    if (workNorm < bestWorkNorm) {
                        bestWorkNorm = workNorm;
                        bestCandidate = candidate;
                    }
                }

                // Set the per-pixel decision
                mergeMask.SetPixel(col, row, bestCandidate.Merge ? 1 : 0);
                connectMask.SetPixel(col, row, bestCandidate.NumConnections);
            }
        });

        // Filter the sample masks
        MonochromeImage mergeMaskBuf = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
        Filter.Dilation(mergeMask, mergeMaskBuf, 8);
        Filter.RepeatedBox(mergeMaskBuf, mergeMask, 4);

        if (UserPerPixelConnections) {
            MonochromeImage connectMaskBuf = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
            Filter.Dilation(connectMask, connectMaskBuf, 8);
            Filter.RepeatedBox(connectMaskBuf, connectMask, 4);
        }

        Layers.WriteToExr(Scene.FrameBuffer.Basename + "Masks.exr", ("merge", mergeMask), ("connect", connectMask));
    }

    void OptimizePerImage() {
        // Initialize total cost and moment for all per-image candidates
        Dictionary<Candidate, float> relMoments = new();
        Dictionary<Candidate, float> costs = new();
        int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
        foreach (float nRel in NumLightPathCandidates) {
            int n = (int)(numPixels * nRel);
            if (UserPerPixelConnections) {
                relMoments[new(n, 0, false)] = 0.0f;
                costs[new(n, 0, false)] = 0.0f;
            } else {
                foreach (int c in NumConnectionsCandidates) {
                    relMoments[new(n, c, false)] = 0.0f;
                    costs[new(n, c, false)] = 0.0f;
                }
            }
        }
        // Add path tracing
        relMoments[new(0, 0, false)] = 0.0f;
        costs[new(0, 0, false)] = 0.0f;

        // Accumulate relative moments and cost
        float recipNumPixels = 1.0f / numPixels;
        Parallel.For(0, Scene.FrameBuffer.Height, row => {
            for (int col = 0; col < Scene.FrameBuffer.Width; ++col) {
                // Merges are considered enabled if there's at least a 10% probability of them happening
                float mergeProb = GetPerPixelMergeProbability(new(col, row));
                bool mergeDecision = mergeProb > 0.1f;

                // Find closest candidate for the actual number of connections
                float connectCount = GetPerPixelConnectionCount(new(col, row));
                int candidateConnect = NumConnectionsCandidates[^1];
                for (int i = 0; i < NumConnectionsCandidates.Length; ++i) {
                    if (NumConnectionsCandidates[i] > connectCount) {
                        candidateConnect = NumConnectionsCandidates[i];
                        break;
                    }
                }

                float mean = denoisedImage.GetPixel(col, row).Average;
                float recipMeanSquare = mean == 0.0f ? 1 : (1.0f / (mean * mean));

                foreach (var (c, moment) in momentImages) {
                    // Filter out candidates that match the per-pixel decision, except the path tracer
                    if (c.NumLightPaths != 0) {
                        if (c.Merge != mergeDecision)
                            continue;
                        if (UserPerPixelConnections && c.NumConnections != candidateConnect)
                            continue;
                    }

                    var g = c;
                    g.Merge = false;
                    if (UserPerPixelConnections) g.NumConnections = 0;

                    relMoments[g] += moment.Query(col, row) * recipMeanSquare * recipNumPixels;
                    costs[g] += CostHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections, mergeProb);
                }
            }
        });

        // Find the best candidate in a simple linear search
        float bestWorkNorm = float.MaxValue;
        Candidate bestCandidate = new();
        foreach (var (candidate, moment) in relMoments) {
            float cost = costs[candidate];
            float workNorm = moment * cost;
            if (workNorm < bestWorkNorm) {
                bestWorkNorm = workNorm;
                bestCandidate = candidate;
            }
        }

        Scene.FrameBuffer.MetaData["PerImageDecision"] = bestCandidate;
        Console.WriteLine(bestCandidate);
        Console.WriteLine(bestWorkNorm);
    }

    protected override void OnStartIteration(uint iteration) {
        base.OnStartIteration(iteration);

        if (iteration == 0) InitCandidates();

        CostHeuristic.UpdateStats(Scene.FrameBuffer.Width * Scene.FrameBuffer.Height, NumLightPaths.Value,
            AverageCameraPathLength, AverageLightPathLength, AveragePhotonsPerQuery);

        var timer = Stopwatch.StartNew();

        // Only update the denoised ground truth twice (it's expensive)
        if (iteration > 0 && iteration < 2) {
            DenoiseBuffers.Denoise();
            denoisedImage = Scene.FrameBuffer.GetLayer("denoised").Image as RgbImage;
        }

        if (iteration > 0) {
            OptimizePerPixel();
            OptimizePerImage();
        }

        if (iteration == 0)
            Scene.FrameBuffer.MetaData["OptimizerTime"] = 0L;
        else
            Scene.FrameBuffer.MetaData["OptimizerTime"] += timer.ElapsedMilliseconds;
    }

    protected override void OnAfterRender() {
        base.OnAfterRender();

        if (WriteMomentImages) {
            var layers = new (string, ImageBase)[momentImages.Count];
            int i = 0;
            foreach (var (c, img) in momentImages) {
                img.Scale(1.0f / Scene.FrameBuffer.CurIteration);
                layers[i++] = (c.ToString(), img.ToImage());
            }
            Layers.WriteToExr(Scene.FrameBuffer.Basename + "Moments.exr", layers);
        }
    }

    protected override void OnMomentSample(RgbColor weight, float kernelWeight, int pathLength,
                                           ProxyWeights proxyWeights, Vector2 pixel) {
        // We compute the second moment of the average value across all color channels.
        float w2 = weight.Average * weight.Average * kernelWeight;

        // Precompute terms where possible
        float lt = proxyWeights.LightTracing / NumLightPathsProxyStrategy;
        float con = proxyWeights.Connections / NumConnectionsProxy;
        float curMerge = GetPerPixelMergeProbability(pixel);

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
            Debug.Assert(float.IsFinite(correctionFactor));
            Debug.Assert(correctionFactor > 0);

            img.AtomicAdd((int)pixel.X, (int)pixel.Y, correctionFactor * w2);
        }
    }
}