# EfficiencyAwareMIS

## Project structure

### VCM integrator

The integrator code is separated into multiple classes, to separate the core logic from bookkeeping and
PDF gathering code.

The key logic is implemented in [AdaptiveVcm](VcmExperiment/AdaptiveVcm.cs). Given the MIS weights of the proxy strategy for each sample, it estimates the required second moments and updates the current strategy.

The less interesting MIS computations and statistics gathering code are distributed over the following 4 classes (which inherit each other in the same order):

- [CorrelAwareVcm](VcmExperiment/CorrelAwareVcm.cs) is a slightly modified version of the correlation-aware MIS weights
- [MergeMaskVcm](VcmExperiment/MergeMaskVcm.cs) is a base-class for VCM integrators that perform merging with a per-pixel probability. Contains all the related MIS weight computation code
- [PathLengthEstimatingVcm](VcmExperiment/PathLengthEstimatingVcm.cs) contains the code to compute average path lengths and photon count statistics.
- [MomentEstimatingVcm](VcmExperiment/MomentEstimatingVcm.cs) computes the MIS weights of the proxy strategy and invokes an abstract method for each sample.

**TODO** point out how the different pieces of the pseudo code relate to code in this repo

### Cost heuristic

The cost heuristic is defined in [CostHeuristic.cs](VcmExperiment/CostHeuristic.cs).

### Experiments

TODO

### Figures