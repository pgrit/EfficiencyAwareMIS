namespace EfficiencyAwareMIS.VcmExperiment;

public class BlurredEstimates : IFilteredEstimates {
    int radius;
    MonochromeImage tiles;
    MonochromeImage blurred;

    public BlurredEstimates(int width, int height, int radius) {
        tiles = new(width, height);
        this.radius = radius;
    }

    public void AtomicAdd(int col, int row, float value) {
        tiles.AtomicAdd(col, row, value);
    }

    public void Prepare() {
        blurred = tiles;
        blurred = new(tiles.Width, tiles.Height);
        Filter.RepeatedBox(tiles, blurred, radius);
    }

    public float Query(int col, int row) {
        return blurred.GetPixel(col, row);
    }

    public void Scale(float v) {
        tiles.Scale(v);
    }

    public MonochromeImage ToImage() {
        return blurred;
    }
}