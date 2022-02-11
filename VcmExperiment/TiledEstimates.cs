namespace EfficiencyAwareMIS.VcmExperiment;

public class TiledEstimates : IFilteredEstimates {
    int orgWidth, orgHeight;
    int tileSize;
    int leftoverCol, leftoverRow;
    MonochromeImage tiles;

    public TiledEstimates(int width, int height, int tileSize) {
        int w = width / tileSize;
        int h = height / tileSize;
        tiles = new(w, h);

        orgWidth = width;
        orgHeight = height;
        this.tileSize = tileSize;

        // If width or height are not multiples of tileSize, the last tile in each row / column is grown
        // to include the leftover pixels. This ensures that we do not have bad estimates in tiny tiles.
        leftoverCol = width % tileSize;
        leftoverRow = height % tileSize;
    }

    public void AtomicAdd(int col, int row, float value) {
        int c = Math.Clamp(col / tileSize, 0, tiles.Width - 1);
        int r = Math.Clamp(row / tileSize, 0, tiles.Height - 1);
        tiles.AtomicAdd(c, r, value);
    }

    public void Prepare() {}

    public float Query(int col, int row) {
        int c = Math.Clamp(col / tileSize, 0, tiles.Width - 1);
        int r = Math.Clamp(row / tileSize, 0, tiles.Height - 1);
        float v = tiles.GetPixel(c, r);

        // Normalize
        float tileWidth = tileSize;
        float tileHeight = tileSize;
        if (col / tileSize >= tiles.Width) tileWidth += leftoverCol;
        if (row / tileSize >= tiles.Height) tileHeight += leftoverRow;
        return v / (tileWidth * tileHeight);
    }

    public void Scale(float v) {
        tiles.Scale(v);
    }

    public MonochromeImage ToImage() {
        MonochromeImage img = new(orgWidth, orgHeight);
        for (int row = 0; row < orgHeight; ++row) {
            for (int col = 0; col < orgWidth; ++col) {
                img.SetPixel(col, row, Query(col, row));
            }
        }
        return img;
    }
}