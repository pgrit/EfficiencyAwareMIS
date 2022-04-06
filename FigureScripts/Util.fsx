// Image viewer
#r "../../imageLab/ImageLab.Flip/bin/Release/net6.0/ImageLab.Flip.dll"
#r "../../imageLab/ImageLab.Flip/bin/Release/net6.0/ImageLab.GUI.dll"
#r "nuget: SimpleImageIO"

// Plotting stuff
#r "nuget: Plotly.NET, 2.0.0-preview.18"
#r "nuget: Plotly.NET.Interactive, 2.0.0-preview.18"

#r "../VcmExperiment/bin/Release/net6.0/VcmExperiment.dll"

open EfficiencyAwareMIS.VcmExperiment
open Plotly.NET
open System.IO
open SimpleImageIO
open ImageLab.Flip
open ImageLab.GUI.Util

HTML(Flip.MakeHeader()).Display() |> ignore

let CompareMomentVarRatio scene firstCandidate secondCandidate exposure =
    let dirname = "../VcmExperiment/Results/GroundTruth/" + scene

    let moments = Layers.LoadFromFile(dirname + $"/Moments.exr")
    let variances = Layers.LoadFromFile(dirname + $"/Variances.exr")
    let reference = new RgbImage(dirname + "/Reference.exr")
    let ptMoment = moments[firstCandidate]
    let bdptMoment = moments[secondCandidate]
    let ptVariance = variances[firstCandidate]
    let bdptVariance = variances[secondCandidate]

    let momentRatio = new MonochromeImage(640, 480)
    let varianceRatio = new MonochromeImage(640, 480)
    let mutable avgMomentRatio = 0.0f
    let mutable avgVarRatio = 0.0f
    for row in 0..momentRatio.Height - 1 do
        for col in 0..momentRatio.Width - 1 do
            let mutable ratio = ptMoment.GetPixelChannel(col, row, 0) / bdptMoment.GetPixelChannel(col, row, 0)
            let mutable ratioVar = ptVariance.GetPixelChannel(col, row, 0) / bdptVariance.GetPixelChannel(col, row, 0)

            if not(Single.IsFinite(ratio)) || ratio <= 0f || reference.GetPixel(col, row) = RgbColor.Black then
                ratio <- 1f
            if not(Single.IsFinite(ratioVar)) || ratioVar <= 0f || reference.GetPixel(col, row) = RgbColor.Black then
                ratioVar <- 1f

            momentRatio.SetPixel(col, row, MathF.Log(ratio))
            varianceRatio.SetPixel(col, row, MathF.Log(ratioVar))

            avgMomentRatio <- avgMomentRatio + MathF.Log(ratio) / (640f * 480f)
            avgVarRatio <- avgVarRatio + MathF.Log(ratioVar) / (640f * 480f)
    avgMomentRatio <- MathF.Exp(avgMomentRatio)
    avgVarRatio <- MathF.Exp(avgVarRatio)

    let ratioColor = new FalseColorMap()
    let momentRatioColor = ratioColor.Apply(momentRatio)
    let varianceRatioColor = ratioColor.Apply(varianceRatio)
    let mapExposure = new ToneMapExposure()
    mapExposure.Exposure <- exposure

    let _ = HTML("<p>Geometric mean of per-pixel ratios:<p>").Display()
    let _ = (Map [ ("Moment", avgMomentRatio); ("Variance", avgVarRatio) ]).Display()

    HTML(Flip.Make [|
        struct ("Moment ratio", momentRatioColor :> ImageBase)
        struct ("Variance ratio", varianceRatioColor :> ImageBase)
        struct ("Reference", mapExposure.Apply(reference) :> ImageBase)
    |]).Display()

let GetErrors scene candidate =
    let dirname = "../VcmExperiment/Results/GroundTruth/" + scene

    let moments = Layers.LoadFromFile(dirname + $"/Moments.exr")
    let variances = Layers.LoadFromFile(dirname + $"/Variances.exr")
    let reference = new RgbImage(dirname + "/Reference.exr")
    let mutable avgMomentRatio = 0.0f
    let mutable avgVarRatio = 0.0f
    for row in 0..reference.Height - 1 do
        for col in 0..reference.Width - 1 do
            let m = moments[candidate].GetPixelChannel(col, row, 0)
            let v = variances[candidate].GetPixelChannel(col, row, 0)
            let r = reference.GetPixel(col, row).Average
            if r <> 0f then
                avgMomentRatio <- avgMomentRatio + m / r / r
                avgVarRatio <- avgVarRatio + v / r / r
    let numPixels = float32(reference.Height * reference.Width)
    avgMomentRatio <- avgMomentRatio / numPixels
    avgVarRatio <- avgVarRatio / numPixels
    avgMomentRatio, avgVarRatio

