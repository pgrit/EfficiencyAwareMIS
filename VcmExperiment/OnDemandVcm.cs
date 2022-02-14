namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Starts with a plain path tracer and evaluates a small set of VCM candidate strategies. If one is deemed
/// worthwhile, switches to it and expands the set of candidates for one more optimization.
/// </summary>
class OnDemandVcm : AdaptiveVcm {
    protected override void OnBeforeRender() {
        base.OnBeforeRender();

        // Adapt the candidates

        // TODO cache current config, replace temporarily with: PT, vanilla BDPT (with lower num light paths and higher num connect), vanilla VCM

        // TODO switch temporarily to global optimization

        // TODO force all bidir off

        // TODO set MaxNumUpdates temporarily to unbounded / rather high (pretty cheap in the PT pilot)
    }

    protected override void OnEndIteration(uint iteration) {
        base.OnEndIteration(iteration);

        // TODO check if something bidir was turned on. IF yes: restore old candidates and per-pixel settings, max num updates
    }
}