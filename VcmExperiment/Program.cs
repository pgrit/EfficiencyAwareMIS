using EfficiencyAwareMIS.VcmExperiment;

// TODO move scenes into this repository
SceneRegistry.AddSource("../../GuidingExperiments/Scenes");

Benchmark benchmark = new(new GroundTruthExperiment(), new() {
    SceneRegistry.LoadScene("HotLivingMod", maxDepth: 10),
    SceneRegistry.LoadScene("VeachBidir", maxDepth: 5),
    SceneRegistry.LoadScene("TargetPractice", maxDepth: 5),
    SceneRegistry.LoadScene("ModernLivingRoom", maxDepth: 10),
    SceneRegistry.LoadScene("Pool", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("HomeOffice", maxDepth: 5, minDepth: 1),

    SceneRegistry.LoadScene("CountryKitchen", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("ReverseCornellBox", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("ModernHall", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("SpongeScene", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("LowPoly", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("LampCaustic", maxDepth: 10, minDepth: 1),
    SceneRegistry.LoadScene("LampCausticNoShade", maxDepth: 10, minDepth: 1),
    SceneRegistry.LoadScene("RoughGlasses", maxDepth: 10, minDepth: 1),
    SceneRegistry.LoadScene("RoughGlassesIndirect", maxDepth: 10, minDepth: 1),
    SceneRegistry.LoadScene("RoughNoGlasses", maxDepth: 10, minDepth: 1),

    SceneRegistry.LoadScene("Bathroom2", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("DiningRoom", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("GlassOfWater", maxDepth: 10, minDepth: 1),
    SceneRegistry.LoadScene("House", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("Lamp", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("LivingRoom2", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("Sponza", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("Staircase", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("VeachAjar", maxDepth: 5, minDepth: 1),

    SceneRegistry.LoadScene("BoxLargeLight", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("Footprint", maxDepth: 5, minDepth: 1),
    SceneRegistry.LoadScene("Garage", maxDepth: 5, minDepth: 1),
}, "Results", 640, 480, computeErrorMetrics: true);
benchmark.Run();