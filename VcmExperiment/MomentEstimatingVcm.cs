namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Base class for a VCM integrator that computes estimates of the second moments of different MIS strategies.<br/>
///
/// For each sample of each technique, the MIS weights of the proxy strategy are computed, and a virtual
/// method is invoked. Derived classes can implement the <see cref="OnMomentSample" /> method to estimate the
/// desired moments or derivatives from the pre-computed data.
/// </summary>
public class MomentEstimatingVcm : PathLengthEstimatingVcm {
    /// <summary>
    /// Product of selection probability and sample count for connections in the proxy strategy.
    /// The probability depends on the actual number of light subpath vertices, which we cannot know.
    /// It is approximated via the current estimate / initial guess of the average path length.
    /// </summary>
    protected float BidirSelectDensityProxy {
        get {
            if (AverageCameraPathLength == 0) // No path length statistics have been computed yet
                return NumConnectionsProxy / 5.0f;

            if (AverageLightPathLength == 0) // No light paths have been traced
                return NumConnectionsProxy / AverageCameraPathLength;

            return NumConnectionsProxy / AverageLightPathLength;
        }
    }

    /// <summary>
    /// Number of connections in the proxy strategy. Arbitrary but around the same order as the typical values.
    /// </summary>
    protected int NumConnectionsProxy => 4;

    /// <summary>
    /// Number of light paths in the proxy strategy. Typically the same order of magnitude as the number of
    /// pixels, so equal to those.
    /// </summary>
    protected int NumLightPathsProxyStrategy => Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;

    /// <summary>
    /// Set to false (in-between iterations) to stop updating the moment estimates, which reduces the overhead.
    /// </summary>
    protected virtual bool UpdateEstimates { get; } = true;

    /// <summary>
    /// Stores the MIS weights of the proxy strategy used to compute the correction factor delta
    /// </summary>
    protected struct ProxyWeights {
        /// <summary>
        /// Sum of the MIS weights for next event and BSDF sampling
        /// </summary>
        public float PathTracing { get; init; }

        /// <summary>
        /// Sum of the MIS weights of all connection techniques
        /// </summary>
        public float Connections { get; init; }

        /// <summary>
        /// MIS weight of the light tracing technique
        /// </summary>
        public float LightTracing { get; init; }

        /// <summary>
        /// Sum of the MIS weights of all merging techniques
        /// </summary>
        public float Merges { get; init; }

        /// <summary>
        /// Sum of the MIS weights of all merging techniques, each multiplied by the respective correction
        /// factor from correlation-aware mis: \sum c_t w_t
        /// </summary>
        public float MergesTimesCorrelAware { get; init; }
    }

    /// <summary>
    /// Identifies a sampling technique in VCM.
    /// </summary>
    protected record struct TechIndex(int CameraEdges, int LightEdges, int TotalEdges);

    /// <summary>
    /// Called for each sample of each technique. Derived class should estimate the desired moments or
    /// derivatives.
    /// </summary>
    /// <param name="weight">Product of path contribution and MIS weights</param>
    /// <param name="kernelWeight">The value of the PM kernel for a merge, otherwise 1</param>
    /// <param name="pathLength">Number of edges along the path</param>
    /// <param name="proxyWeights">MIS weights of the proxy strategy</param>
    /// <param name="pixel">The pixel this path contributes to</param>
    protected virtual void OnMomentSample(RgbColor weight, float kernelWeight, TechIndex techIndex,
                                          ProxyWeights proxyWeights, Vector2 pixel) { }

    /// <summary>
    /// Computes the MIS weights of the proxy strategy, from which we compute the correction factors for all
    /// second moment estimators of all candidate strategies.
    /// </summary>
    /// <param name="pathPdfs">Bidirectional path sampling PDFs</param>
    /// <param name="pixel">The pixel the path contributes to</param>
    /// <param name="distToCam">Distance between the camera and the primary hit point</param>
    /// <param name="radius">The photon mapping radius used at all intersections</param>
    /// <returns>The MIS weights of the proxy strategy</returns>
    private ProxyWeights ComputeProxyWeights(BidirPathPdfs pathPdfs, Vector2 pixel, float distToCam, float radius) {
        var diffRatio = new PdfRatio(pathPdfs, radius, NumLightPathsProxyStrategy, this, distToCam);

        // Joint MIS weight of next event and hitting the light
        float ptRecip = CameraPathReciprocals(pathPdfs.NumPdfs - 2, pathPdfs, pixel, diffRatio,
            NumLightPathsProxyStrategy, BidirSelectDensityProxy, 1, radius);
        float pt = 1 / (1 + ptRecip / pathPdfs.PdfsCameraToLight[^1]);

        // MIS weight of light tracing
        float ltRecip = LightPathReciprocals(-1, pathPdfs, pixel, diffRatio, NumLightPathsProxyStrategy,
            BidirSelectDensityProxy, 1, radius);
        float lt = 1 / (1 + ltRecip / NumLightPathsProxyStrategy);

        // Sum of the MIS weights of all merging techniques
        (float vm, float vmCov) = ComputeMergeMisWithProxy(1, pathPdfs, pixel, diffRatio, radius);

        if (ltRecip == 0 && ptRecip == 0) { // catch corner case (path cannot be sampled bidirectionally)
            pt = 1;
            lt = 0;
            vm = 0;
            vmCov = 0;
        }

        // MIS weights must sum to one. We don't compute the connection weights explicitly, so make sure
        // that everything else is below one, up to some numerical error margin.
        // Debug.Assert(pt + lt + vm < 1.0f + 1e-3f);

        return new() {
            PathTracing = pt,
            Connections = Math.Clamp(1.0f - pt - lt - vm, 0, 1), // Prevent negative moments due to small numerical errors
            LightTracing = lt,
            Merges = vm,
            MergesTimesCorrelAware = vmCov,
        };
    }

