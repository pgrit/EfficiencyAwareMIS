namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Starts with a plain path tracer and evaluates a small set of VCM candidate strategies. If one is deemed
/// worthwhile, switches to it and expands the set of candidates for one more optimization.
/// </summary>
class OnDemandVcm : AdaptiveVcm {
    protected override void OnBeforeRender() {
        base.OnBeforeRender();

        // Adapt the candidates

    }
}