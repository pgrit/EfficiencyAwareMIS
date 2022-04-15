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
    /// If set to true, writes all candidate moment images to a layered .exr file after rendering is done,
    /// generates and writes images of global marginalized quantities, and outputs history of global values.
    /// </summary>
    public bool WriteDebugInfo = false;

    /// <summary>
    /// If set to true, moments are accumulated by the optimizer is never run
    /// </summary>
    public bool OnlyAccumulate = false;

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

    /// <summary>
    /// Determines after how many rendering iterations an update step is done. The i-th update is performed
    /// after ExponentialUpdateFrequency^i rendering iterations.
    /// </summary>
    public int ExponentialUpdateFrequency = 4;

    /// <summary>
    /// Filters the per-pixel moments to reduce noise
    /// </summary>
    /// <param name="input">The unfiltered per-pixel moments. Must not be written to.</param>
    /// <param name="filtered">Pre-allocated memory for the filtered result</param>
    public virtual void FilterMoments(MonochromeImage input, MonochromeImage filtered)
    => Filter.RepeatedBox(input, filtered, 3);

    /// <summary>
    /// Filters the per-pixel merging decisions and writes the result to the same mask.
    /// </summary>
    /// <param name="mask">Input / Output merge mask</param>
    public virtual void FilterMergeMask(ref MonochromeImage mask) {
        MonochromeImage buf = new(mask.Width, mask.Height);
        Filter.Dilation(mask, buf, 16);
        Filter.RepeatedBox(buf, mask, 16);
    }

    /// <summary>
    /// Filters the per-pixel connection counts and writes the result to the same mask.
    /// </summary>
    /// <param name="mask">Input / Output connection mask</param>
    public virtual void FilterConnectMask(ref MonochromeImage mask) {
        MonochromeImage buf = new(mask.Width, mask.Height);
        Filter.Dilation(mask, buf, 3);
        Filter.RepeatedBox(buf, mask, 16);
    }

    CostHeuristic CostHeuristic { get; set; } = new();

    int numUpdates = 0;

    Dictionary<Candidate, MonochromeImage> momentImages;
    Dictionary<Candidate, MonochromeImage> filteredMoments;
    MonochromeImage denoisedImage;

    /// <summary>
    /// Debugging visualization of the per-pixel masked global decisions. Only written if
    /// <see cref="WriteDebugInfo" /> is true.
    /// </summary>
    Dictionary<Candidate, MonochromeImage> marginalizedMoments = null;

    Dictionary<Candidate, MonochromeImage> marginalizedCosts = null;

    public MonochromeImage MergeMask = null;
    public MonochromeImage ConnectMask = null;

    bool? useMergesGlobal;

    protected override float GetPerPixelMergeProbability(Vector2 pixel) {
        if (!EnableMerging) return 0.0f;
        if (MergeMask == null) return useMergesGlobal.GetValueOrDefault(true) ? 1.0f : 0.0f;
        return MergeMask.GetPixel((int)pixel.X, (int)pixel.Y);
    }

    protected override float GetPerPixelConnectionCount(Vector2 pixel) {
        if (ConnectMask == null) return NumConnections;
        return ConnectMask.GetPixel((int)pixel.X, (int)pixel.Y);
    }

    void InitCandidates() {
        int width = Scene.FrameBuffer.Width;
        int height = Scene.FrameBuffer.Height;
        int numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;

        momentImages = new();
        filteredMoments = new();

        // Allocates all requred buffers for a candidate
        void AddCandidate(Candidate candidate) {
            momentImages.Add(candidate, new MonochromeImage(width, height));
            filteredMoments.Add(candidate, new MonochromeImage(width, height));
        }

        // All combinations of connection counts and numbers of light paths
        foreach (float nRel in NumLightPathCandidates) {
            foreach (int c in NumConnectionsCandidates) {
                int n = (int)(numPixels * nRel);
                if (EnableMerging) AddCandidate(new(n, c, true));
                AddCandidate(new(n, c, false));
            }
        }

        // Pure path tracer: 0 light paths are only allowed if merges and connections are also disabled
        AddCandidate(new(0, 0, false));

        numUpdates = 0;
        Scene.FrameBuffer.MetaData["PerImageDecision"] = new List<Candidate>();
    }

    protected override void OnStartIteration(uint iteration) {
        base.OnStartIteration(iteration);

        if (iteration == 0) InitCandidates();
        else if (numUpdates < MaxNumUpdates) {
            foreach (var (c, img) in momentImages) {
                img.Scale(iteration / (iteration + 1.0f));
            }
        }
    }

    protected override void OnEndIteration(uint iteration) {
        base.OnEndIteration(iteration);

        if (numUpdates + 1 > MaxNumUpdates || OnlyAccumulate) return;
        int targetCount = (int)Math.Pow(ExponentialUpdateFrequency, numUpdates);
        if (iteration + 1 < targetCount) return;
        numUpdates++;

        var timer = Stopwatch.StartNew();

        CostHeuristic.UpdateStats(Scene.FrameBuffer.Width * Scene.FrameBuffer.Height, NumLightPaths.Value,
            AverageCameraPathLength, AverageLightPathLength, AveragePhotonsPerQuery);

        // Only update the denoised ground truth once (it's expensive)
        if (numUpdates == 1) {
            DenoiseBuffers.Denoise();
            var img = Scene.FrameBuffer.GetLayer("denoised").Image as RgbImage;
            denoisedImage = new(img, MonochromeImage.RgbConvertMode.Average);

            // Apply the same blur to the denoised image as is applied to the second moments. This avoids
            // artifacts at discontinuities with very high contrast.
            MonochromeImage buf = new(img.Width, img.Height);
            FilterMoments(denoisedImage, buf);
            denoisedImage = buf;
        }

        // Reduce noise in the second moments via a simple lowpass filter
        Parallel.ForEach(momentImages, (elem) => {
            var (candidate, img) = elem;
            FilterMoments(img, filteredMoments[candidate]);
        });

        VcmOptimizer.OptimizePerPixel(filteredMoments, CostHeuristic, UsePerPixelMerges, UsePerPixelConnections,
            out MergeMask, out ConnectMask);

        if (MergeMask != null) FilterMergeMask(ref MergeMask);
        if (ConnectMask != null) FilterConnectMask(ref ConnectMask);

        var (n, c, m) = VcmOptimizer.OptimizePerImage(filteredMoments, denoisedImage, NumLightPathCandidates,
            NumConnectionsCandidates, CostHeuristic, (col, row) => GetPerPixelMergeProbability(new(col, row)),
            (col, row) => GetPerPixelConnectionCount(new(col, row)), UsePerPixelConnections, UsePerPixelMerges);

        // Apply the optimized global sample counts
        NumLightPaths = n;
        NumConnections = c ?? NumConnections;
        useMergesGlobal = m;

        Scene.FrameBuffer.MetaData["PerImageDecision"].Add(new Candidate(n, c, m));
        if (numUpdates == 1)
            Scene.FrameBuffer.MetaData["OptimizerTime"] = timer.ElapsedMilliseconds;
        else
            Scene.FrameBuffer.MetaData["OptimizerTime"] += timer.ElapsedMilliseconds;
    }

    protected override void OnAfterRender() {
        base.OnAfterRender();

        if (WriteDebugInfo) {
            int num = momentImages.Count + (marginalizedMoments?.Count ?? 0) + (marginalizedCosts?.Count ?? 0);
            var layers = new (string, ImageBase)[num];

            int i = 0;
            foreach (var (c, img) in momentImages) layers[i++] = (c.ToString(), img);
            if (marginalizedMoments != null) {
                foreach (var (c, img) in marginalizedMoments) {
                    layers[i++] = ($"global-{c.ToString()}", img);
                    layers[i++] = ($"global-cost-{c.ToString()}", marginalizedCosts[c]);
                }
            }

            Layers.WriteToExr(Scene.FrameBuffer.Basename + "Moments.exr", layers);
        }

        // Write either or both of merge and connect sample mask, depending on which got created
        List<(string, ImageBase)> masks = new();
        if (MergeMask != null) masks.Add(("merge", MergeMask));
        if (ConnectMask != null) masks.Add(("connect", ConnectMask));
        if (masks.Count != 0) Layers.WriteToExr(Scene.FrameBuffer.Basename + "Masks.exr", masks.ToArray());
    }

    protected override bool UpdateEstimates => numUpdates < MaxNumUpdates;

    protected override void OnMomentSample(RgbColor weight, float kernelWeight, TechIndex techIndex,
                                             ProxyWeights proxyWeights, Vector2 pixel) {
        // We compute the second moment of the average value across all color channels.
        float w2 = weight.Average * weight.Average * kernelWeight / Scene.FrameBuffer.CurIteration;

        // Precompute terms where possible
        float lt = proxyWeights.LightTracing / NumLightPathsProxyStrategy;
        float con = proxyWeights.Connections / NumConnectionsProxy;
        float curMerge = GetPerPixelMergeProbability(pixel);
        float curConnect = GetPerPixelConnectionCount(pixel);

        int col = (int)pixel.X;
        int row = (int)pixel.Y;

        Debug.Assert(float.IsFinite(w2));

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
                + con * candidate.NumConnections.Value
                + proxyWeights.Merges * (candidate.Merge.Value ? 1.0f : 0.0f);

            // Same, but multiplied with correl-aware correction factors
            float d = proxyWeights.PathTracing
                + lt * candidate.NumLightPaths
                + con * candidate.NumConnections.Value
                + proxyWeights.MergesTimesCorrelAware * (candidate.Merge.Value ? 1.0f : 0.0f);

            float correctionFactor = (a * a * d) / (c * c * b);
            Debug.Assert(float.IsFinite(correctionFactor));
            Debug.Assert(correctionFactor > 0);

            img.AtomicAdd(col, row, correctionFactor * w2);
        }
    }
}