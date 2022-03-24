using System.IO;
using System.Text.Json;
using EfficiencyAwareMIS.VcmExperiment;

/// <summary>
/// Measures the relative cost of different VCM technique components
/// </summary>
class CostMeasurements : Experiment {
    class MergeHack : PathLengthEstimatingVcm {
        protected override RgbColor Merge((CameraPath path, float cameraJacobian) userData, SurfacePoint hit,
                                          Vector3 outDir, int pathIdx, int vertexIdx, float distSqr, float radiusSquared) {
            OnMergeSample(RgbColor.Black, 0, 0, userData.path, LightPaths.PathCache[pathIdx, vertexIdx], 0, 0, 0);
            return RgbColor.Black;
        }
    }

    public override List<Method> MakeMethods() => new() {
        // new("PTLT", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 0,
        //     NumLightPaths = null,
        //     EnableMerging = false
        // }),
        // new("PTLT2", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 0,
        //     NumLightPaths = 640 * 480 * 2,
        //     EnableMerging = false
        // }),
        // new("PTLTMerge", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 0,
        //     NumLightPaths = null,
        //     EnableMerging = true
        // }),
        // new("PTLTMergeQueryOnly", new MergeHack() {
        //     NumIterations = 16,
        //     NumConnections = 0,
        //     NumLightPaths = null,
        //     EnableMerging = true
        // }),
        // new("PTLT2Merge", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 0,
        //     NumLightPaths = 640 * 480 * 2,
        //     EnableMerging = true
        // }),
        // new("PTLTConnect", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 1,
        //     NumLightPaths = null,
        //     EnableMerging = false
        // }),
        // new("PTLTConnect16", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 16,
        //     NumLightPaths = null,
        //     EnableMerging = false
        // }),
        // new("PTLT2Connect", new PathLengthEstimatingVcm() {
        //     NumIterations = 16,
        //     NumConnections = 1,
        //     NumLightPaths = 640 * 480 * 2,
        //     EnableMerging = false
        // }),
    };

    class Stats {
        public float PtTime, LtTime, AvgCamLen, AvgLightLen, AvgPhotonsPerQuery, PmBuildTime, LtShadowTime;
        public Stats(string sceneDir, string methodName) {
            string json = File.ReadAllText(Path.Join(sceneDir, methodName, "Render.json"));
            var root = JsonDocument.Parse(json).RootElement;

            PtTime = (float) root.GetProperty("PathTracerTime").GetInt64();
            LtTime = (float) root.GetProperty("LightTracerTime").GetInt64();
            AvgCamLen = root.GetProperty("AverageCameraPathLength").GetSingle();
            AvgLightLen = root.GetProperty("AverageLightPathLength").GetSingle();
            AvgPhotonsPerQuery = root.GetProperty("AveragePhotonsPerQuery").GetSingle();
            PmBuildTime = (float) root.GetProperty("PhotonBuildTime").GetInt64();
            LtShadowTime = (float) root.GetProperty("LightTracerShadowTime").GetInt64();
        }
    }

    public override void OnDoneScene(Scene scene, string dir) {
        var ptlt = new Stats(dir, "PTLT");
        var ptlt2 = new Stats(dir, "PTLT2");
        var ptltmerge = new Stats(dir, "PTLTMerge");
        var ptlt2merge = new Stats(dir, "PTLT2Merge");
        var ptltcon = new Stats(dir, "PTLTConnect");
        var ptltcon16 = new Stats(dir, "PTLTConnect16");
        var ptlt2con = new Stats(dir, "PTLT2Connect");

        // LT shadow time relative to LT trace time
        float ltTraceTime = ptlt.LtTime - ptlt.LtShadowTime;
        float ltRatio = ltTraceTime / ptlt.LtShadowTime;
        float ltShadowRatio = 1.0f / (1.0f + ltRatio);
        Console.WriteLine($"LT weights: trace {1 - ltShadowRatio}, shadow {ltShadowRatio}");
        float lt2TraceTime = ptlt2.LtTime - ptlt2.LtShadowTime;
        float lt2Ratio = lt2TraceTime / ptlt2.LtShadowTime;
        float lt2ShadowRatio = 1.0f / (1.0f + lt2Ratio);
        Console.WriteLine($"LT2 weights: trace {1 - lt2ShadowRatio}, shadow {lt2ShadowRatio}");

        // PT trace + shadow relative to LT trace + shadow
        float ptPerVertex = ptlt.PtTime / ptlt.AvgCamLen;
        float ltPerVertex = ptlt.LtTime / ptlt.AvgLightLen;
        Console.WriteLine($"PT compared to LT: {ptPerVertex / ltPerVertex}");
        float pt2PerVertex = ptlt2.PtTime / ptlt2.AvgCamLen;
        float lt2PerVertex = ptlt2.LtTime / ptlt2.AvgLightLen;
        Console.WriteLine($"PT compared to LT2: {pt2PerVertex / lt2PerVertex}");

        // Build time (approximately linear complexity)
        Console.WriteLine($"PM build ratio to LT: {ptltmerge.PmBuildTime / ptltmerge.LtTime}");

        // For querying,
        float mergeTime = ptltmerge.PtTime - ptlt.PtTime;
        float mergeTime2 = ptlt2merge.PtTime - ptlt.PtTime;
        Console.WriteLine($"Query time scaling: {mergeTime2 / mergeTime}");

        float query = (2 * mergeTime - mergeTime2) / 0.95f;
        float shade = mergeTime - query;
        Console.WriteLine($"Raw query time: {query}");
        Console.WriteLine($"Raw shade time: {shade}");

        // Ratio between shading cost per photon and path tracing cost is between 0.5 and 0.9, 0.65 on average
        Console.WriteLine($"Shade ratio to PT: {shade / ptltmerge.AvgPhotonsPerQuery / ptlt.PtTime}");

        // Ratio of query time (corrected by log of photon count) to path tracing time is around
        // 0.3 in most scenes. Notable outlier: 0.5 in VeachBidir (very focused illumination!)
        Console.WriteLine($"Query ratio to PT: {query / MathF.Log(ptltmerge.AvgLightLen*640*480) / (ptlt.PtTime)}");

        // Connect time relative to PT trace + shadow
        float connect1Time = ptltcon.PtTime - ptlt.PtTime;
        float connect16Time = ptltcon16.PtTime - ptlt.PtTime;
        float connectTime2 = ptlt2con.PtTime - ptlt.PtTime;
        Console.WriteLine($"16 connections vs 1: {connect16Time / connect1Time}");
        Console.WriteLine($"1 connection vs PT: {connect1Time / ptlt.PtTime}");
        Console.WriteLine($"With changing number of light paths: {connect1Time / connectTime2}");

        Console.WriteLine();
    }
}
