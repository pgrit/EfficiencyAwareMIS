namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Computes the average path lengths and average photon counts of a VCM integrator. The counts are reset
/// after each iteration.
/// </summary>
public class PathLengthEstimatingVcm : MergeMaskVcm {
    long TotalCameraPathLength = 0;
    long TotalMergeOperations = 0;
    long TotalMergePhotons = 0;

    /// <summary>
    /// Average number of edges along the camera subpaths
    /// </summary>
    protected float AverageCameraPathLength = 0;

    /// <summary>
    /// Average number of edges along the light subpaths
    /// </summary>
    protected float AverageLightPathLength = 0;

    /// <summary>
    /// Average number of photons found by each merging operation. Depends on the average light path length
    /// and the total number of light subpaths.
    /// </summary>
    protected float AveragePhotonsPerQuery = 0;

    protected override void OnAfterRender() {
        base.OnAfterRender();
        Scene.FrameBuffer.MetaData["AverageCameraPathLength"] = AverageCameraPathLength;
        Scene.FrameBuffer.MetaData["AverageLightPathLength"] = AverageLightPathLength;
        Scene.FrameBuffer.MetaData["AveragePhotonsPerQuery"] = AveragePhotonsPerQuery;
    }

    protected override void PreIteration(uint iteration) {
        base.PreIteration(iteration);

        if (iteration > 0) {
            float numPixels = Scene.FrameBuffer.Width * Scene.FrameBuffer.Height;
            AverageCameraPathLength = TotalCameraPathLength / numPixels;
            AveragePhotonsPerQuery = TotalMergePhotons / (float)TotalMergeOperations;
            AverageLightPathLength = ComputeAverageLightPathLength();
        }

        // Reset statistics
        TotalCameraPathLength = 0;
        TotalMergeOperations = 0;
        TotalMergePhotons = 0;
    }

    protected override void OnCameraPathTerminate(CameraPath path)
    => Interlocked.Add(ref TotalCameraPathLength, path.Vertices.Count);

    protected override void OnCombinedMergeSample(Ray ray, SurfacePoint hit, RNG rng, CameraPath path,
                                                  float cameraJacobian, RgbColor estimate)
    => Interlocked.Increment(ref TotalMergeOperations);

    protected override void OnMergeSample(RgbColor weight, float kernelWeight, float misWeight,
                                          CameraPath cameraPath, PathVertex lightVertex, float pdfCameraReverse,
                                          float pdfLightReverse, float pdfNextEvent) {
        base.OnMergeSample(weight, kernelWeight, misWeight, cameraPath, lightVertex, pdfCameraReverse,
            pdfLightReverse, pdfNextEvent);

        Interlocked.Increment(ref TotalMergePhotons);
    }

    private float ComputeAverageLightPathLength() {
        float average = 0;
        if (LightPaths == null) return 0;
        for (int i = 0; i < LightPaths.NumPaths; ++i) {
            int length = LightPaths.PathCache.Length(i);
            average = (length + i * average) / (i + 1);
        }
        return average;
    }
}