    /// <returns>
    /// Sum of MIS weights for all merges along the path when using the proxy strategy, and the same sum
    /// but with each weight multiplied by the correl-aware MIS correction factor
    /// </returns>
    protected (float, float) ComputeMergeMisWithProxy(int start, BidirPathPdfs pdfs, Vector2 pixel,
                                                      PdfRatio ratio, float radius) {
        float misSum = 0;
        float misCovSum = 0;
        // This loop over all merge techniques can be optimized by interleaving it with the other gathering
        // operations along the path.
        for (int i = start; i < pdfs.NumPdfs - 1; ++i) {
            Debug.Assert(ratio[i] <= 1 + 1e-5f);

            float mergeApproximation =
                pdfs.PdfsLightToCamera[i] * MathF.PI * radius * radius * NumLightPathsProxyStrategy * ratio[i];

            float cam = CameraPathReciprocals(i, pdfs, pixel, ratio, NumLightPathsProxyStrategy,
                BidirSelectDensityProxy, 1, radius);
            float lig = LightPathReciprocals(i, pdfs, pixel, ratio, NumLightPathsProxyStrategy,
                BidirSelectDensityProxy, 1, radius);

            if (cam == 0 || lig == 0) return (0, 0);

            float sumReciprocals = 0.0f;
            sumReciprocals += cam / mergeApproximation;
            sumReciprocals += lig / mergeApproximation;

            // Ratio between the merge and the equivalent connection technique
            int lightVertexDepth = pdfs.NumPdfs - i;
            if (lightVertexDepth > 1) sumReciprocals += BidirSelectDensityProxy / mergeApproximation;

            Debug.Assert(sumReciprocals > 0);
            misSum += 1 / sumReciprocals;
            misCovSum += ratio[i] / sumReciprocals;
        }

        return (misSum, misCovSum);
    }

    protected override void OnLightTracerSample(RgbColor weight, float misWeight, Vector2 pixel,
                                                PathVertex lightVertex, float pdfCamToPrimary,
                                                float pdfReverse, float pdfNextEvent, float distToCam) {
        if (!UpdateEstimates) return;
        if (weight == RgbColor.Black) return;

        int numPdfs = lightVertex.Depth + 1;
        int lastCameraVertexIdx = -1;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);

        pathPdfs.GatherLightPdfs(lightVertex, lastCameraVertexIdx);

        pathPdfs.PdfsCameraToLight[0] = pdfCamToPrimary;
        pathPdfs.PdfsCameraToLight[1] = pdfReverse + pdfNextEvent;

