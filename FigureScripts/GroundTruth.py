from Common import *

scene = "HotLivingMod"
def make_scene_grid(scene, is_first):
    dirname = "../VcmExperiment/Results/EqualTime/" + scene
    ref = sio.read(dirname + "/Reference.exr")
    moment_mask = sio.read_layered_exr(dirname + "/GroundTruthMoment60s/RenderMasks.exr")["merge"]
    var_mask = sio.read_layered_exr(dirname + "/GroundTruthVariance60s/RenderMasks.exr")["merge"]

    moment_render = sio.read(dirname + "/GroundTruthMoment60s/Render.exr")
    var_render = sio.read(dirname + "/GroundTruthVariance60s/Render.exr")
    vcm_render = sio.read(dirname + "/VanillaVcm60s/Render.exr")
    pt_render = sio.read(dirname + "/Pt60s/Render.exr")

    moment_err = sio.relative_mse_outlier_rejection(moment_render, ref)
    var_err = sio.relative_mse_outlier_rejection(var_render, ref)
    vcm_err = sio.relative_mse_outlier_rejection(vcm_render, ref)
    pt_err = sio.relative_mse_outlier_rejection(pt_render, ref)

    def tonemap(img): return fig.JPEG(sio.lin_to_srgb(sio.exposure(img, 1)))

    with open("../VcmExperiment/Results/GroundTruth/" + scene + "/GlobalCounts.txt") as fp:
        lines = fp.readlines()
        (n_moment, c_moment, n_var, c_var) = int(lines[16]), int(lines[17]), int(lines[14]), int(lines[15])

    grid = fig.Grid(1, 3)
    grid[0,0].set_image(tonemap(ref))
    grid[0,1].set_image(colormap(moment_mask))
    grid[0,2].set_image(colormap(var_mask))

    if is_first:
        grid.set_col_titles("top", ["Reference", "Moment", "Variance"])
        grid.layout.set_col_titles("top", 2.5, fontsize=7)

    grid.set_col_titles("bottom", [
        "$n, c$\\\\speed-up",
        f"${n_moment}, {c_moment}$\\\\${vcm_err/ moment_err:.2f}\\times$",
        f"${n_var}, {c_var}$\\\\${vcm_err/var_err:.2f}\\times$"
    ])

    grid.layout.set_col_titles("bottom", 5, fontsize=7, offset_mm=0.5)

    return grid

fig.figure([
    [make_scene_grid("CountryKitchen", True)],
    [make_scene_grid("HotLivingMod", False)],
    [make_scene_grid("LampCausticNoShade", False)],
], 8.5, "GroundTruthResult.pdf", backend)