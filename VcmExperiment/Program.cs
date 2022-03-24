using EfficiencyAwareMIS.VcmExperiment;

// TODO move scenes into this repository
SceneRegistry.AddSource("../../GuidingExperiments/Scenes");

List<(string, int)> scenes = new() {
    // Most interesting
    ("HotLivingMod", 10),
    ("VeachBidir", 5),
    ("TargetPractice", 5),
    ("ModernLivingRoom", 10),
    ("Pool", 5),
    ("House", 5),
    ("LampCaustic", 10),
    ("RoughGlasses", 10),
    ("RoughGlassesIndirect", 10),
    ("CountryKitchen", 5),
    ("LampCausticNoShade", 10),
    ("HomeOffice", 5),

    // Other scenes
    // ("LowPoly", 5),
    // ("Bedroom", 5),
    // ("GlassOfWater", 10),
    // ("Lamp", 5),
    // ("ModernHall", 5),
    // ("ReverseCornellBox", 5),
    // ("SpongeScene", 5),
    // ("RoughNoGlasses", 10),
    // ("Bathroom2", 5),
    // ("DiningRoom", 5),
    // ("LivingRoom2", 5),
    // ("Sponza", 5),
    // ("Staircase", 5),
    // ("VeachAjar", 5),
    // ("BoxLargeLight", 5),
    // ("Footprint", 5),
    // ("Garage", 5),
};

List<SceneConfig> sceneConfigs = new();
foreach(var (name, maxDepth) in scenes)
    sceneConfigs.Add(SceneRegistry.LoadScene(name, maxDepth: maxDepth));

// new Benchmark(
//     new CostMeasurements(),
//     sceneConfigs,
//     "Results/CostMeasurements",
//     640, 480
// ).Run(skipReference: true);

new Benchmark(
    new GroundTruthExperiment(),
    sceneConfigs,
    "Results/GroundTruth",
    640,
    480,
    computeErrorMetrics: true
).Run();
