namespace EfficiencyAwareMIS.VcmExperiment;

class FilterExperiment : Experiment {
    public int NumIterations = 1;

    public override List<Method> MakeMethods() {
        List<Method> methods = new() { };

        // Render moment estimates with a pilot method and no filtering

        return methods;
    }

    public override void OnDoneScene(Scene scene, string dir) {
        // Read unfiltered moments from the file

        // Apply filtering with different parameters and run the optimizer
        // (tests the adverse effect of filtering in a setting with little / no noise)
    }

    public override void OnDone(string workingDirectory) {
        // Generate overview figure ...
    }
}