let connectColor = new FalseColor(new LinearColormap(0f, 16f))
let mergeColor = new FalseColor(new LinearColormap(0f, 1f))

let ShowMasks scene exposure suffix prefix =
    let dirname = "../VcmExperiment/Results/GroundTruth/" + scene
    let mapExposure = new ToneMapExposure()
    mapExposure.Exposure <- exposure

    let masks = Layers.LoadFromFile(dirname + $"/{prefix}Masks{suffix}.exr")
    let connectVar = connectColor.Apply(masks["connect-var"]) :> ImageBase
    let mergeVar = mergeColor.Apply(masks["merge-var"]) :> ImageBase
    let connectMoment = connectColor.Apply(masks["connect-moment"]) :> ImageBase
    let mergeMoment = mergeColor.Apply(masks["merge-moment"]) :> ImageBase
    let reference = new RgbImage(dirname + "/Reference.exr") :> ImageBase
    let lines = System.IO.File.ReadAllLines(dirname + $"/{prefix}GlobalCounts{suffix}.txt")

    HTML(Flip.Make[|
        struct ("ConnectVar", connectVar)
        struct ("MergeVar", mergeVar)
        struct ("ConnectMoment", connectMoment)
        struct ("MergeMoment", mergeMoment)
        struct ("Reference", mapExposure.Apply(reference))
    |]).Display() |> ignore

    // Read the global decisions and display them
    HTML("<p>Number of light paths:</p>").Display() |> ignore
    (Map [
        ( "Variance", (Int32.Parse(lines[0]), Int32.Parse(lines[1]) <> 0) )
        ( "Moment", (Int32.Parse(lines[2]), Int32.Parse(lines[3]) <> 0) )
        ( "Variance (abs.)", (Int32.Parse(lines[4]), Int32.Parse(lines[5]) <> 0) )
        ( "Moment (abs.)", (Int32.Parse(lines[6]), Int32.Parse(lines[7]) <> 0) )
    ]).Display() |> ignore

    HTML("<p>Global optimization:</p>").Display() |> ignore
    (Map [
        ( "Variance", (Int32.Parse(lines[8]), Int32.Parse(lines[9]), bool.Parse(lines[10])) )
        ( "Moment", (Int32.Parse(lines[11]), Int32.Parse(lines[12]), bool.Parse(lines[13])) )
    ]).Display() |> ignore

let ShowRenders scene exposure =
    let dirname = "../VcmExperiment/Results/GroundTruth/" + scene
    let mapExposure = new ToneMapExposure()
    mapExposure.Exposure <- exposure
    let reference = new RgbImage(dirname + "/Reference.exr")
    let pathTracer = new RgbImage(dirname + "/n=000000,c=00,m=0/Render.exr")
    let vcm = new RgbImage(dirname + "/n=307200,c=04,m=1/Render.exr")
    let bdpt = new RgbImage(dirname + "/n=307200,c=04,m=0/Render.exr")
    let lt = new RgbImage(dirname + "/n=307200,c=00,m=0/Render.exr")
    HTML(Flip.Make[|
        struct ("Reference", mapExposure.Apply(reference) :> ImageBase)
        struct ("PT", mapExposure.Apply(pathTracer) :> ImageBase)
        struct ("PT+LT", mapExposure.Apply(lt) :> ImageBase)
        struct ("BDPT", mapExposure.Apply(bdpt) :> ImageBase)
        struct ("VCM", mapExposure.Apply(vcm) :> ImageBase)
    |]).Display() |> ignore

let numLightPathCandidates = [ 0.25f; 0.5f; 0.75f; 1.0f; 2.0f ]
let numConnectionsCandidates = [ 0; 1; 2; 4; 8; 16 ]
let width, height = 640f, 480f

let mutable candidates = [
    for lightRatio in numLightPathCandidates do
        let numLightPaths = int(lightRatio * width * height)
        for numConnect in numConnectionsCandidates do
            for merge in [ true; false ] ->
                new Candidate(numLightPaths, numConnect, merge)
]
candidates <- new Candidate(0, 0, false) :: candidates // Path tracer

