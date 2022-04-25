from Common import *

def make_scene_grid(scene, exposure, is_top, first, second, ratio_latex, crop):
    dirname = "../VcmExperiment/Results/GroundTruth/" + scene
    ref = crop(sio.read(dirname + "/Reference.exr"))
    (w, h, _) = ref.shape

    moments = sio.read_layered_exr(dirname + "/Moments.exr")
    variances = sio.read_layered_exr(dirname + "/Variances.exr")
    firstMoment = crop(moments[first])
    secondMoment = crop(moments[second])
    firstVariance = crop(variances[first])
    secondVariance = crop(variances[second])
    moment_ratio = np.log(firstMoment / secondMoment)
    moment_ratio[secondMoment == 0] = 1
    moment_ratio[firstMoment / secondMoment <= 0] = 1
    variance_ratio = np.log(firstVariance / secondVariance)
    variance_ratio[secondVariance == 0] = 1
    variance_ratio[firstVariance / secondVariance <= 0] = 1

    geom_mean_moment = np.exp(np.sum(moment_ratio) / (w * h))
    geom_mean_var = np.exp(np.sum(variance_ratio) / (w * h))

    norm = np.percentile(moment_ratio, 95)
    moment_ratio /= norm
    variance_ratio /= norm

    def tonemap(img): return fig.JPEG(sio.lin_to_srgb(sio.exposure(img, exposure)))

    grid = fig.Grid(1, 3)
    grid[0,0].set_image(tonemap(ref))
    grid[0,1].set_image(colormap(moment_ratio))
    grid[0,2].set_image(colormap(variance_ratio))

    grid.set_col_titles("bottom", [
        "Average ratio",
        f"${geom_mean_moment:.2f}$",
        f"${geom_mean_var:.2f}$ (${geom_mean_moment / geom_mean_var:.2f}\\times$)"
    ])
    grid.layout.set_col_titles("bottom", 2.5, offset_mm=0.5, fontsize=7)

    if is_top:
        grid.set_col_titles("top", ["Reference", "Moment ratio", "Variance ratio"])
        grid.layout.set_col_titles("top", 2.5, fontsize=7)
    else:
        grid.layout.set_padding(1)

    grid.set_row_titles("right", [ratio_latex])
    grid.layout.set_row_titles("right", 12, offset_mm=0.5, fontsize=7, txt_rotation=0)

    return grid

def crop(img, left, top, width, height):
    if img.ndim == 3:
        return img[top:top+height,left:left+width,:]
    else:
        return img[top:top+height,left:left+width]

def veach_crop(img): return crop(img, 80, 0, 640-80*2, 480)
def no_crop(img): return img

fig.figure([
    [make_scene_grid("BoxLargeLight", -1, True, f"n={0:06},c=00,m=0", f"n={640*480*2:06},c=00,m=0", "$$\\frac{n=0}{n=614k}$$", no_crop)],
    [make_scene_grid("HotLivingMod", 1, False, f"n={0:06},c=00,m=0", f"n={640*480*2:06},c=00,m=0", "$$\\frac{n=0}{n=614k}$$", no_crop)],
    [make_scene_grid("TargetPractice", 1, False, f"n={0:06},c=00,m=0", f"n={640*480*2:06},c=00,m=0", "$$\\frac{n=0}{n=614k}$$", no_crop)],
], 8.5, "VarMomentRatiosLowVar.pdf", backend)

fig.figure([
    [make_scene_grid("VeachBidir", 1, True, f"n={640*480:06},c=00,m=0", f"n={640*480:06},c=01,m=0", "$$\\frac{c=0}{c=1}$$", veach_crop)],
    [make_scene_grid("VeachBidir", 1, False, f"n={640*480:06},c=00,m=0", f"n={640*480:06},c=16,m=0", "$$\\frac{c=0}{c=16}$$", veach_crop)],
    [make_scene_grid("VeachBidir", 1, False, f"n={640*480:06},c=00,m=0", f"n={640*480:06},c=00,m=1", "$$\\frac{\chi=0}{\chi=1}$$", veach_crop)],
], 8.5, "VarMomentRatiosCov.pdf", backend)