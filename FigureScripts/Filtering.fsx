#r "nuget: SimpleImageIO"
#r "../VcmExperiment/bin/Release/net6.0/VcmExperiment.dll"

open EfficiencyAwareMIS.VcmExperiment
open System.IO
open SimpleImageIO

// Our filter primitives: blur, dilate then blur, and do nothing
let blur radius (img:MonochromeImage) =
    let buf = new MonochromeImage(img.Width, img.Height)
    Filter.RepeatedBox(img, buf, radius)
    buf

let dilate radius (img:MonochromeImage) =
    let buf = new MonochromeImage(img.Width, img.Height)
    Filter.Dilation(img, buf, radius)
    buf

let dilateBlur dilateRadius blurRadius (img:MonochromeImage) =
    let buf = new MonochromeImage(img.Width, img.Height)
    Filter.Dilation(img, buf, dilateRadius)
    Filter.RepeatedBox(buf, img, blurRadius)
    img

let noop img = img

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

let InitCostHeuristic scene =
    let dir = "../VcmExperiment/Results/GroundTruth/" + scene
    let path = Path.Join(dir, "MomentEstimator", "Render.json")
    let json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path))
    let avgCamLen = json.RootElement.GetProperty("AverageCameraPathLength").GetSingle()
    let avgLightLen = json.RootElement.GetProperty("AverageLightPathLength").GetSingle()
    let avgPhoton = json.RootElement.GetProperty("AveragePhotonsPerQuery").GetSingle()
    let costHeuristic = new CostHeuristic()
    costHeuristic.UpdateStats(width * height, width * height, avgCamLen, avgLightLen, avgPhoton)
    costHeuristic

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

    let costHeuristic = InitCostHeuristic scene

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

let MakeFilterFigure scene exposure istop showrender filename preOps postOps =
    let groundTruthDir = "../VcmExperiment/Results/GroundTruth/" + scene
    let convergedMasks = Layers.LoadFromFile(groundTruthDir + $"/Masks.exr")

    let mutable idx = 0
    let mutable masks = [|
        for preName, pre in preOps do
            for postName, post in postOps do
                let (mask, _, _) = OptimizeWithFilter scene 1f pre post noop
                idx <- idx + 1
                struct($"i{idx:d2}-{preName}-{postName}", mask :> ImageBase)
    |]
    if showrender then
        masks <- Array.insertAt 0 struct("z99-Reference", convergedMasks["merge-moment"]) masks
    Layers.WriteToExr("GeneratedMasks.exr", masks)

    let p = new System.Diagnostics.Process()
    p.StartInfo.UseShellExecute <- false
    p.StartInfo.RedirectStandardOutput <- true
    p.StartInfo.RedirectStandardError <- true
    p.StartInfo.FileName <- "python"
    p.StartInfo.Arguments <- "./Filtering.py " + scene + " " + $"{exposure} {(if istop then 1 else 0)} {(if showrender then 1 else 0)} {filename}"
    p.Start() |> ignore
    let output = p.StandardOutput.ReadToEnd()
    let errout = p.StandardError.ReadToEnd()
    p.WaitForExit()
    System.Console.WriteLine(output + "\n" + errout)

MakeFilterFigure "Pool" -4 true true "Filtering01.pdf"
<|
[
    "None", noop
    "Blur1", blur 1
    "Blur3", blur 3
    "Blur10", blur 10
    "Blur100", blur 100
]
<|
[
    "None", noop
]

MakeFilterFigure "Pool" -4 true false "Filtering02.pdf"
<|
[
    "Blur3", blur 3
]
<|
[
    "Dilate1", dilate 1
    "Dilate2", dilate 2
    "Dilate4", dilate 4
    "Dilate8", dilate 8
    "Dilate16", dilate 16
    "Dilate32", dilate 32
]

MakeFilterFigure "Pool" -4 true false "Filtering03.pdf"
<|
[
    "Blur3", blur 3
]
<|
[
    "Dilate16", dilate 16
    "Dilate16-Blur1", dilateBlur 16 1
    "Dilate16-Blur2", dilateBlur 16 2
    "Dilate16-Blur4", dilateBlur 16 4
    "Dilate16-Blur8", dilateBlur 16 8
    "Dilate16-Blur16", dilateBlur 16 16
    "Dilate16-Blur32", dilateBlur 16 32
]

MakeFilterFigure "RoughGlasses" 1 true true "Filtering04.pdf"
<|
[
    "None", noop
    "Blur1", blur 1
    "Blur3", blur 3
    "Blur10", blur 10
    "Blur100", blur 100
]
<|
[
    "None", noop
]

MakeFilterFigure "RoughGlasses" 1 true false "Filtering05.pdf"
<|
[
    "Blur3", blur 3
]
<|
[
    "Dilate1", dilate 1
    "Dilate2", dilate 2
    "Dilate4", dilate 4
    "Dilate8", dilate 8
    "Dilate16", dilate 16
    "Dilate32", dilate 32
]

MakeFilterFigure "RoughGlasses" 1 true false "Filtering06.pdf"
<|
[
    "Blur3", blur 3
]
<|
[
    "Dilate8", dilate 8
    "Dilate8-Blur1", dilateBlur 8 1
    "Dilate8-Blur2", dilateBlur 8 2
    "Dilate8-Blur4", dilateBlur 8 4
    "Dilate8-Blur8", dilateBlur 8 8
    "Dilate8-Blur16", dilateBlur 8 16
    "Dilate8-Blur32", dilateBlur 8 32
]