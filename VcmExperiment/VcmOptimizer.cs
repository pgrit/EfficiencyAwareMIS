using System.Linq;

namespace EfficiencyAwareMIS.VcmExperiment;

public record struct Candidate(int NumLightPaths, int NumConnections, bool Merge) {
    public override string ToString() => $"n={NumLightPaths:000000},c={NumConnections:00},m={(Merge ? 1 : 0)}";
}

/// <summary>
/// Searches the best candidate configuration given a set of per-pixel error estimates and a cost heuristic.
/// </summary>
class VcmOptimizer {
    /// <summary>
    /// Filters the per-pixel merging decisions and writes the result to the same mask.
    /// </summary>
    /// <param name="mask">Input / Output merge mask</param>
    static void FilterMergeMask(ref MonochromeImage mask) {
        // TODO make this configurable: delgate / FilterPipeline object / ...

        // MonochromeImage buf = new(mask.Width, mask.Height);
        // Filter.Dilation(mask, buf, 8);
        // Filter.RepeatedBox(buf, mask, 16);
    }

    /// <summary>
    /// Filters the per-pixel connection counts and writes the result to the same mask.
    /// </summary>
    /// <param name="mask">Input / Output connection mask</param>
    static void FilterConnectMask(ref MonochromeImage mask) {
        // MonochromeImage buf = new(mask.Width, mask.Height);
        // Filter.Dilation(mask, buf, 3);
        // Filter.RepeatedBox(buf, mask, 16);
    }

    public static void OptimizePerPixel(Dictionary<Candidate, MonochromeImage> filteredErrors,
                                        CostHeuristic costHeuristic, bool perPixelMerge, bool perPixelConnect,
                                        out MonochromeImage mergeMask, out MonochromeImage connectMask) {
        int width = filteredErrors.First().Value.Width;
        int height = filteredErrors.First().Value.Height;

        MonochromeImage merge = perPixelMerge ? new(width, height) : null;
        MonochromeImage connect = perPixelConnect ? new(width, height) : null;
        Parallel.For(0, height, row => {
            for (int col = 0; col < width; ++col) {
                // Search best full candidate in this pixel
                float bestWorkNorm = float.MaxValue;
                Candidate bestCandidate = new();
                foreach (var (candidate, moment) in filteredErrors) {
                    float cost = costHeuristic.EvaluatePerPixel(candidate.NumLightPaths,
                        candidate.NumConnections, (candidate.Merge ? 1.0f : 0.0f));
                    Debug.Assert(float.IsFinite(cost));
                    Debug.Assert(cost > 0);

                    float workNorm = moment.GetPixel(col, row) * cost;

                    if (workNorm < bestWorkNorm) {
                        bestWorkNorm = workNorm;
                        bestCandidate = candidate;
                    }
                }

                if (bestWorkNorm == 0.0f) { // disable all techs in completely black pixels
                    bestCandidate = new(0, 0, false);
                }

                // Extract the per-pixel components from the best candidate
                merge?.SetPixel(col, row, bestCandidate.Merge ? 1 : 0);
                connect?.SetPixel(col, row, bestCandidate.NumConnections);
            }
        });

        // Filtering the results prevents artifacts from single pixels / groups of pixels making bad decisions
        // based on noisy or missing data.
        if (perPixelMerge) FilterMergeMask(ref merge);
        if (perPixelConnect) FilterConnectMask(ref connect);

        mergeMask = merge;
        connectMask = connect;
    }

    // /// <summary>
    // /// Creates buffers to store the marginalized relative moments and costs of all global sub-strategies
    // /// </summary>
    // (Dictionary<Candidate, float> RelMoments, Dictionary<Candidate, float> Costs) MakeBuffers() {
    //     // Initialize total cost and moment for all per-image candidates
    //     Dictionary<Candidate, float> relMoments = new();
    //     Dictionary<Candidate, float> costs = new();
    //     int numPixels = width * height;
    //     foreach (float nRel in NumLightPathCandidates) {
    //         int n = (int)(numPixels * nRel);
    //         if (UsePerPixelConnections) {
    //             relMoments[new(n, 0, false)] = 0.0f;
    //             if (!UsePerPixelMerges) relMoments[new(n, 0, true)] = 0.0f;

