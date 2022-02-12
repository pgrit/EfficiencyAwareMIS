namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Renders images with different sample counts for the VCM integrator and computes their error adn render
/// times. Compares the results to the predictions of <see cref="AdaptiveVcm"/>
/// </summary>
class GroundTruthExperiment : Experiment {
    public override List<Method> MakeMethods() {
        List<Method> methods = new() {
            // new("VanillaVcm", new SampleMaskVcm() {
            //     NumIterations = 4,
            //     RenderTechniquePyramid = true,
            // }),
            // new("VanillaBdpt", new CorrelAwareVcm() {
            //     NumIterations = 2,
            //     EnableMerging = false,
            // }),
            // new("OurBdptDecision", new AdaptiveVcm() {
            //     EnableMerging = false,
            //     NumIterations = 2,
            // }),
            // new("OurDecision", new AdaptiveVcm() {
            //     // NumLightPaths = 0,
            //     // NumConnections = 0,
            //     DisableCorrelAware = true,//false,
            //     // EnableMerging = false,
            //     NumIterations = 2,
            // }),
            new("OurDecisionCorrelAware", new AdaptiveVcm() {
                DisableCorrelAware = true,//false,
                NumIterations = 4,
                WriteMomentImages = true,
                // NumLightPaths = 0,
                // NumConnections = 0,
                // EnableMerging = false,
            })
        };

        return methods;
    }
}