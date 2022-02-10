namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Renders images with different sample counts for the VCM integrator and computes their error adn render
/// times. Compares the results to the predictions of <see cref="AdaptiveVcm"/>
/// </summary>
class GroundTruthExperiment : Experiment {
    public override List<Method> MakeMethods() {
        List<Method> methods = new() {
            // new("VanillaVcm", new CorrelAwareVcm() {
            //     NumIterations = 2
            // }),
            new("OurDecision", new AdaptiveVcm() {
                // NumLightPaths = 0,
                // NumConnections = 0,
                DisableCorrelAware = false,
                // EnableMerging = false,
                NumIterations = 2,
            }),
            new("OurDecisionPT", new AdaptiveVcm() {
                NumLightPaths = 0,
                NumConnections = 0,
                DisableCorrelAware = false,
                EnableMerging = false,
                NumIterations = 2,
            })
        };

        return methods;
    }
}