    //             costs[new(n, 0, false)] = 0.0f;
    //             if (!UsePerPixelMerges) costs[new(n, 0, true)] = 0.0f;
    //         } else {
    //             foreach (int c in NumConnectionsCandidates) {
    //                 relMoments[new(n, c, false)] = 0.0f;
    //                 if (!UsePerPixelMerges) relMoments[new(n, c, true)] = 0.0f;

    //                 costs[new(n, c, false)] = 0.0f;
    //                 if (!UsePerPixelMerges) costs[new(n, c, true)] = 0.0f;
    //             }
    //         }
    //     }
    //     // Add path tracing
    //     relMoments[new(0, 0, false)] = 0.0f;
    //     costs[new(0, 0, false)] = 0.0f;

    //     return (relMoments, costs);
    // }

    // float ComputeOutlierThreshold(MonochromeImage image) {
    //     int invPercentage = 50000; // = 1 / 0.002% = 1 / 0.00002
    //     int numPixels = image.Width * image.Height;
    //     int n = numPixels / invPercentage;

    //     PriorityQueue<float, float> nLargest = new();
    //     for (int row = 0; row < image.Height; ++row) {
    //         for (int col = 0; col < image.Width; ++col) {
    //             float v = image.GetPixel(col, row);
    //             if (nLargest.Count >= n) v = Math.Max(nLargest.Dequeue(), v);
    //             nLargest.Enqueue(v, v);
    //         }
    //     }
    //     var threshold = nLargest.Dequeue();

    //     return threshold;
    // }

    // void OptimizePerImage() {
    //     var (relMoments, costs) = MakeBuffers();

    //     // Create buffers to store the per-pixel marginalized values of the global candidates for debugging
    //     if (WriteDebugInfo && marginalizedMoments == null) {
    //         marginalizedMoments = new();
    //         marginalizedCosts = new();
    //         foreach (var (candidate, _) in relMoments) {
    //             marginalizedMoments.Add(candidate, new(width, height));
    //             marginalizedCosts.Add(candidate, new(width, height));
    //         }
    //     }

    //     // For robustness, we reject a small percentage of largest values as outliers. For convenience,
    //     // we first find these outliers and set a threshold for each candidate. This can be optimized, e.g.,
    //     // by folding it into the optimization itself so it is done once per pixel and not once per pixel and
    //     // candidate. (currently costs around 20ms per optimization run)
    //     Dictionary<Candidate, float> outlierThresholds = new();
    //     Parallel.ForEach(momentImages, elem => {
    //         float t = ComputeOutlierThreshold(elem.Value);
    //         lock(outlierThresholds) outlierThresholds.Add(elem.Key, t);
    //     });

    //     // Accumulate relative moments and cost, using a parallel reduction with per-line buffering
    //     float recipNumPixels = 1.0f / (width * height);
    //     int numOutliers = 0;
    //     Parallel.For(0, height, row => {
    //         // Allocate a buffer for this line (TODO could be made thread-local and re-use the memory)
    //         var lineBuffers = MakeBuffers();

    //         for (int col = 0; col < width; ++col) {
    //             // Merges are considered enabled if there's at least a 10% probability of them happening
    //             float mergeProb = GetPerPixelMergeProbability(new(col, row));
    //             bool mergeDecision = mergeProb > 0.1f;

    //             // Find closest candidate for the actual number of connections
    //             float connectCount = GetPerPixelConnectionCount(new(col, row));
    //             int candidateConnect = NumConnectionsCandidates[^1];
    //             for (int i = 0; i < NumConnectionsCandidates.Length; ++i) {
    //                 if (NumConnectionsCandidates[i] >= connectCount) {
    //                     candidateConnect = NumConnectionsCandidates[i];
    //                     break;
    //                 }
    //             }

    //             float mean = denoisedImage.GetPixel(col, row);
    //             if (mean == 0.0f) continue; // skip completely black pixels
    //             float recipMeanSquare = 1.0f / (mean * mean);

