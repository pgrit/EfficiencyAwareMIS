from Common import *
import sys

scene = sys.argv[1]
exposure = float(sys.argv[2])
is_top = bool(int(sys.argv[3]))
show_renders = bool(int(sys.argv[4]))
filename = sys.argv[5]

def tonemap(img): return fig.JPEG(sio.lin_to_srgb(sio.exposure(img, exposure)))

dirname = "../VcmExperiment/Results/Filtering/" + scene
ref = sio.read(dirname + "/Reference.exr")
(w, h, _) = ref.shape
render = sio.read(dirname + "/MomentEstimator/Render.exr")

masks = sio.read_layered_exr("GeneratedMasks.exr")

if show_renders:
    grid = fig.Grid(1, len(masks) + 2)
else:
    grid = fig.Grid(1, len(masks))
grid[0,0].set_image(tonemap(ref))
grid[0,1].set_image(tonemap(render))

i = 2 if show_renders else 0
for k in masks:
    grid[0,i].set_image(fig.PNG(masks[k]))
    i += 1

names = ["Reference", "Rendered"] if show_renders else []
for k in masks:
    names.append(k[4:])

if is_top:
    grid.set_col_titles("top", names)
    grid.layout.set_col_titles("top", 2.5, fontsize=7)
else:
    grid.layout.set_padding(1)

fig.figure([
    [grid]
], 17.8, filename, backend)