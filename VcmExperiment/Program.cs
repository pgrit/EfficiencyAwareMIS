using EfficiencyAwareMIS.VcmExperiment;

SceneRegistry.AddSource("../Scenes");

List<(string, int)> scenes = new() {
    ("HotLivingMod", 10),
    ("VeachBidir", 5),
    ("ModernLivingRoom", 10),
    ("Pool", 5),
    ("RoughGlassesIndirect", 10),
    ("CountryKitchen", 5),

    // Other scenes (not in this repository)
    // ("LampCaustic", 10),
    // ("LampCausticNoShade", 10),
    // ("TargetPractice", 5),
    // ("RoughGlasses", 10),
    // ("HomeOffice", 5),
    // ("House", 5),
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

// new Benchmark(
//     new GroundTruthExperiment(),
//     sceneConfigs,
//     "Results/GroundTruth",
//     640, 480
// ).Run();

// new Benchmark(
//     new FilterExperiment(),
//     sceneConfigs,
//     "Results/Filtering",
//     640, 480
// ).Run();

new Benchmark(
    new EqualTimeExperiment(),
    sceneConfigs,
    "Results/EqualTime",
    640, 480
).Run();