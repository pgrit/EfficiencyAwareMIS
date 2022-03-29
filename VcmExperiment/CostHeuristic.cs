namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Evaluates our cost heuristic, based on global hyper-parameters and per-scene statistics
/// </summary>
public class CostHeuristic {
    /// <summary>
    /// Hyper-parameter: relative cost of continuing the light subpath with one more edge and performing
    /// next event (connection to the camera, aka light tracing)
    /// </summary>
    // public float CostLight = 1.0f;

    // /// <summary>
    // /// Hyper-parameter: relative cost of continuing the camera subpath with one more edge and performing next
    // /// event (connection to the light)
    // /// </summary>
    // public float CostCamera = 1.0f;

    // /// <summary>
    // /// Hyper-paramter: relative cost of a single bidirectional connection (one shadow ray, two BSDF eval)
    // /// </summary>
    // public float CostConnect = 0.3f;

    // /// <summary>
    // /// Cost of shading a single photon, multiple of the average number of photons found per query
    // /// </summary>
    // public float CostShade = 0.65f;

    // /// <summary>
    // /// Cost of querying the photon map as a multiple of the logarithm of the number of photons
    // /// </summary>
    // public float CostQuery = 1.04f;

    // /// <summary>
    // /// Cost of building the photon map as a multiple of the number of photons.
    // /// Roughly 40% of LT in our implementation.
    // /// </summary>
    // public float CostPhotonBuild = 0.4f;

    public float CostLight = 1.0f;
    public float CostCamera = 1.0f;
    public float CostConnect = 0.3f;
    public float CostShade = 0.25f;
    public float CostQuery = 0.045f;
    public float CostPhotonBuild = 0.175f;

    float numPixels;
    float avgCamLen;
    float avgLightLen;
    float avgPhotonsPerQueryPerLightPath;

    /// <summary>
    /// Updates the scene specific statistics
    /// </summary>
    /// <param name="numPixels">Number of pixels in the image = number of camera subpaths</param>
    /// <param name="numLightPaths">Current number of light subpaths in the pilot strategy</param>
    /// <param name="avgCamLen">Average camera subpath length</param>
    /// <param name="avgLightLen">Average light subpath length</param>
    /// <param name="avgPhotonsPerQuery">Average number of photons found by each query</param>
    public void UpdateStats(float numPixels, float numLightPaths, float avgCamLen, float avgLightLen,
                            float avgPhotonsPerQuery) {
        this.numPixels = numPixels;
        this.avgCamLen = avgCamLen == 0 ? 5 : avgCamLen;
        this.avgLightLen = numLightPaths == 0 ? this.avgCamLen : avgLightLen;
        if (numLightPaths == 0 || avgPhotonsPerQuery == 0)
            avgPhotonsPerQueryPerLightPath = 1e-7f; // Initial guess if there were no light paths or no merges
        else
            avgPhotonsPerQueryPerLightPath = avgPhotonsPerQuery / numLightPaths;
    }

    /// <summary>
    /// Evaluates our cost heuristic for a single pixel. The cost of global components (i.e., tracing the
    /// light subpaths) is ammortized, so we report the relative per-pixel value.
    /// </summary>
    /// <param name="numLightPaths">Number of light subpaths</param>
    /// <param name="numConnections">Number of connections</param>
    /// <param name="mergeProbability">Merge probability in this pixel</param>
    /// <param name="disableMerge">
    /// If merging is disabled globally, this should be set to true. If this is false, the PM build time
    /// will be included in the heuristic, ammortized over all pixels.
    /// </param>
    /// <returns></returns>
    public float EvaluatePerPixel(float numLightPaths, float numConnections, float mergeProbability,
                                  bool disableMerge = false) {
        // path tracing and light tracing incl. next event
        float ltTime = CostLight * avgLightLen * numLightPaths / numPixels;
        float ptTime = avgCamLen * CostCamera;
        float connectTime = avgCamLen * numConnections * CostConnect;

        // building the photon map, ammortized over all pixels.
        float buildTime = numLightPaths * avgLightLen > 1
            ? CostPhotonBuild * avgLightLen * numLightPaths * MathF.Log(avgLightLen * numLightPaths) / numPixels
            : 0.0f;

        // performing the merges
        float queryTime = numLightPaths * avgLightLen > 1
            ? CostQuery * avgCamLen * MathF.Log(numLightPaths * avgLightLen)
            : 0.0f;
        float mergeShadeTime = CostShade * avgPhotonsPerQueryPerLightPath * avgCamLen * numLightPaths;
        float mergeTime = (queryTime + mergeShadeTime) * mergeProbability;

        float result = ptTime + ltTime + connectTime;
        if (!disableMerge) result += buildTime + mergeTime; // PM is only built if merging is enabled anywhere
        Debug.Assert(float.IsFinite(result));
        return result;

        // light tracing incl. next event, ammortized over all pixels
        // float result = CostLight * avgLightLen * numLightPaths / numPixels;

        // // path tracing incl. next event
        // result += avgCamLen * CostCamera;

        // // connections
        // result += avgCamLen * numConnections * CostConnect;

        // // building the photon map, ammortized over all pixels
        // result += CostPhotonBuild * avgLightLen * numLightPaths / numPixels;

        // // performing the merges
        // result += CostShade * avgPhotonsPerQueryPerLightPath * avgCamLen * numLightPaths * mergeProbability;
        // if (numLightPaths * avgLightLen > 1) // prevent negative numbers and -Inf due to the log(n)
        //     result += CostQuery * avgCamLen * MathF.Log(numLightPaths * avgLightLen);

        // return result;
    }
}
