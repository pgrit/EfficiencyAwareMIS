# Efficiency-Aware Multiple Importance Sampling for Bidirectional Rendering

Implementation of the paper "Efficiency-Aware Multiple Importance Sampling for Bidirectional Rendering". This is a cleaned re-implementation of the original code with additional improvements. This is not the version that was used to generate the results in the paper. Results are similar but not identical.

Compared to the paper, the main changes are:
- Number of connections can be controlled on a per-pixel basis
- A simple iterative update scheme: the optimizer is run multiple times with exponentially growing time between subsequent updates
- Outlier rejection: the 0.1% of pixels with highest second moment are ignored when optimizing per-image sample counts
- A more elaborate cost heuristic that incorporates the cost of building the photon map acceleration structure (experimental, seems to have little to no effect on the optimization outcome)

## Dependencies



## Running the experiments

TODO how to run

TODO how to inspect results


## Project structure

TODO pointers to key pieces from the paper (ref pseudo code)

TODO description of each file + priority + relationship

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
