namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Renders images with different sample counts for the VCM integrator and computes their error adn render
/// times. Compares the results to the predictions of <see cref="AdaptiveVcm"/>
/// </summary>
class GroundTruthExperiment : Experiment {
    public override List<Method> MakeMethods() {
        List<Method> methods = new() {
            // new("OurDecisionCorrelAware", new AdaptiveVcm() {
            //     DisableCorrelAware = false,
            //     NumIterations = 16,
            //     MaxNumUpdates = 4,
            //     NumConnections = 1,
            //     EnableMerging = true,
            //     WriteMomentImages = true,
            // }),
            // new("VanillaVcm30s", new CorrelAwareVcm() {
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 30000,
            //     NumConnections = 1,
            // }),
            // new("Pt30s", new CorrelAwareVcm() {
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 30000,
            //     NumLightPaths = 0,
            //     NumConnections = 0,
            //     EnableMerging = false,
            // }),
            new("Our30s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                MaxNumUpdates = 1,
                NumConnections = 1,
            }),
            new("OurBdpt30s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                MaxNumUpdates = 1,
                NumConnections = 1,
                EnableMerging = false,
            }),
            new("OurGlobal30s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                MaxNumUpdates = 1,
                NumConnections = 1,
                UsePerPixelConnections = false,
            }),
            new("OurBdptGlobal30s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 30000,
                MaxNumUpdates = 1,
                NumConnections = 1,
                UsePerPixelConnections = false,
                EnableMerging = false,
            }),
        };

        return methods;
    }
}