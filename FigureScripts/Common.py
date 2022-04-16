import figuregen as fig
import simpleimageio as sio
import matplotlib.pyplot as plt
import numpy as np

def outline(text, outline_clr=[10,10,10], text_clr=[250,250,250]):
    if outline_clr is None:
        res = "\\definecolor{FillClr}{RGB}{" + f"{text_clr[0]},{text_clr[1]},{text_clr[2]}" + "}"
        return res + "\\textcolor{FillClr}{" + text + "}"

    res = "\\DeclareDocumentCommand{\\Outlined}{ O{black} O{white} O{0.55pt} m }{"\
            "\\contourlength{#3}"\
            "\\contour{#2}{\\textcolor{#1}{#4}}"\
        "}"
    res += "\\definecolor{FillClr}{RGB}{" + f"{text_clr[0]},{text_clr[1]},{text_clr[2]}" + "}"
    res += "\\definecolor{StrokeClr}{RGB}{" + f"{outline_clr[0]},{outline_clr[1]},{outline_clr[2]}" + "}"

    res += "\\Outlined[FillClr][StrokeClr][0.55pt]{"+ text + "}"
    return res

def colormap(img):
    cm = plt.get_cmap('inferno')
    if len(img.shape) == 2:
        return fig.PNG(cm(img[:,:])[:,:,:3])
    else:
        return fig.PNG(cm(img[:,:,0])[:,:,:3])

def colorbar():
    cm = plt.get_cmap('inferno')
    gradient = np.linspace(1, 0, 256)
    gradient = np.vstack((gradient, gradient))
    bar = np.swapaxes(cm(gradient), 0, 1)
    bar = np.repeat(bar, 10, axis=1)
    return bar

backend = fig.PdfBackend(None, [
    "\\usepackage[utf8]{inputenc}",
    "\\usepackage[T1]{fontenc}",
    "\\usepackage{libertine}"
    "\\usepackage{color}"
    "\\usepackage{xparse}"
    "\\usepackage[outline]{contour}"
    "\\renewcommand{\\familydefault}{\\sfdefault}"
])