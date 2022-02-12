namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Evaluates our cost heuristic, based on global hyper-parameters and per-scene statistics
/// </summary>
class CostHeuristic {
    /// <summary>
    /// Hyper-parameter: relative cost of continuing the light subpath with one more edge and performing
    /// next event (connection to the camera, aka light tracing)
    /// </summary>
    public float CostLight = 1.0f;

    /// <summary>
    /// Hyper-parameter: relative cost of continuing the camera subpath with one more edge and performing next
    /// event (connection to the light)
    /// </summary>
    public float CostCamera = 1.0f;

    /// <summary>
    /// Hyper-paramter: relative cost of a single bidirectional connection (one shadow ray, two BSDF eval)
    /// </summary>
    public float CostConnect = 0.3f;

    /// <summary>
    /// Hyper-parameter: relative cost of a single merge (one camera vertex, one light vertex)
    /// </summary>
    public float CostMerge = 2.5f;

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
    /// <returns></returns>
    public float EvaluatePerPixel(float numLightPaths, float numConnections, float mergeProbability) {
        // light tracing incl. next event, ammortized over all pixels
        float result = CostLight * avgLightLen * numLightPaths / numPixels;

        // techniques along the camera subpath
        result += avgCamLen * (
            CostCamera // unidir. path tracing including next event
            + CostConnect * numConnections
            + CostMerge * avgPhotonsPerQueryPerLightPath * numLightPaths * mergeProbability
        );

        return result;
    }
}
