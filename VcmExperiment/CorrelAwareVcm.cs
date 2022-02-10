namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Slightly modified implementation of correlation-aware MIS for VCM, with a flag to revert to classic balance.
/// </summary>
public class CorrelAwareVcm : VertexConnectionAndMerging {
    abstract class RadiusInitializer {
        public abstract float ComputeRadius(float sceneRadius, float primaryPdf, float primaryDistance);
    }

    class RadiusInitFov : RadiusInitializer {
        public float ScalingFactor;
        public override float ComputeRadius(float sceneRadius, float primaryPdf, float primaryDistance)
        => primaryDistance * ScalingFactor;
    }

    RadiusInitializer CorrelAwareRadius = new RadiusInitFov { ScalingFactor = MathF.Tan(MathF.PI / 180) };

    /// <summary>
    /// If true, disables correl-aware weighting
    /// </summary>
    public bool DisableCorrelAware = false;

    /// <summary>
    /// Gathers the pdfs used for correl-aware weights and computes the heuristic value
    /// </summary>
    protected readonly ref struct PdfRatio {
        public readonly Span<float> cameraToLight;
        public readonly Span<float> lightToCamera;
        readonly CorrelAwareVcm parent;
        readonly int numPaths;

        public PdfRatio(BidirPathPdfs pdfs, float radius, int numPaths, CorrelAwareVcm parent,
                        float distToCam) {
            this.numPaths = numPaths;
            this.parent = parent;

            if (parent.CorrelAwareRadius == null || parent.DisableCorrelAware) {
                cameraToLight = null;
                lightToCamera = null;
                return;
            }

            radius = parent.CorrelAwareRadius.ComputeRadius(parent.Scene.Radius,
                pdfs.PdfsCameraToLight[0], distToCam);

            int numSurfaceVertices = pdfs.PdfsCameraToLight.Length - 1;
            cameraToLight = new float[numSurfaceVertices];
            lightToCamera = new float[numSurfaceVertices];

            float acceptArea = radius * radius * MathF.PI;

            // Gather camera probability
            float product = 1.0f;
            for (int i = 0; i < numSurfaceVertices; ++i) {
                float next = pdfs.PdfsCameraToLight[i] * acceptArea;
                next = MathF.Min(next, 1.0f);
                product *= next;
                cameraToLight[i] = product;
            }

            // Gather light probability
            product = 1.0f;
            for (int i = numSurfaceVertices - 1; i >= 0; --i) {
                float next = pdfs.PdfsLightToCamera[i] * acceptArea;

                if (i == numSurfaceVertices - 1)
                    next *= acceptArea; // TODO unless environment map! (in that case: different radius approach)

                next = MathF.Min(next, 1.0f);
                product *= next;
                lightToCamera[i] = product;
            }
        }

        public float this[int idx] {
            get {
                if (idx == 0) return 1; // primary merges are not correlated
                if (numPaths == 0) return 1; // merging is disabled, don't touch anything!

                if (parent.DisableCorrelAware) return 1;

                float light = lightToCamera[idx];
                float cam = cameraToLight[idx];

                // Prevent NaNs
                if (cam == 0 && light == 0)
                    return 1;

                float ratio = cam / (cam + light - cam * light);
                ratio = MathF.Max(ratio, 1.0f / numPaths);

                Debug.Assert(float.IsFinite(ratio));

                return ratio;
            }
        }
    }

    public override float MergeMis(CameraPath cameraPath, PathVertex lightVertex, float pdfCameraReverse,
                                   float pdfLightReverse, float pdfNextEvent) {
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

        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);

        // Compute our additional heuristic values
        var correlRatio = new PdfRatio(pathPdfs, radius, NumLightPaths.Value, this, cameraPath.Distances[0]);

        // Compute the acceptance probability approximation
        float mergeApproximation = pathPdfs.PdfsLightToCamera[lastCameraVertexIdx]
                                 * MathF.PI * radius * radius * NumLightPaths.Value;
        mergeApproximation *= correlRatio[lastCameraVertexIdx];

        if (mergeApproximation == 0.0f) return 0.0f;

        // Compute reciprocals for hypothetical connections along the camera sub-path
        float sumReciprocals = 0.0f;
        sumReciprocals +=
            CameraPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / mergeApproximation;
        sumReciprocals +=
            LightPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / mergeApproximation;

