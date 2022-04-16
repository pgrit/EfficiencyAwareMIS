namespace EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Starts with a plain path tracer and evaluates a small set of VCM candidate strategies. If one is deemed
/// worthwhile, switches to it and expands the set of candidates for one more optimization.
/// </summary>
class OnDemandVcm : AdaptiveVcm {
    bool inPilotPhase = true;

    protected override void OnBeforeRender() {
        base.OnBeforeRender();

        // Adapt the candidates: only consider a small set that is likely to be better than path tracing
        NumLightPathCandidates = new[] { 0.5f, 1f };
        NumConnectionsCandidates = new[] { 0, 1 };
        UsePerPixelConnections = false;
        UsePerPixelMerges = false;

        // Start with pure path tracing
        NumLightPaths = 0;
        NumConnections = 0;
        UseMergesGlobally = false;

        inPilotPhase = true;
    }

    protected override void OnEndIteration(uint iteration) {
        base.OnEndIteration(iteration);

        if (NumLightPaths != 0 && inPilotPhase) { // A bidirectional technique was enabled
            NumLightPathCandidates = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 2.0f };
            NumConnectionsCandidates = new[] { 0, 1, 2, 4, 8 };
            UsePerPixelConnections = false;
            UsePerPixelMerges = true;
            inPilotPhase = false;

            Scene.FrameBuffer.Clear();

            InitCandidates();
        }
    }
}