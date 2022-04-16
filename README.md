# Efficiency-Aware Multiple Importance Sampling for Bidirectional Rendering

Implementation of the paper "Efficiency-Aware Multiple Importance Sampling for Bidirectional Rendering". This is a cleaned re-implementation of the original code with additional improvements. This is not the version that was used to generate the results in the paper. Results are similar but not identical.

Compared to the paper, the main changes are:
- Number of connections can be controlled on a per-pixel basis (experimental, disabled by default)
- A simple iterative update scheme: the optimizer is run multiple times with exponentially growing time between subsequent updates
- Outlier rejection: the 0.1% of pixels with highest second moment are ignored when optimizing per-image sample counts
- A more elaborate cost heuristic that incorporates the cost of building the photon map acceleration structure (experimental, the old heuristic is also implemented but commented out in [VcmExperiment/CostHeuristic.cs](VcmExperiment/CostHeuristic.cs))

## Dependencies

- [.NET 6.0](https://dotnet.microsoft.com/download)

## Running the experiments

There are multiple experiments in this repository. By default, all except for a simple equal-time comparison are commented out in [Program.cs](VcmExperiment/Program.cs). Downloading the dependencies, building, and running the experiment(s) is as simple as:

```sh
cd VcmExperiment
dotnet run -c Release
```

The rendered results can be viewed by manually opening the .exr files in `VcmExperiment/Results/`. There is also a [.NET interactive](https://github.com/dotnet/interactive) notebook, [FigureScripts/EqualTime.dib](FigureScripts/EqualTime.dib), that loads the rendered images, computes the speed-ups in terms of relMSE, and displays the images in interactive viewers.

## Project structure

This repository contains a lot of code for debugging, testing, and visualizing various things. Not all of that is properly documented.

The following core pieces are most relevant to understand the method or find out how to implement it in your own renderer. Sorted from most interesting to least interesting:
- [VcmOptimizer.cs](VcmExperiment/VcmOptimizer.cs) implements the optimizer itself, i.e., the functions `OptPerPixel` and `OptPerImage` from Algorithm 1 in the paper.
- [AdaptiveVcm.cs](VcmExperiment/AdaptiveVcm.cs) integrates this logic into a VCM integrator and computes the second moments via our correction factors.
- [OnDemandVcm.cs](VcmExperiment/OnDemandVcm.cs) specializes the integrator to start with unidirectional path tracing and a reduced set of candidates
- [MomentEstimatingVcm](VcmExperiment/MomentEstimatingVcm.cs) computes the MIS weights of the proxy strategy for each sample (basically a slightly modified copy&paste of the normal MIS computations)
- [PathLengthEstimatingVcm](VcmExperiment/PathLengthEstimatingVcm.cs) computes the path lengths and photon count statistics used by our cost heuristic
- [CorrelAwareVcm](VcmExperiment/CorrelAwareVcm.cs) is a slightly modified version of the correlation-aware MIS weights
- [MergeMaskVcm](VcmExperiment/MergeMaskVcm.cs) implements a VCM integrator where merging is enabled on a per-pixel basis, and the number of connections is also set per-pixel

