namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// A VCM integrator where every pixel performs merging only with a certain probability. The actual logic
/// of how to decide the sample counts is implemented in the derived class by overriding
/// <see cref="GetPerPixelMergeProbability"/> and <see cref="GetPerPixelConnectionCount"/>.
/// The default uses constant values based on the global parameters from the base class.
/// </summary>
public class SampleMaskVcm : CorrelAwareVcm {
    long photonBuildTime;
    long ltShadowTime;

    protected override void OnBeforeRender() {
        base.OnBeforeRender();
        photonBuildTime = 0;
        ltShadowTime = 0;
    }

    protected override void OnAfterRender() {
        base.OnAfterRender();

        Scene.FrameBuffer.MetaData["PhotonBuildTime"] = photonBuildTime;
        Scene.FrameBuffer.MetaData["LightTracerShadowTime"] = ltShadowTime;
    }

    protected virtual float GetPerPixelMergeProbability(Vector2 pixel) {
        if (!EnableMerging) return 0.0f;
        return 1.0f;
    }

    protected virtual float GetPerPixelConnectionCount(Vector2 pixel) {
        return NumConnections;
    }

    public override float BidirSelectDensity(Vector2 pixel) {
        if (vertexSelector.Count == 0) return 0;

        // We select light path vertices uniformly
        float selectProb = 1.0f / vertexSelector.Count;

        // There are "NumLightPaths" samples that could have generated the selected vertex,
        // we repeat the process "NumConnections" times
        float numSamples = GetPerPixelConnectionCount(pixel) * NumLightPaths.Value;

        return selectProb * numSamples;
    }

    protected override void ProcessPathCache() {
        var timer = Stopwatch.StartNew();

        // always create vertex cache, we don't bother checking all pixels whether they actually do connections
        vertexSelector = new VertexSelector(LightPaths.PathCache);
        if (EnableLightTracer) SplatLightVertices();

        ltShadowTime += timer.ElapsedMilliseconds;
        timer.Restart();

        if (EnableMerging) {
            int index = 0;
            photons.Clear();
            photonMap.Clear();
            for (int i = 0; i < LightPaths.NumPaths; ++i) {
                for (int k = 1; k < LightPaths.PathCache.Length(i); ++k) {
                    var vertex = LightPaths.PathCache[i, k];
                    if (vertex.Depth >= 1 && vertex.Weight != RgbColor.Black) {
                        photonMap.AddPoint(vertex.Point.Position, index++);
                        photons.Add((i, k));
                    }
                }
            }
            photonMap.Build();
        }
        photonBuildTime += timer.ElapsedMilliseconds;
    }

    /// <summary>
    /// Turns a real-valued sample count into an integer one. Fractional part is rounded stochastically. I.e.
    /// 1.7 yields 2 with a 70% probability and 1 with a 30% probability.
    /// </summary>
    /// <param name="rng">Random number generator</param>
    /// <param name="value">The real-valued sample count</param>
    /// <returns>Stochastically rounded integer</returns>
    int StochasticRound(RNG rng, float value) {
        int num = (int)value;
        float residual = value - num;
        if (rng.NextFloat() < residual) num++;
        return num;
    }

    /// <inheritdoc />
    protected override RgbColor OnCameraHit(CameraPath path, RNG rng, Ray ray, SurfacePoint hit,
                                            float pdfFromAncestor, RgbColor throughput, int depth,
                                            float toAncestorJacobian) {
        RgbColor value = RgbColor.Black;

        // Was a light hit?
        Emitter light = Scene.QueryEmitter(hit);
        if (light != null && EnableHitting && depth >= MinDepth) {
            value += throughput * OnEmitterHit(light, hit, ray, path, toAncestorJacobian);
        }

        // Perform connections if the maximum depth has not yet been reached
        if (depth < MaxDepth) {
            int numConnect = StochasticRound(rng, GetPerPixelConnectionCount(path.Pixel));
            RgbColor connectContrib = RgbColor.Black;
            for (int i = 0; i < numConnect; ++i)
                connectContrib += BidirConnections(hit, -ray.Direction, rng, path, toAncestorJacobian);
            value += throughput * connectContrib;

            value += throughput * PerformMerging(ray, hit, rng, path, toAncestorJacobian);
        }

        if (depth < MaxDepth && depth + 1 >= MinDepth) {
            for (int i = 0; i < NumShadowRays; ++i) {
                value += throughput * PerformNextEventEstimation(ray, hit, rng, path, toAncestorJacobian);
            }
        }

        return value;
    }

