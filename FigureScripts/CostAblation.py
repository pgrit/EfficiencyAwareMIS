from Common import *

def GetRender(scene, suffix, mode):
    dirname = "../VcmExperiment/Results/EqualTime/" + scene + f"/GroundTruth{mode}60s" + suffix
    return sio.read(dirname + "/Render.exr")

def GetReference(scene):
    return sio.read("../VcmExperiment/Results/EqualTime/" + scene + "/Reference.exr")

def GetError(scene, suffix, mode):
    return sio.relative_mse_outlier_rejection(GetRender(scene, suffix, mode), GetReference(scene), 0.1)

def GetMergeMasks(scene, suffix, prefix):
    dirname = "../VcmExperiment/Results/GroundTruth/" + scene
    masks = sio.read_layered_exr(dirname + f"/{prefix}Masks{suffix}.exr")
    with open(dirname + f"/{prefix}GlobalCounts{suffix}.txt") as fp:
        lines = fp.readlines()
    numPathsVar = int(lines[14])
    numConnectVar = int(lines[15])
    numPathsMoment = int(lines[16])
    numConnectMoment = int(lines[17])
    return masks["merge-var"], masks["merge-moment"], numPathsVar, numConnectVar, numPathsMoment, numConnectMoment

def CostAblation(scene, exposure, mode, is_top):
    correct = GetMergeMasks(scene, "", "")
    x01 = GetMergeMasks(scene, "CostMerge0.1", "")
    x05 = GetMergeMasks(scene, "CostMerge0.5", "")
    x2 = GetMergeMasks(scene, "CostMerge2", "")
    x10 = GetMergeMasks(scene, "CostMerge10", "")

    def varmask(m): return m[0]
    def momentmask(m): return m[1]
    def varnumpaths(m): return m[2]
    def varnumconnect(m): return m[3]
    def momentnumpaths(m): return m[4]
    def momentnumconnect(m): return m[5]

    mask = momentmask if mode == "Moment" else varmask
    numPaths = momentnumpaths if mode == "Moment" else varnumpaths
    numConnect = momentnumconnect if mode == "Moment" else varnumconnect

    grid = fig.Grid(1, 5)
    grid[0, 0].set_image(fig.PNG(mask(x01)))
    grid[0, 1].set_image(fig.PNG(mask(x05)))
    grid[0, 2].set_image(fig.PNG(mask(correct)))
    grid[0, 3].set_image(fig.PNG(mask(x2)))
    grid[0, 4].set_image(fig.PNG(mask(x10)))
    if is_top:
        grid.set_col_titles("top", [
            "Merges 10x cheaper",
            "Merges 2x cheaper",
            "Correct cost",
            "Merges 2x more expensive",
            "Merges 10x more expensive",
        ])
    else:
        grid.layout.set_padding(top=1)

    errors = [
        GetError(scene, "CostMerge0.1", mode),
        GetError(scene, "CostMerge0.5", mode),
        GetError(scene, "", mode),
        GetError(scene, "CostMerge2", mode),
        GetError(scene, "CostMerge10", mode),
    ]
    errors = [ errors[2] / e for e in errors ]

    grid.set_col_titles("bottom", [
        f"$n = {numPaths(x01)}, c = {numConnect(x01)}$\\\\Speed-up: ${errors[0]:.2f}\\times$",
        f"$n = {numPaths(x05)}, c = {numConnect(x05)}$\\\\Speed-up: ${errors[1]:.2f}\\times$",
        f"$n = {numPaths(correct)}, c = {numConnect(correct)}$\\\\Speed-up: ${errors[2]:.2f}\\times$",
        f"$n = {numPaths(x2)}, c = {numConnect(x2)}$\\\\Speed-up: ${errors[3]:.2f}\\times$",
        f"$n = {numPaths(x10)}, c = {numConnect(x10)}$\\\\Speed-up: ${errors[4]:.2f}\\times$",
    ])

    grid.set_row_titles("left", [scene])

    grid.layout.set_col_titles("bottom", 5, offset_mm=1)

    return grid

fig.figure([
    [CostAblation("BoxLargeLight", -4, "Moment", True)],
    [CostAblation("CountryKitchen", -4, "Moment", False)],
    [CostAblation("LampCaustic", -4, "Moment", False)],
    [CostAblation("RoughGlassesIndirect", -4, "Moment", False)],
], 17.8, "CostAblation.pdf", backend)