    //             foreach (var (c, moment) in momentImages) {
    //                 // Filter out candidates that match the per-pixel decision, except the path tracer
    //                 if (c.NumLightPaths != 0) {
    //                     if (UsePerPixelMerges && c.Merge != mergeDecision)
    //                         continue;
    //                     if (UsePerPixelConnections && c.NumConnections != candidateConnect)
    //                         continue;
    //                 }

    //                 var g = c;
    //                 if (UsePerPixelMerges) g.Merge = false;
    //                 if (UsePerPixelConnections) g.NumConnections = 0;

    //                 var relMoment = moment.GetPixel(col, row);
    //                 var cost = CostHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections, c.Merge ? 1 : 0);

    //                 // Very simple outlier removal. Without this, a single firefly pixel can completely
    //                 // change the result. A hard-coded constant works, because we consider only the _relative_
    //                 // moments. In a practical application, you may want to use something more elaborate.
    //                 if (relMoment > outlierThresholds[c]) { //1e5) {
    //                     relMoment = 1.0f;
    //                     Interlocked.Increment(ref numOutliers);
    //                 }

    //                 // Accumulate in the per-line storage. We scale by the number of pixels to get resolution
    //                 // independent values that are easier to interpret when debugging. Has no effect on the
    //                 // outcome since recipNumPixels is a constant.
    //                 lineBuffers.RelMoments[g] += relMoment * recipNumPixels;
    //                 lineBuffers.Costs[g] += cost * recipNumPixels;

    //                 // If the debugging buffers exist, track the last marginalized per-pixel value in them.
    //                 // We use "SetPixel", i.e. overwrite instead of accumulate, to not distort the values.
    //                 // Intermediate states are currently not kept, only the last outcome.
    //                 marginalizedMoments?[g]?.SetPixel(col, row, relMoment);
    //                 marginalizedCosts?[g]?.SetPixel(col, row, cost);
    //             }
    //         }

    //         lock (relMoments) {
    //             foreach (var (c, v) in lineBuffers.RelMoments) {
    //                 relMoments[c] += v;
    //             }
    //             foreach (var (c, v) in lineBuffers.Costs) {
    //                 costs[c] += v;
    //             }
    //         }
    //     });

    //     if (numOutliers > width * height * 0.0001) {
    //         Logger.Warning($"Rejected {numOutliers} outliers, which is more than 0.01% of all pixels.");
    //     }

    //     // Find the best candidate in a simple linear search
    //     float bestWorkNorm = float.MaxValue;
    //     Candidate bestCandidate = new();
    //     foreach (var (candidate, moment) in relMoments) {
    //         float cost = costs[candidate];
    //         float workNorm = moment * cost;
    //         if (workNorm < bestWorkNorm) {
    //             bestWorkNorm = workNorm;
    //             bestCandidate = candidate;
    //         }
    //     }

    //     // Keep track of the history of candidate values in the frame buffer
    //     if (WriteDebugInfo) {
    //         if (!Scene.FrameBuffer.MetaData.ContainsKey("GlobalCandidateHistory")) {
    //             Scene.FrameBuffer.MetaData["GlobalCandidateHistory"] =
    //                 new List<Dictionary<string, Dictionary<string, float>>>();
    //         }
    //         // Copy in a dictionary with a string key so it can be serialized
    //         Dictionary<string, float> relMomentSerialized = new();
    //         Dictionary<string, float> costSerialized = new();
    //         foreach (var (c, m) in relMoments) {
    //             relMomentSerialized[c.ToString()] = m;
    //             costSerialized[c.ToString()] = costs[c];
    //         }
    //         Scene.FrameBuffer.MetaData["GlobalCandidateHistory"].Add(new Dictionary<string, Dictionary<string, float>>() {
    //             {"RelativeMoment", relMomentSerialized},
    //             {"Cost", costSerialized }
    //         });
    //     }

    //     Scene.FrameBuffer.MetaData["PerImageDecision"] = bestCandidate;
    //     Console.WriteLine(bestCandidate); // TODO proper logging of the history in a list

    //     NumLightPaths = bestCandidate.NumLightPaths;
    //     if (!UsePerPixelConnections) NumConnections = bestCandidate.NumConnections;
    //     if (!UsePerPixelMerges) useMergesGlobal = bestCandidate.Merge;
    // }
}
