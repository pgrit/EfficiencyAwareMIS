using EfficiencyAwareMIS.VcmExperiment;

// TODO move scenes into this repository
SceneRegistry.AddSource("../../GuidingExperiments/Scenes");

Benchmark benchmark = new(new GroundTruthExperiment(), new() {
    SceneRegistry.LoadScene("HotLivingMod", maxDepth: 10)
}, "Results", 640, 480, computeErrorMetrics: true);
benchmark.Run();