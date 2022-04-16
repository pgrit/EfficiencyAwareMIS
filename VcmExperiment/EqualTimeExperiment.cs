namespace EfficiencyAwareMIS.VcmExperiment;

class EqualTimeExperiment : Experiment {
    List<AdaptiveVcm> groundTruthMoment = null, groundTruthVariance = null;
    string[] suffixes = new[] { "", "CostMerge0.1", "CostMerge0.5", "CostMerge2", "CostMerge10" };

    public override List<Method> MakeMethods() {
        List<Method> methods = new() {
            new("VanillaVcm60s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 60000,
                NumConnections = 1,
            }),
            new("VanillaBdpt60s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 60000,
                NumConnections = 1,
                EnableMerging = false,
            }),
            new("Pt60s", new CorrelAwareVcm() {
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 60000,
                NumLightPaths = 0,
                NumConnections = 0,
                EnableMerging = false,
            }),
            new("OurVcm60s", new AdaptiveVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 60000,
                MaxNumUpdates = 4,
                NumConnections = 1,
                UsePerPixelConnections = false
            }),
            new("OurVcmStartPT60s", new OnDemandVcm() {
                DisableCorrelAware = false,
                NumIterations = int.MaxValue,
                MaximumRenderTimeMs = 60000,
                MaxNumUpdates = 4,
            }),
        };

        // The following only works if the GroundTruthExperiment has been run. Equal-time rendering
        // with pre-computed variance and moments.
        // groundTruthMoment = new();
        // groundTruthVariance = new();
        // foreach (var suffix in suffixes) {
        //     groundTruthMoment.Add(new AdaptiveVcm() {
        //         DisableCorrelAware = false,
        //         NumIterations = int.MaxValue,
        //         MaximumRenderTimeMs = 60000,
        //         MaxNumUpdates = 0,
        //         NumConnections = 1,
        //         UsePerPixelConnections = false,
        //     });
        //     groundTruthVariance.Add(new AdaptiveVcm() {
        //         DisableCorrelAware = false,
        //         NumIterations = int.MaxValue,
        //         MaximumRenderTimeMs = 60000,
        //         MaxNumUpdates = 0,
        //         NumConnections = 1,
        //         UsePerPixelConnections = false,
        //     });
        //     methods.Add(new($"GroundTruthMoment60s{suffix}", groundTruthMoment[^1]));
        //     methods.Add(new($"GroundTruthVariance60s{suffix}", groundTruthVariance[^1]));
        // }

        return methods;
    }

    public override void OnStartScene(Scene scene, string dir) {
        if (groundTruthMoment == null) return;

        string sceneName = System.IO.Path.GetFileName(dir);
        int i = 0;
        foreach (var suffix in suffixes) {
            var masks = Layers.LoadFromFile($"Results/GroundTruth/{sceneName}/Masks{suffix}.exr");
            groundTruthMoment[i].MergeMask = masks["merge-moment"] as MonochromeImage;
            groundTruthVariance[i].MergeMask = masks["merge-var"] as MonochromeImage;

            var lines = System.IO.File.ReadAllLines($"Results/GroundTruth/{sceneName}" + $"/GlobalCounts{suffix}.txt");
            groundTruthMoment[i].NumLightPaths = int.Parse(lines[16]);
            groundTruthMoment[i].NumConnections = int.Parse(lines[17]);
            groundTruthVariance[i].NumLightPaths = int.Parse(lines[14]);
            groundTruthVariance[i].NumConnections = int.Parse(lines[15]);

            i++;
        }
    }
}