    /// <summary>
    /// Called once for each merge operation with the summed outcome of the full merge.
    /// </summary>
    protected virtual void OnCombinedMergeSample(Ray ray, SurfacePoint hit, RNG rng, CameraPath path,
                                                 float cameraJacobian, RgbColor estimate) { }

    protected override RgbColor PerformMerging(Ray ray, SurfacePoint hit, RNG rng, CameraPath path, float cameraJacobian) {
        if (path.Vertices.Count == 1 && !MergePrimary) return RgbColor.Black;

        // Randomly reject merging at this vertex based on the per-pixel probability
        float mergeProbability = GetPerPixelMergeProbability(path.Pixel);
        if (rng.NextFloat() > mergeProbability) return RgbColor.Black;
        path.Throughput *= 1.0f / mergeProbability;

        var estimate = base.PerformMerging(ray, hit, rng, path, cameraJacobian);
        OnCombinedMergeSample(ray, hit, rng, path, cameraJacobian, estimate);
        return estimate;
    }

    /// <summary>
    /// Same as the base-class, except we additionally multiply with the merge probability
    /// </summary>
    /// <inheritdoc/>
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
        float mergeAcceptProb = pathPdfs.PdfsLightToCamera[lastCameraVertexIdx]
                                 * MathF.PI * radius * radius * NumLightPaths.Value;
        mergeAcceptProb *= correlRatio[lastCameraVertexIdx];
        mergeAcceptProb *= GetPerPixelMergeProbability(cameraPath.Pixel);

        if (mergeAcceptProb == 0.0f) return 0.0f;

        // Compute reciprocals for hypothetical connections along the camera sub-path
        float sumReciprocals = 0.0f;
        sumReciprocals +=
            CameraPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / mergeAcceptProb;
        sumReciprocals +=
            LightPathReciprocals(lastCameraVertexIdx, pathPdfs, cameraPath.Pixel, correlRatio, radius)
            / mergeAcceptProb;

        // Add the reciprocal for the connection that replaces the last light path edge
        if (lightVertex.Depth > 1 && NumConnections > 0)
            sumReciprocals += BidirSelectDensity(cameraPath.Pixel) / mergeAcceptProb;

