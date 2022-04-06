namespace EfficiencyAwareMIS.VcmExperiment;

class EqualTimeExperiment : Experiment {
    AdaptiveVcm groundTruthMoment, groundTruthVariance;
    public override List<Method> MakeMethods() {
        // TODO:
        // - methods without correl-aware weights
        // - BDPT application with per-pixel and per-image counts
        // - lightweight path tracer pilot

        List<Method> methods = new() {
            // new("VanillaVcm60s", new CorrelAwareVcm() {
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 60000,
            //     NumConnections = 1,
            // }),
            // new("VanillaBdpt60s", new CorrelAwareVcm() {
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 60000,
            //     NumConnections = 1,
            //     EnableMerging = false,
            // }),
            // new("Pt60s", new CorrelAwareVcm() {
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 60000,
            //     NumLightPaths = 0,
            //     NumConnections = 0,
            //     EnableMerging = false,
            // }),
            // new("Our60s", new AdaptiveVcm() {
            //     DisableCorrelAware = false,
            //     NumIterations = int.MaxValue,
            //     MaximumRenderTimeMs = 60000,
            //     MaxNumUpdates = 1,
            //     NumConnections = 1,
            // }),
        };

        groundTruthMoment = new AdaptiveVcm() {
            DisableCorrelAware = false,
            NumIterations = int.MaxValue,
            MaximumRenderTimeMs = 60000,
            MaxNumUpdates = 0,
            NumConnections = 1,
            UsePerPixelConnections = false,
        };
        groundTruthVariance = new AdaptiveVcm() {
            DisableCorrelAware = false,
            NumIterations = int.MaxValue,
            MaximumRenderTimeMs = 60000,
            MaxNumUpdates = 0,
            NumConnections = 1,
            UsePerPixelConnections = false,
        };
        methods.Add(new("GroundTruthMoment60s", groundTruthMoment));
        methods.Add(new("GroundTruthVariance60s", groundTruthVariance));

        return methods;
    }

    public override void OnStartScene(Scene scene, string dir) {
        string sceneName = System.IO.Path.GetFileName(dir);
        var masks = Layers.LoadFromFile($"Results/GroundTruth/{sceneName}/Masks.exr");
        groundTruthMoment.MergeMask = masks["merge-moment"] as MonochromeImage;
        groundTruthVariance.MergeMask = masks["merge-var"] as MonochromeImage;

        var lines = System.IO.File.ReadAllLines($"Results/GroundTruth/{sceneName}" + $"/GlobalCounts.txt");
        groundTruthMoment.NumLightPaths = int.Parse(lines[11]);
        groundTruthMoment.NumConnections = int.Parse(lines[12]);
        groundTruthVariance.NumLightPaths = int.Parse(lines[8]);
        groundTruthVariance.NumConnections = int.Parse(lines[9]);
    }
}