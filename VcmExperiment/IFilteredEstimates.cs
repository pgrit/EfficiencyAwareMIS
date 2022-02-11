namespace EfficiencyAwareMIS.VcmExperiment;

public interface IFilteredEstimates {
    void AtomicAdd(int col, int row, float value);
    void Prepare();
    float Query(int col, int row);
    void Scale(float v);
    MonochromeImage ToImage();
}