        return 1 / sumReciprocals;
    }

    protected override float CameraPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs,
                                                   Vector2 pixel, PdfRatio correlRatio, float radius) {
        float mergeProbability = GetPerPixelMergeProbability(pixel);
        float bidirDensity = BidirSelectDensity(pixel);
        return CameraPathReciprocals(lastCameraVertexIdx, pdfs, pixel, correlRatio,
                                     NumLightPaths.Value, bidirDensity, mergeProbability, radius);
    }

    protected override float LightPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs,
                                                 Vector2 pixel, PdfRatio correlRatio, float radius) {
        float mergeProbability = GetPerPixelMergeProbability(pixel);
        float bidirDensity = BidirSelectDensity(pixel);
        return LightPathReciprocals(lastCameraVertexIdx, pdfs, pixel, correlRatio,
                                    NumLightPaths.Value, bidirDensity, mergeProbability, radius);
    }

    /// <summary>
    /// Computes the ratio of PDFs and sample counts used by the balance heuristic. Allows specifying the
    /// sample counts via parameters and ignores the actual settings of the integrator.
    /// </summary>
    /// <param name="lastCameraVertexIdx">Index of the last vertex along the camera subpath in the current technique</param>
    /// <param name="pdfs">Bidirectional path pdfs</param>
    /// <param name="pixel">The pixel this path contributes to</param>
    /// <param name="correlRatio">Comptues correction factor for correl-aware MIS</param>
    /// <param name="numLightPaths">Number of light subpaths</param>
    /// <param name="bidirSelectDensity">Effective density (pdf * num) of connection re-sampling</param>
    /// <param name="mergeProbability">Probablity that merging is used at each vertex</param>
    /// <param name="radius">PM radius used for all merges along the path</param>
    /// <returns>Sum of ratios of PDF * sampleCount</returns>
    protected float CameraPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs, Vector2 pixel,
                                          PdfRatio correlRatio, int numLightPaths, float bidirSelectDensity,
                                          float mergeProbability, float radius) {
        float sumReciprocals = 0.0f;
        float nextReciprocal = 1.0f;
        for (int i = lastCameraVertexIdx; i > 0; --i) {
            if (pdfs.PdfsCameraToLight[i] == 0)
                return 0; // the current technique cannot sample this path

            // Merging at this vertex
            float acceptProb = pdfs.PdfsLightToCamera[i] * MathF.PI * radius * radius;
            acceptProb *= correlRatio[i];
            sumReciprocals += nextReciprocal * numLightPaths * acceptProb * mergeProbability;

            nextReciprocal *= pdfs.PdfsLightToCamera[i] / pdfs.PdfsCameraToLight[i];

            // Connecting this vertex to the next one along the camera path
            sumReciprocals += nextReciprocal * bidirSelectDensity;
        }

        // Light tracer
        sumReciprocals += nextReciprocal * pdfs.PdfsLightToCamera[0] / pdfs.PdfsCameraToLight[0] * numLightPaths;

        // Merging directly visible (almost the same as the light tracer!)
        if (MergePrimary)
            sumReciprocals += nextReciprocal * numLightPaths * pdfs.PdfsLightToCamera[0]
                            * MathF.PI * radius * radius * mergeProbability;

        return sumReciprocals;
    }

    /// <summary>
    /// Computes the ratio of PDFs and sample counts used by the balance heuristic. Allows specifying the
    /// sample counts via parameters and ignores the actual settings of the integrator.
    /// </summary>
    /// <param name="lastCameraVertexIdx">Index of the last vertex along the camera subpath in the current technique</param>
    /// <param name="pdfs">Bidirectional path pdfs</param>
    /// <param name="pixel">The pixel this path contributes to</param>
    /// <param name="correlRatio">Comptues correction factor for correl-aware MIS</param>
    /// <param name="numLightPaths">Number of light subpaths</param>
    /// <param name="bidirSelectDensity">Effective density (pdf * num) of connection re-sampling</param>
    /// <param name="mergeProbability">Probablity that merging is used at each vertex</param>
    /// <param name="radius">PM radius used for all merges along the path</param>
    /// <returns>Sum of ratios of PDF * sampleCount</returns>
    protected float LightPathReciprocals(int lastCameraVertexIdx, BidirPathPdfs pdfs, Vector2 pixel,
                                         PdfRatio correlRatio, int numLightPaths, float bidirSelectDensity,
                                         float mergeProbability, float radius) {
        float sumReciprocals = 0.0f;
        float nextReciprocal = 1.0f;
        for (int i = lastCameraVertexIdx + 1; i < pdfs.NumPdfs; ++i) {
            if (pdfs.PdfsLightToCamera[i] == 0)
                return 0; // the current technique cannot sample this path

            if (i < pdfs.NumPdfs - 1 && (MergePrimary || i > 0)) { // no merging on the emitter itself
                // Account for merging at this vertex
                float acceptProb = pdfs.PdfsCameraToLight[i] * MathF.PI * radius * radius;
                acceptProb *= correlRatio[i];
                sumReciprocals += nextReciprocal * numLightPaths * acceptProb * mergeProbability;
            }

            nextReciprocal *= pdfs.PdfsCameraToLight[i] / pdfs.PdfsLightToCamera[i];

            // Account for connections from this vertex to its ancestor
            if (i < pdfs.NumPdfs - 2) // Connections to the emitter (next event) are treated separately
                sumReciprocals += nextReciprocal * bidirSelectDensity;
        }
        if (EnableHitting || NumShadowRays != 0) sumReciprocals += nextReciprocal; // Next event and hitting the emitter directly
        return sumReciprocals;
    }
}