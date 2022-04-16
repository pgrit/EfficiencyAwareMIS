namespace EfficiencyAwareMIS.VcmExperiment;

class FilterExperiment : Experiment {
    public int NumIterations = 1;

    float[] numLightPathCandidates = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 2.0f };
    int[] numConnectionsCandidates = new[] { 0, 1, 2, 4, 8, 16 };

    public override List<Method> MakeMethods() {
        List<Method> methods = new() { };

        // Render moment estimates with a pilot method and no filtering
        methods.Add(new("MomentEstimator", new AdaptiveVcm() {
            NumIterations = NumIterations,
            MaxNumUpdates = int.MaxValue,
            NumConnections = 4,
            NumLightPaths = null,
            EnableMerging = true,
            WriteDebugInfo = true,
            OnlyAccumulate = true,
            NumConnectionsCandidates = numConnectionsCandidates,
            NumLightPathCandidates = numLightPathCandidates
        }));

        return methods;
    }
}