        // Add the reciprocal for the connection that replaces the last light path edge
        if (lightVertex.Depth > 1 && NumConnections > 0)
            sumReciprocals += BidirSelectDensity() / mergeApproximation;

        return 1 / sumReciprocals;
    }

    public override float EmitterHitMis(CameraPath cameraPath, float pdfEmit, float pdfNextEvent) {
        int numPdfs = cameraPath.Vertices.Count;
        int lastCameraVertexIdx = numPdfs - 1;

        if (numPdfs == 1) return 1.0f; // sole technique for rendering directly visible lights.

        // Gather the pdfs
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];
        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);
        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);
        pathPdfs.PdfsLightToCamera[^2] = pdfEmit;

        // Compute our additional heuristic values
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var correlRatio = new PdfRatio(pathPdfs, radius, NumLightPaths.Value, this, cameraPath.Distances[0]);

        float pdfThis = cameraPath.Vertices[^1].PdfFromAncestor;

        // Compute the actual weight
        float sumReciprocals = 1.0f;

        // Next event estimation
        sumReciprocals += pdfNextEvent / pdfThis;

        // All connections along the camera path
        sumReciprocals +=
            CameraPathReciprocals(lastCameraVertexIdx - 1, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / pdfThis;

        return 1 / sumReciprocals;
    }

    public override float LightTracerMis(PathVertex lightVertex, float pdfCamToPrimary, float pdfReverse,
                                         float pdfNextEvent, Vector2 pixel, float distToCam) {
        int numPdfs = lightVertex.Depth + 1;
        int lastCameraVertexIdx = -1;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);
        pathPdfs.GatherLightPdfs(lightVertex, lastCameraVertexIdx);
        pathPdfs.PdfsCameraToLight[0] = pdfCamToPrimary;
        pathPdfs.PdfsCameraToLight[1] = pdfReverse + pdfNextEvent;

        // Compute our additional heuristic values
        float radius = ComputeLocalMergeRadius(distToCam);
        var correlRatio = new PdfRatio(pathPdfs, radius, NumLightPaths.Value, this, distToCam);

        // Compute the actual weight
        float sumReciprocals =
            LightPathReciprocals(lastCameraVertexIdx, pathPdfs, pixel, correlRatio, radius);
        sumReciprocals /= NumLightPaths.Value;
        sumReciprocals += 1;

        return 1 / sumReciprocals;
    }

    public override float BidirConnectMis(CameraPath cameraPath, PathVertex lightVertex,
                                          float pdfCameraReverse, float pdfCameraToLight,
                                          float pdfLightReverse, float pdfLightToCamera,
                                          float pdfNextEvent) {
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

        // Compute our additional heuristic values
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var correlRatio = new PdfRatio(pathPdfs, radius, NumLightPaths.Value, this, cameraPath.Distances[0]);

        // Compute reciprocals for hypothetical connections along the camera sub-path
        float sumReciprocals = 1.0f;
        sumReciprocals +=
            CameraPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / BidirSelectDensity();
        sumReciprocals +=
            LightPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / BidirSelectDensity();

        return 1 / sumReciprocals;
    }

    public override float NextEventMis(CameraPath cameraPath, float pdfEmit, float pdfNextEvent,
                                       float pdfHit, float pdfReverse) {
        int numPdfs = cameraPath.Vertices.Count + 1;
        int lastCameraVertexIdx = numPdfs - 2;
        Span<float> camToLight = stackalloc float[numPdfs];
        Span<float> lightToCam = stackalloc float[numPdfs];

        var pathPdfs = new BidirPathPdfs(LightPaths.PathCache, lightToCam, camToLight);

        pathPdfs.GatherCameraPdfs(cameraPath, lastCameraVertexIdx);

        pathPdfs.PdfsCameraToLight[^2] = cameraPath.Vertices[^1].PdfFromAncestor;
        pathPdfs.PdfsLightToCamera[^2] = pdfEmit;
        if (numPdfs > 2) // not for direct illumination
            pathPdfs.PdfsLightToCamera[^3] = pdfReverse;

        // Compute our additional heuristic values
        float radius = ComputeLocalMergeRadius(cameraPath.Distances[0]);
        var correlRatio = new PdfRatio(pathPdfs, radius, NumLightPaths.Value, this, cameraPath.Distances[0]);

        // Compute the actual weight
        float sumReciprocals = 1.0f;

        // Hitting the light source
        if (EnableHitting) sumReciprocals += pdfHit / pdfNextEvent;

        // All bidirectional connections
        sumReciprocals +=
            CameraPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / pdfNextEvent;

        Debug.Assert(sumReciprocals != 0 && float.IsFinite(sumReciprocals));

        return 1 / sumReciprocals;
    }

    /// <summary>
    /// Computes the sum of ratios of PDFs and sample counts used to compute the balance heuristic
    /// </summary>
    /// <param name="lastCameraVertexIdx">Index of the last vertex along the camera subpath</param>
    /// <param name="pdfs">The bidirectional path pdfs</param>
    /// <param name="pixel">Pixel the paath contributes to</param>
    /// <param name="correlRatio">Correlation-aware MIS</param>
    /// <param name="radius">photon mapping radius used for all vertices along the path</param>
    /// <returns>Sum of PDF * sampleCount ratios</returns>
    protected virtual float CameraPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs,
                                                  Vector2 pixel, PdfRatio correlRatio, float radius) {
        float sumReciprocals = 0.0f;
        float nextReciprocal = 1.0f;
        for (int i = lastCameraVertexIdx; i > 0; --i) {
            // Merging at this vertex
            if (EnableMerging) {
                float acceptProb = pdfs.PdfsLightToCamera[i] * MathF.PI * radius * radius;
                acceptProb *= correlRatio[i];
                sumReciprocals += nextReciprocal * NumLightPaths.Value * acceptProb;
            }

            nextReciprocal *= pdfs.PdfsLightToCamera[i] / pdfs.PdfsCameraToLight[i];

            // Connecting this vertex to the next one along the camera path
            if (NumConnections > 0) sumReciprocals += nextReciprocal * BidirSelectDensity();

            Debug.Assert(float.IsFinite(sumReciprocals));
        }

        // Light tracer
        if (EnableLightTracer) sumReciprocals +=
            nextReciprocal * pdfs.PdfsLightToCamera[0] / pdfs.PdfsCameraToLight[0] * NumLightPaths.Value;

        // Merging directly visible (almost the same as the light tracer!)
        if (MergePrimary) sumReciprocals +=
            nextReciprocal * NumLightPaths.Value * pdfs.PdfsLightToCamera[0] * MathF.PI * radius * radius;

        Debug.Assert(float.IsFinite(sumReciprocals));

        return sumReciprocals;
    }

    /// <summary>
    /// Computes the sum of ratios of PDFs and sample counts used to compute the balance heuristic
    /// </summary>
    /// <param name="lastCameraVertexIdx">Index of the last vertex along the camera subpath</param>
    /// <param name="pdfs">The bidirectional path pdfs</param>
    /// <param name="pixel">Pixel the paath contributes to</param>
    /// <param name="correlRatio">Correlation-aware MIS</param>
    /// <param name="radius">photon mapping radius used for all vertices along the path</param>
    /// <returns>Sum of PDF * sampleCount ratios</returns>
    protected virtual float LightPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs,
                                                 Vector2 pixel, PdfRatio correlRatio, float radius) {
        float sumReciprocals = 0.0f;
        float nextReciprocal = 1.0f;
        for (int i = lastCameraVertexIdx + 1; i < pdfs.NumPdfs; ++i) {
            if (i < pdfs.NumPdfs - 1 && (MergePrimary || i > 0)) { // no merging on the emitter itself
                                                              // Account for merging at this vertex
                if (EnableMerging) {
                    float acceptProb = pdfs.PdfsCameraToLight[i] * MathF.PI * radius * radius;
                    acceptProb *= correlRatio[i];
                    sumReciprocals += nextReciprocal * NumLightPaths.Value * acceptProb;
                }
            }

            nextReciprocal *= pdfs.PdfsCameraToLight[i] / pdfs.PdfsLightToCamera[i];

            // Account for connections from this vertex to its ancestor
            if (i < pdfs.NumPdfs - 2) // Connections to the emitter (next event) are treated separately
                if (NumConnections > 0) sumReciprocals += nextReciprocal * BidirSelectDensity();
        }
        // Next event and hitting the emitter directly
        if (NumShadowRays > 0 || EnableHitting) sumReciprocals += nextReciprocal;
        return sumReciprocals;
    }
}