        // Compute the hypothetical PT weight: the combined weight of hitting and next event
        float radius = ComputeLocalMergeRadius(distToCam);
        var proxyWeights = ComputeProxyWeights(pathPdfs, pixel, distToCam, radius);
        OnMomentSample(weight * misWeight, 1, new(0, lightVertex.Depth, numPdfs), proxyWeights, pixel);
    }

    protected override void OnNextEventSample(RgbColor weight, float misWeight, CameraPath cameraPath,
                                              float pdfEmit, float pdfNextEvent, float pdfHit,
                                              float pdfReverse, Emitter emitter, Vector3 lightToSurface,
                                              SurfacePoint lightPoint) {
        if (!UpdateEstimates) return;
        if (weight == RgbColor.Black) return;

        int numPdfs = cameraPath.Vertices.Count + 1;
        int lastCameraVertexIdx = numPdfs - 2;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);

        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);

        float pdfCombined = pdfNextEvent + pdfHit;

        pathPdfs.PdfsCameraToLight[^2] = cameraPath.Vertices[^1].PdfFromAncestor;
        pathPdfs.PdfsLightToCamera[^2] = pdfEmit;
        if (numPdfs > 2) // not for direct illumination
            pathPdfs.PdfsLightToCamera[^3] = pdfReverse;

        pathPdfs.PdfsCameraToLight[^1] = pdfCombined;
        pathPdfs.PdfsLightToCamera[^1] = 1;

        // Compute the hypothetical PT weight: the combined weight of hitting and next event
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var proxyWeights = ComputeProxyWeights(pathPdfs, cameraPath.Pixel, cameraPath.Distances[0], radius);
        OnMomentSample(weight * misWeight, 1, new(cameraPath.Vertices.Count, 0, numPdfs), proxyWeights,
            cameraPath.Pixel);
    }

    protected override void OnEmitterHitSample(RgbColor weight, float misWeight, CameraPath cameraPath,
                                               float pdfEmit, float pdfNextEvent, Emitter emitter,
                                               Vector3 lightToSurface, SurfacePoint lightPoint) {
        if (!UpdateEstimates) return;
        if (weight == RgbColor.Black) return;

        int numPdfs = cameraPath.Vertices.Count;
        int lastCameraVertexIdx = numPdfs - 1;

        if (numPdfs == 1) return; // sole technique for rendering directly visible lights.

        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);
        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);
        pathPdfs.PdfsLightToCamera[^2] = pdfEmit;
        float pdfThis = cameraPath.Vertices[^1].PdfFromAncestor;

        float pdfCombined = pdfNextEvent + pdfThis;
        pathPdfs.PdfsCameraToLight[^1] = pdfCombined;
        pathPdfs.PdfsLightToCamera[^1] = 1;

        // Compute the hypothetical PT weight: the combined weight of hitting and next event
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var proxyWeights = ComputeProxyWeights(pathPdfs, cameraPath.Pixel, cameraPath.Distances[0], radius);
        OnMomentSample(weight * misWeight, 1, new(numPdfs, 0, numPdfs), proxyWeights, cameraPath.Pixel);
    }

    protected override void OnBidirConnectSample(RgbColor weight, float misWeight, CameraPath cameraPath,
                                                 PathVertex lightVertex, float pdfCameraReverse,
                                                 float pdfCameraToLight, float pdfLightReverse,
                                                 float pdfLightToCamera, float pdfNextEvent) {
        if (!UpdateEstimates) return;
        if (weight == RgbColor.Black) return;

        int numPdfs = cameraPath.Vertices.Count + lightVertex.Depth + 1;
        int lastCameraVertexIdx = cameraPath.Vertices.Count - 1;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);
        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);
        pathPdfs.GatherLightPdfs(lightVertex, lastCameraVertexIdx);

        // Set the pdf values that are unique to this combination of paths
        if (lastCameraVertexIdx > 0) // only if this is not the primary hit point
            pathPdfs.PdfsLightToCamera[lastCameraVertexIdx - 1] = pdfCameraReverse;
        pathPdfs.PdfsCameraToLight[lastCameraVertexIdx] = cameraPath.Vertices[^1].PdfFromAncestor;
        pathPdfs.PdfsLightToCamera[lastCameraVertexIdx] = pdfLightToCamera;
        pathPdfs.PdfsCameraToLight[lastCameraVertexIdx + 1] = pdfCameraToLight;
        pathPdfs.PdfsCameraToLight[lastCameraVertexIdx + 2] = pdfLightReverse + pdfNextEvent;

        // Compute the hypothetical PT weight (next event + hitting)
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var proxyWeights = ComputeProxyWeights(pathPdfs, cameraPath.Pixel, cameraPath.Distances[0], radius);
        OnMomentSample(weight * misWeight, 1, new(cameraPath.Vertices.Count, lightVertex.Depth, numPdfs),
            proxyWeights, cameraPath.Pixel);
    }

    protected override void OnMergeSample(RgbColor weight, float kernelWeight, float misWeight,
                                          CameraPath cameraPath, PathVertex lightVertex,
                                          float pdfCameraReverse, float pdfLightReverse, float pdfNextEvent) {
        base.OnMergeSample(weight, kernelWeight, misWeight, cameraPath, lightVertex, pdfCameraReverse,
            pdfLightReverse, pdfNextEvent);

        if (!UpdateEstimates) return;
        if (weight == RgbColor.Black) return;

        int numPdfs = cameraPath.Vertices.Count + lightVertex.Depth;
        int lastCameraVertexIdx = cameraPath.Vertices.Count - 1;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);
        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);
        pathPdfs.GatherLightPdfs(lightVertex, lastCameraVertexIdx - 1);

        // Set the pdf values that are unique to this combination of paths
        if (lastCameraVertexIdx > 0) // only if this is not the primary hit point
            pathPdfs.PdfsLightToCamera[lastCameraVertexIdx - 1] = pdfCameraReverse;
        pathPdfs.PdfsLightToCamera[lastCameraVertexIdx] = lightVertex.PdfFromAncestor;
        pathPdfs.PdfsCameraToLight[lastCameraVertexIdx] = cameraPath.Vertices[^1].PdfFromAncestor;
        pathPdfs.PdfsCameraToLight[lastCameraVertexIdx + 1] = pdfLightReverse + pdfNextEvent;

        // Compute the hypothetical PT weight (next event + hitting)
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var proxyWeights = ComputeProxyWeights(pathPdfs, cameraPath.Pixel, cameraPath.Distances[0], radius);
        OnMomentSample(weight * misWeight, kernelWeight, new(cameraPath.Vertices.Count, lightVertex.Depth, numPdfs),
            proxyWeights, cameraPath.Pixel);
    }
}