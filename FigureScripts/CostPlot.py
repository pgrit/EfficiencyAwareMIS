from Common import *
import json, sys

json_filename = sys.argv[1]
filename = sys.argv[2]
name = sys.argv[3]

with open(json_filename) as fp:
    data = json.load(fp)

grid = fig.Grid(1, len(data))
i = 0
for scene in data:
    x = data[scene]["Item1"]
    time = data[scene]["Item2"]
    heuristic = data[scene]["Item3"]
    ratio = data[scene]["Item4"]

    plot = fig.PgfLinePlot(0.6, [(x, time), (x, heuristic), (x, ratio)])
    if i == 1:
        plot.set_legend(["Time", "Heuristic", "Ratio"], fontsize_pt=7, pos=(0.02, 1), anchor="north west")
    plot.set_axis_label("x", name)
    plot.set_padding(left_mm=3.5, bottom_mm=3.5)
    if len(x) > 2:
        plot.set_axis_properties("x", [x[3]], use_log_scale=False)
    else:
        plot.set_axis_properties("x", [], use_log_scale=False)
    plot.set_axis_properties("y", [1, 2], range=(0.7,3), use_log_scale=False)

    grid[0, i].set_image(plot)
    i += 1

grid.set_col_titles("top", [k for k in data])

fig.figure([[grid]], 17.8, filename, backend)
