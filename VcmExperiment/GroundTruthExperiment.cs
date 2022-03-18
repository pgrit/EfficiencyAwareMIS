namespace EfficiencyAwareMIS.VcmExperiment;

class GroundTruthExperiment : Experiment {
    public int NumIterations = 64;

    int width, height;

    public override List<Method> MakeMethods() {
        List<Method> methods = new() { };

        // Generate equivalent methods for each candidate
        float[] numLightPathCandidates = new[] { /*0.25f,*/ 0.5f, /*0.75f,*/ 1.0f, 2.0f };
        int[] numConnectionsCandidates = new[] { 0, /*1, 2,*/ 4, 8/*, 16*/ };
        foreach (float lightRatio in numLightPathCandidates) {
            int numLightPaths = (int)(lightRatio * width * height);
            foreach (int numConnect in numConnectionsCandidates) {
                foreach (bool merge in new[] { true, false }) {
                    methods.Add(new($"n{numLightPaths}-c{numConnect:00}-m{(merge?1:0)}", new CorrelAwareVcm() {
                        NumIterations = NumIterations,
                        NumConnections = numConnect,
                        NumLightPaths = numLightPaths,
                        EnableMerging = merge
                    }));
                }
            }
        }

        // Path tracer is a special case
        methods.Add(new($"n{0}-c{0:00}-m0", new CorrelAwareVcm() {
            NumIterations = NumIterations,
            NumConnections = 0,
            NumLightPaths = 0,
            EnableMerging = true
        }));

        return methods;
    }

    public override void OnStartScene(Scene scene, string dir) {
        width = scene.FrameBuffer.Width;
        height = scene.FrameBuffer.Height;
    }

    public override void OnDoneScene(Scene scene, string dir) {
        // Run optimizer on the moment / variance estimates

        // Apply filtering and run the optimizer
        // (tests the adverse effect of filtering in a setting with little / no noise)

        // Compare cost heuristic values to actual render times (plot)

        // Ablation study: optimization with different values for the cost heuristic hyperparameters
    }

    public override void OnDone(string workingDirectory) {
        // Generate overview figure ...
    }
}