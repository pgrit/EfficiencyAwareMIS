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
    public bool UsePerPixelConnections = true;

    /// <summary>
    /// If set to false, merges are optimized over the whole image instead of per-pixel
    /// </summary>
    public bool UsePerPixelMerges = true;

    /// <summary>
    /// Maximum number of iterations after which to update the sample counts
    /// </summary>
    public int MaxNumUpdates = 1;

    record struct Candidate(int NumLightPaths, int NumConnections, bool Merge) {
        public override string ToString() => $"n={NumLightPaths:000000},c={NumConnections:00},m={(Merge ? 1 : 0)}";
    }

    CostHeuristic CostHeuristic { get; set; } = new();

    Dictionary<Candidate, MonochromeImage> momentImages;
    Dictionary<Candidate, MonochromeImage> filteredMoments;
    MonochromeImage denoisedImage;

    /// <summary>
    /// Debugging visualization of the per-pixel masked global decisions. Only written if
    /// <see cref="WriteMomentImages" /> is true.
    /// </summary>
    Dictionary<Candidate, MonochromeImage> marginalizedMoments = null;

    MonochromeImage mergeMask, connectMask;

    bool? useMergesGlobal;

    protected override float GetPerPixelMergeProbability(Vector2 pixel) {
        if (!EnableMerging) return 0.0f;
        if (mergeMask == null) return useMergesGlobal.GetValueOrDefault(false) ? 1.0f : 0.0f;
        return mergeMask.GetPixel((int)pixel.X, (int)pixel.Y);
    }

    protected override float GetPerPixelConnectionCount(Vector2 pixel) {
        if (connectMask == null) return NumConnections;
        return connectMask.GetPixel((int)pixel.X, (int)pixel.Y);
    }

    void InitCandidates() {
        int width = Scene.FrameBuffer.Width;
        int height = Scene.FrameBuffer.Height;
        int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
        momentImages = new();
        filteredMoments = new();
        foreach (float nRel in NumLightPathCandidates) {
            foreach (int c in NumConnectionsCandidates) {
                int n = (int)(numPixels * nRel);
                if (EnableMerging) {
                    momentImages.Add(new(n, c, true), new MonochromeImage(width, height));
                    filteredMoments.Add(new(n, c, true), new MonochromeImage(width, height));
                }
                momentImages.Add(new(n, c, false), new MonochromeImage(width, height));
                filteredMoments.Add(new(n, c, false), new MonochromeImage(width, height));
            }
        }
        momentImages.Add(new(0, 0, false), new MonochromeImage(width, height));
        filteredMoments.Add(new(0, 0, false), new MonochromeImage(width, height));
    }

    void OptimizePerPixel() {
        // Make per-pixel decisions
        if (UsePerPixelMerges) mergeMask = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
        if (UsePerPixelConnections) connectMask = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
        Parallel.For(0, Scene.FrameBuffer.Height, row => {
            for (int col = 0; col < Scene.FrameBuffer.Width; ++col) {
                float bestWorkNorm = float.MaxValue;
                Candidate bestCandidate = new();
                foreach (var (candidate, moment) in filteredMoments) {
                    float cost = CostHeuristic.EvaluatePerPixel(candidate.NumLightPaths,
                        candidate.NumConnections, (candidate.Merge ? 1.0f : 0.0f));
                    Debug.Assert(float.IsFinite(cost));
                    Debug.Assert(cost > 0);

                    float workNorm = moment.GetPixel(col, row) * cost;

                    if (workNorm < bestWorkNorm) {
                        bestWorkNorm = workNorm;
                        bestCandidate = candidate;
                    }
                }

                if (bestWorkNorm == 0.0f) { // disable all techs in completely black pixels
                    bestCandidate = new(0, 0, false);
                }

                // Set the per-pixel decision
                if (UsePerPixelMerges) mergeMask.SetPixel(col, row, bestCandidate.Merge ? 1 : 0);
                if (UsePerPixelConnections) connectMask.SetPixel(col, row, bestCandidate.NumConnections);
            }
        });

        // Filter the sample masks
        if (UsePerPixelMerges) {
            MonochromeImage mergeMaskBuf = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
            Filter.Dilation(mergeMask, mergeMaskBuf, 16);
            Filter.RepeatedBox(mergeMaskBuf, mergeMask, 8);
        }

        if (UsePerPixelConnections) {
            MonochromeImage connectMaskBuf = new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height);
            Filter.Dilation(connectMask, connectMaskBuf, 16);
            Filter.RepeatedBox(connectMaskBuf, connectMask, 8);
        }
    }

    /// <summary>
    /// Creates buffers to store the marginalized relative moments and costs of all global sub-strategies
    /// </summary>
    (Dictionary<Candidate, float> RelMoments, Dictionary<Candidate, float> Costs) MakeBuffers() {
        // Initialize total cost and moment for all per-image candidates
        Dictionary<Candidate, float> relMoments = new();
        Dictionary<Candidate, float> costs = new();
        int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
        foreach (float nRel in NumLightPathCandidates) {
            int n = (int)(numPixels * nRel);
            if (UsePerPixelConnections) {
                relMoments[new(n, 0, false)] = 0.0f;
                if (!UsePerPixelMerges) relMoments[new(n, 0, true)] = 0.0f;

                costs[new(n, 0, false)] = 0.0f;
                if (!UsePerPixelMerges) costs[new(n, 0, true)] = 0.0f;
            } else {
                foreach (int c in NumConnectionsCandidates) {
                    relMoments[new(n, c, false)] = 0.0f;
                    if (!UsePerPixelMerges) relMoments[new(n, c, true)] = 0.0f;

                    costs[new(n, c, false)] = 0.0f;
                    if (!UsePerPixelMerges) costs[new(n, c, true)] = 0.0f;
                }
            }
        }
        // Add path tracing
        relMoments[new(0, 0, false)] = 0.0f;
        costs[new(0, 0, false)] = 0.0f;

        return (relMoments, costs);
    }

    void OptimizePerImage() {
        var (relMoments, costs) = MakeBuffers();

        // Create buffers to store the per-pixel marginalized values of the global candidates for debugging
        if (WriteMomentImages && marginalizedMoments == null) {
            marginalizedMoments = new();
            foreach (var (candidate, _) in relMoments) {
                marginalizedMoments.Add(candidate, new(Scene.FrameBuffer.Width, Scene.FrameBuffer.Height));
            }
        }

        // Accumulate relative moments and cost, using a parallel reduction with per-line buffering
        float recipNumPixels = 1.0f / (Scene.FrameBuffer.Width * Scene.FrameBuffer.Height);
        Parallel.For(0, Scene.FrameBuffer.Height, row => {
            // Allocate a buffer for this line (TODO could be made thread-local and re-use the memory)
            var lineBuffers = MakeBuffers();

            for (int col = 0; col < Scene.FrameBuffer.Width; ++col) {
                // Merges are considered enabled if there's at least a 10% probability of them happening
                float mergeProb = GetPerPixelMergeProbability(new(col, row));
                bool mergeDecision = mergeProb > 0.1f;

                // Find closest candidate for the actual number of connections
                float connectCount = GetPerPixelConnectionCount(new(col, row));
                int candidateConnect = NumConnectionsCandidates[^1];
                for (int i = 0; i < NumConnectionsCandidates.Length; ++i) {
                    if (NumConnectionsCandidates[i] >= connectCount) {
                        candidateConnect = NumConnectionsCandidates[i];
                        break;
                    }
                }

                float mean = denoisedImage.GetPixel(col, row);
                if (mean == 0.0f) continue; // skip completely black pixels
                float recipMeanSquare = 1.0f / (mean * mean);

                foreach (var (c, moment) in momentImages) {
                    // Filter out candidates that match the per-pixel decision, except the path tracer
                    if (c.NumLightPaths != 0) {
                        if (UsePerPixelMerges && c.Merge != mergeDecision)
                            continue;
                        if (UsePerPixelConnections && c.NumConnections != candidateConnect)
                            continue;
                    }

                    var g = c;
                    if (UsePerPixelMerges) g.Merge = false;
                    if (UsePerPixelConnections) g.NumConnections = 0;

                    var relMoment = moment.GetPixel(col, row);
                    var cost = CostHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections, c.Merge ? 1 : 0);

                    // Accumulate in the per-line storage. We scale by the number of pixels to get resolution
                    // independent values that are easier to interpret when debugging. Has no effect on the
                    // outcome since recipNumPixels is a constant.
                    lineBuffers.RelMoments[g] += relMoment * recipNumPixels;
                    lineBuffers.Costs[g] += cost * recipNumPixels;

                    // If the debugging buffers exist, track the last marginalized per-pixel value in them.
                    // We use "SetPixel", i.e. overwrite instead of accumulate, to not distort the values.
                    // Intermediate states are currently not kept, only the last outcome.
                    if (marginalizedMoments != null) {
                        marginalizedMoments[g].SetPixel(col, row, relMoment * cost);
                    }
                }
            }

            lock (relMoments) {
                foreach (var (c, v) in lineBuffers.RelMoments) {
                    relMoments[c] += v;
                }
                foreach (var (c, v) in lineBuffers.Costs) {
                    costs[c] += v;
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

        NumLightPaths = bestCandidate.NumLightPaths;
        if (!UsePerPixelConnections) NumConnections = bestCandidate.NumConnections;
        if (!UsePerPixelMerges) useMergesGlobal = bestCandidate.Merge;
    }

    protected override void OnStartIteration(uint iteration) {
        base.OnStartIteration(iteration);

        if (iteration == 0) InitCandidates();
        else if (iteration < MaxNumUpdates)
            foreach (var (_, img) in momentImages) img.Scale(iteration / (iteration + 1.0f));
    }

    protected override void OnEndIteration(uint iteration) {
        base.OnEndIteration(iteration);

        if (iteration + 1 > MaxNumUpdates) return;

        var timer = Stopwatch.StartNew();

        CostHeuristic.UpdateStats(Scene.FrameBuffer.Width * Scene.FrameBuffer.Height, NumLightPaths.Value,
            AverageCameraPathLength, AverageLightPathLength, AveragePhotonsPerQuery);

        // Only update the denoised ground truth once (it's expensive)
        if (iteration == 0) {
            DenoiseBuffers.Denoise();
            var img = Scene.FrameBuffer.GetLayer("denoised").Image as RgbImage;
            denoisedImage = new(img, MonochromeImage.RgbConvertMode.Average);

            // Make the current absolute moments of the first iteration relative
            foreach (var (c, moment) in momentImages) {
                for (int row = 0; row < Scene.FrameBuffer.Height; ++row) {
                    for (int col = 0; col < Scene.FrameBuffer.Width; ++col) {
                        float mean = img.GetPixel(col, row).Average;
                        if (mean == 0) continue;
                        moment.SetPixel(col, row, moment.GetPixel(col, row) / (mean * mean));
                    }
                }
            }
        }

        // Reduce noise in the second moments via a simple lowpass filter
        foreach (var (candidate, moment) in momentImages) {
            Filter.RepeatedBox(moment, filteredMoments[candidate], 16);
        }

        OptimizePerPixel();
        OptimizePerImage();

        if (iteration == 0)
            Scene.FrameBuffer.MetaData["OptimizerTime"] = timer.ElapsedMilliseconds;
        else
            Scene.FrameBuffer.MetaData["OptimizerTime"] += timer.ElapsedMilliseconds;
    }

    protected override void OnAfterRender() {
        base.OnAfterRender();

        if (WriteMomentImages) {
            var layers = new (string, ImageBase)[filteredMoments.Count + marginalizedMoments.Count];
            int i = 0;
            foreach (var (c, img) in filteredMoments) {
                // Multiply the cost heuristic on all pixel moments (this pollutes the values, but rendering
                // is done already)
                var cost = CostHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections, (c.Merge ? 1.0f : 0.0f));
                img.Scale(cost);
                layers[i++] = (c.ToString(), img);
            }
            foreach (var (c, img) in marginalizedMoments) {
                layers[i++] = ($"global-{c.ToString()}", img);
            }
            Layers.WriteToExr(Scene.FrameBuffer.Basename + "Moments.exr", layers);
        }

        // Write either or both of merge and connect sample mask, depending on which got created
        List<(string, ImageBase)> masks = new();
        if (mergeMask != null) masks.Add(("merge", mergeMask));
        if (connectMask != null) masks.Add(("connect", connectMask));
        if (masks.Count != 0) Layers.WriteToExr(Scene.FrameBuffer.Basename + "Masks.exr", masks.ToArray());
    }

    protected override bool UpdateEstimates => Scene.FrameBuffer.CurIteration <= MaxNumUpdates;

    protected override void OnMomentSample(RgbColor weight, float kernelWeight, int pathLength,
                                           ProxyWeights proxyWeights, Vector2 pixel) {
        // We compute the second moment of the average value across all color channels.
        float w2 = weight.Average * weight.Average * kernelWeight / Scene.FrameBuffer.CurIteration;

        if (denoisedImage != null) { // Compute relative moments if we have a denoised reference
            float mean = denoisedImage.GetPixel((int)pixel.X, (int)pixel.Y);
            if (mean != 0) w2 /= mean * mean;
        }

        // Precompute terms where possible
        float lt = proxyWeights.LightTracing / NumLightPathsProxyStrategy;
        float con = proxyWeights.Connections / NumConnectionsProxy;
        float curMerge = GetPerPixelMergeProbability(pixel);
        float curConnect = GetPerPixelConnectionCount(pixel);

        // Update the second moment estimates of all candidates.
        foreach (var (candidate, img) in momentImages) {
            // Proxy weights multiplied by pilot sample count, divided by proxy sample count
            float a = proxyWeights.PathTracing
                + lt * NumLightPaths.Value
                + con * curConnect
                + proxyWeights.Merges * curMerge;

            // Same, but multiplied with correl-aware correction factors
            float b = proxyWeights.PathTracing
                + lt * NumLightPaths.Value
                + con * curConnect
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