namespace EfficiencyAwareMIS.VcmExperiment;

class EqualTimeExperiment : Experiment {
    public override List<Method> MakeMethods() {
        // TODO:
        // - methods without correl-aware weights
        // - BDPT application with per-pixel and per-image counts
        // - lightweight path tracer pilot

        List<Method> methods = new() {
            new("VanillaVcm30s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                NumConnections = 1,
            }),
            new("VanillaBdpt30s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                NumConnections = 1,
                EnableMerging = false,
            }),
            new("Pt30s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                NumLightPaths = 0,
                NumConnections = 0,
                EnableMerging = false,
            }),
            new("Our30s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                MaxNumUpdates = 1,
                NumConnections = 1,
            }),
        };

        return methods;
    }
}