let Optimize costHeuristic moments filtered reference =
    let merge, connect = VcmOptimizer.OptimizePerPixel(filtered, costHeuristic, true, true)
    let pixelIntensities = new MonochromeImage(reference, MonochromeImage.RgbConvertMode.Average)
    let globalDecision = VcmOptimizer.OptimizePerImage(moments, pixelIntensities, numLightPathCandidates,
        numConnectionsCandidates, costHeuristic, new VcmOptimizer.CountFn(fun c -> fun r -> merge.GetPixel(c, r)),
        new VcmOptimizer.CountFn(fun c -> fun r -> connect.GetPixel(c, r)), true, true)

    merge, connect, globalDecision

let OptimizeWithFilter scene exposure filterMoments filterMergeMask filterConnectMask =
    let dir = "../VcmExperiment/Results/Filtering/" + scene
    let reference = new RgbImage(Path.Join(dir, "Reference.exr"))
    let render = new RgbImage(Path.Join(dir, "MomentEstimator", "Render.exr"))

    // Initialize the cost heuristic statistics
    let path = Path.Join(dir, "MomentEstimator", "Render.json")
    let json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path))
    let avgCamLen = json.RootElement.GetProperty("AverageCameraPathLength").GetSingle()
    let avgLightLen = json.RootElement.GetProperty("AverageLightPathLength").GetSingle()
    let avgPhoton = json.RootElement.GetProperty("AveragePhotonsPerQuery").GetSingle()
    let costHeuristic = new CostHeuristic()
    costHeuristic.UpdateStats(width * height, width * height, avgCamLen, avgLightLen, avgPhoton)

    // Gather moment estimates
    let momentLayers = Layers.LoadFromFile(Path.Join(dir, "MomentEstimator", "RenderMoments.exr"))
    let momentImages = new System.Collections.Generic.Dictionary<Candidate, MonochromeImage>()
    for c in candidates do
        momentImages[c] <- downcast momentLayers[c.ToString()]

    let filtered = new System.Collections.Generic.Dictionary<Candidate, MonochromeImage>()
    for m in momentImages do
        filtered.Add(m.Key, filterMoments(m.Value))

    // Compute and filter merge and connect masks
    let (merge, connect, globalDecision) = Optimize costHeuristic momentImages filtered reference

    (filterMergeMask(merge), filterConnectMask(connect), globalDecision)

let TestFilter scene exposure filterMoments filterMergeMask filterConnectMask =
    let dir = "../VcmExperiment/Results/Filtering/" + scene
    let reference = new RgbImage(Path.Join(dir, "Reference.exr"))
    let render = new RgbImage(Path.Join(dir, "MomentEstimator", "Render.exr"))

    let merge, connect, globalDecision = OptimizeWithFilter scene exposure filterMoments filterMergeMask filterConnectMask

    let mapExposure = new ToneMapExposure()
    mapExposure.Exposure <- exposure
    let connectColor = new FalseColor(new LinearColormap(0f, 16f))
    let mergeColor = new FalseColor(new LinearColormap(0f, 1f))

    let groundTruthDir = "../VcmExperiment/Results/GroundTruth/" + scene
    let masks = Layers.LoadFromFile(groundTruthDir + $"/Masks.exr")
    let lines = System.IO.File.ReadAllLines(groundTruthDir + $"/GlobalCounts.txt")

    HTML(Flip.Make[|
        struct("Connect", connectColor.Apply(connect) :> ImageBase)
        struct("Connect (ground truth)", connectColor.Apply(masks["connect-moment"]) :> ImageBase)
        struct("Merge", mergeColor.Apply(merge) :> ImageBase)
        struct("Merge (ground truth)", mergeColor.Apply(masks["merge-moment"]) :> ImageBase)
        struct("Reference", mapExposure.Apply(reference) :> ImageBase)
        struct("Render", mapExposure.Apply(render) :> ImageBase)
    |]).Display() |> ignore

    HTML("<p>Global optimization:</p>").Display() |> ignore
    let struct(globalNumPaths, _, _) = globalDecision
    (Map [
        ("Filtered", globalNumPaths)
        ("Ground truth", Int32.Parse(lines[2]))
    ]).Display() |> ignore