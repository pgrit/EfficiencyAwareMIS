using System.Linq;

namespace EfficiencyAwareMIS.VcmExperiment;

public record struct Candidate(int NumLightPaths, int? NumConnections, bool? Merge, bool? BuildPhotonMap = null) {
    public override string ToString() {
        string txt = $"n={NumLightPaths:000000}";
        if (NumConnections.HasValue) txt += $",c={NumConnections:00}";
        if (Merge.HasValue) txt += $",m={(Merge.Value ? 1 : 0)}";
        if (BuildPhotonMap.HasValue) txt += $",pm={(BuildPhotonMap.Value ? 1 : 0)}";
        return txt;
    }
}

/// <summary>
/// Searches the best candidate configuration given a set of per-pixel error estimates and a cost heuristic.
/// </summary>
public class VcmOptimizer {
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
                        candidate.NumConnections.Value, (candidate.Merge.Value ? 1.0f : 0.0f));
                    Debug.Assert(float.IsFinite(cost));
                    Debug.Assert(cost > 0);

                    float workNorm = moment.GetPixel(col, row) * cost;

                    if (workNorm < bestWorkNorm) {
                        bestWorkNorm = workNorm;
                        bestCandidate = candidate;
                    }
                }

                if (bestWorkNorm == 0.0f) { // disable all techs in completely black pixels
                    bestCandidate = new(0, 0, false, null);
                }

                // Extract the per-pixel components from the best candidate
                merge?.SetPixel(col, row, bestCandidate.Merge.Value ? 1 : 0);
                connect?.SetPixel(col, row, bestCandidate.NumConnections.Value);
            }
        });

        // Filtering the results prevents artifacts from single pixels / groups of pixels making bad decisions
        // based on noisy or missing data.
        if (perPixelMerge) FilterMergeMask(ref merge);
        if (perPixelConnect) FilterConnectMask(ref connect);

        mergeMask = merge;
        connectMask = connect;
    }

    /// <summary>
    /// Creates buffers to store the marginalized relative moments and costs of all global sub-strategies
    /// </summary>
    static void MakeBuffers(int numPixels, IEnumerable<float> numLightPathCandidates, bool perPixelConnect,
                            bool perPixelMerge, IEnumerable<int> numConnectCandidates,
                            out Dictionary<Candidate, float> errors, out Dictionary<Candidate, float> costs) {
        // We track the refs in local variables as local functions cannot access "out" parameters
        Dictionary<Candidate, float> errorsLocal = new(); errors = errorsLocal;
        Dictionary<Candidate, float> costsLocal = new(); costs = costsLocal;
        void Add(Candidate c) {
            errorsLocal[c] = 0.0f;
            costsLocal[c] = 0.0f;
        }

        foreach (float nRel in numLightPathCandidates) {
            int n = (int)(numPixels * nRel);
            if (perPixelConnect) {
                if (perPixelMerge) {
                    // Add(new(n, null, null, false));
                    Add(new(n, null, null, true));
                } else {
                    Add(new(n, null, true, true));
                    Add(new(n, null, false, false));
                }
            } else {
                foreach (int c in numConnectCandidates) {
                    if (perPixelMerge) {
                        // Add(new(n, c, null, false));
                        Add(new(n, c, null, true));
                    } else {
                        Add(new(n, c, true, true));
                        Add(new(n, c, false, false));
                    }
                }
            }
        }

        // path tracing
        Add(new(0, 0, false, false));
    }

    /// <summary>
    /// Computes a threshold for outlier clamping. Very conservative: only rejects 0.002% of the brightest pixels.
    /// </summary>
    /// <param name="image">The image with outliers</param>
    /// <returns>The threshold</returns>
    static float ComputeOutlierThreshold(MonochromeImage image) {
        int invPercentage = 50000; // = 1 / 0.002% = 1 / 0.00002 (at our test resolution, this is 6 pixels)
        int numPixels = image.Width * image.Height;
        int n = numPixels / invPercentage;

        PriorityQueue<float, float> nLargest = new();
        for (int row = 0; row < image.Height; ++row) {
            for (int col = 0; col < image.Width; ++col) {
                float v = image.GetPixel(col, row);
                if (nLargest.Count >= n) v = Math.Max(nLargest.Dequeue(), v);
                nLargest.Enqueue(v, v);
            }
        }
        var threshold = nLargest.Dequeue();

        return threshold;
    }

    public delegate float CountFn(int col, int row);

    /// <summary>
    /// Runs the per-image optimization
    /// </summary>
    /// <param name="errorImages">The per-pixel and per-candidate second moments or variances</param>
    /// <param name="referenceImage">Denoised rendering or reference used for relative error computation</param>
    /// <param name="numLightPathCandidates">Candidate number of light paths per camera path</param>
    /// <param name="numConnectCandidates">Sorted list (first is smallest) of candidate connection counts</param>
    /// <param name="perPixelConnect">Number of connections is only optimized if this is true</param>
    /// <param name="perPixelMerge">Number of pixels is only optimized if this is true</param>
    /// <returns>Number of light paths, optional number of connections and whether to use merging</returns>
    public static (int, int?, bool?) OptimizePerImage(Dictionary<Candidate, MonochromeImage> errorImages,
                                                      MonochromeImage referenceImage,
                                                      IEnumerable<float> numLightPathCandidates,
                                                      IEnumerable<int> numConnectCandidates,
                                                      CostHeuristic costHeuristic,
                                                      CountFn mergeProbabilityFn,
                                                      CountFn connectCountFn,
                                                      bool perPixelConnect, bool perPixelMerge,
                                                      string debugOutputFilename = null) {
        int width = errorImages.First().Value.Width;
        int height = errorImages.First().Value.Height;
        MakeBuffers(width * height, numLightPathCandidates, perPixelConnect, perPixelMerge, numConnectCandidates,
            out var relErrors, out var costs);

        // Create buffers to store the per-pixel marginalized values of the global candidates for debugging
        Dictionary<Candidate, MonochromeImage> marginalizedMoments = null;
        Dictionary<Candidate, MonochromeImage> marginalizedCosts = null;
        if (debugOutputFilename != null) {
            marginalizedMoments = new();
            marginalizedCosts = new();
            foreach (var (candidate, _) in relErrors) {
                marginalizedMoments.Add(candidate, new(width, height));
                marginalizedCosts.Add(candidate, new(width, height));
            }
        }


        // For robustness, we reject a small percentage of largest values as outliers. For convenience,
        // we first find these outliers and set a threshold for each candidate. This can be optimized, e.g.,
        // by folding it into the optimization itself so it is done once per pixel and not once per pixel and
        // candidate. (currently costs around 20ms per optimization run)
        Dictionary<Candidate, float> outlierThresholds = new();
        Parallel.ForEach(errorImages, elem => {
            float t = ComputeOutlierThreshold(elem.Value);
            lock(outlierThresholds) outlierThresholds.Add(elem.Key, t);
        });

        // Accumulate relative moments and cost, using a parallel reduction with per-line buffering
        float recipNumPixels = 1.0f / (width * height);
        Parallel.For(0, height, row => {
            // Allocate a buffer for this line (TODO could be made thread-local and re-use the memory)
            MakeBuffers(width * height, numLightPathCandidates, perPixelConnect, perPixelMerge,
                numConnectCandidates, out var lineErrors, out var lineCosts);

            for (int col = 0; col < width; ++col) {
                // Merges are considered enabled if there's at least a 10% probability of them happening
                bool mergeDecision = false;
                if (perPixelMerge) {
                    float mergeProb = mergeProbabilityFn(col, row);
                    mergeDecision = mergeProb > 0.1f;
                }

                // Find closest candidate for the actual number of connections (assumes counts are sorted!)
                int candidateConnect = 0;
                if (perPixelConnect) {
                    float connectCount = connectCountFn(col, row);
                    foreach (int c in numConnectCandidates) {
                        if (c >= connectCount) {
                            candidateConnect = c;
                            break;
                        }
                    }
                }

                float mean = referenceImage.GetPixel(col, row);
                if (mean == 0.0f) continue; // skip completely black pixels
                float recipMeanSquare = 1.0f / (mean * mean + 0.0001f);

                void Accumulate(Candidate c, Candidate global) {
                    var error = errorImages[c].GetPixel(col, row);
                    var cost = costHeuristic.EvaluatePerPixel(c.NumLightPaths, c.NumConnections.Value,
                                                              c.Merge.Value ? 1 : 0, !global.BuildPhotonMap.Value);

                    // Very simple outlier removal. Without this, a single firefly pixel can completely
                    // change the result.
                    if (error > outlierThresholds[c]) { //1e5) {
                        error = 0.0f;
                    }

                    // Accumulate in the per-line storage. We scale by the number of pixels to get resolution
                    // independent values that are easier to interpret when debugging. Has no effect on the
                    // outcome since recipNumPixels is a constant.
                    lineErrors[global] += error * recipMeanSquare * recipNumPixels;
                    lineCosts[global] += cost * recipNumPixels;

                    // If the debugging buffers exist, track the last marginalized per-pixel value in them.
                    // We use "SetPixel", i.e. overwrite instead of accumulate, to not distort the values.
                    // Intermediate states are currently not kept, only the last outcome.
                    marginalizedMoments?[global]?.SetPixel(col, row, error * recipMeanSquare);
                    marginalizedCosts?[global]?.SetPixel(col, row, cost);
                }

                foreach (var (c, moment) in errorImages) {
                    if (c.NumLightPaths == 0) {
                        Accumulate(c, new(0, 0, false, false));
                        continue;
                    }

                    // Filter out candidates that match the per-pixel decision, except the path tracer
                    // if (perPixelMerge && c.Merge != mergeDecision)
                    //     continue;
                    if (perPixelConnect && c.NumConnections != candidateConnect)
                        continue;

                    var g = c;
                    if (perPixelMerge) g.Merge = null;
                    if (perPixelConnect) g.NumConnections = null;

                    if (perPixelMerge) { // 2 cases: PM disabled globally, or using the local decision
                        if (mergeDecision == true && c.Merge == true) {
                            g.BuildPhotonMap = true;
                            Accumulate(c, g); // merges allowed, pm acceleration structure is built
                        } else if (mergeDecision == true && c.Merge == false) {
                            // g.BuildPhotonMap = false;
                            // var localOverride = c; localOverride.Merge = false;
                            // Accumulate(localOverride, g); // merges disabled globally, no acceleration structure built
                        } else if (mergeDecision == false && c.Merge == false) {
                            g.BuildPhotonMap = true;
                            Accumulate(c, g); // merges allowed, pm acceleration structure is built
                            // g.BuildPhotonMap = false;
                            // Accumulate(c, g); // merges disabled globally, no acceleration structure built
                        }
                    } else {
                        g.BuildPhotonMap = c.Merge;
                        Accumulate(c, g);
                    }
                }
            }

            lock (relErrors) {
                foreach (var (c, v) in lineErrors) relErrors[c] += v;
                foreach (var (c, v) in lineCosts) costs[c] += v;
            }
        });

        // Find the best candidate in a simple linear search
        float bestWorkNorm = float.MaxValue;
        Candidate bestCandidate = new();
        foreach (var (candidate, moment) in relErrors) {
            float cost = costs[candidate];
            float workNorm = moment * cost;
            if (workNorm < bestWorkNorm) {
                bestWorkNorm = workNorm;
                bestCandidate = candidate;
            }
        }

        if (debugOutputFilename != null) {
            int num = marginalizedMoments.Count + marginalizedCosts.Count;
            var layers = new (string, ImageBase)[num];
            int i = 0;
            foreach (var (c, img) in marginalizedMoments) {
                layers[i++] = ($"global-{c.ToString()}", img);
                layers[i++] = ($"global-cost-{c.ToString()}", marginalizedCosts[c]);
            }
            Layers.WriteToExr(debugOutputFilename, layers);
        }

        // Keep track of the history of candidate values in the frame buffer
        // TODO this should be done in the integrator
        // if (WriteDebugInfo) {
        //     if (!Scene.FrameBuffer.MetaData.ContainsKey("GlobalCandidateHistory")) {
        //         Scene.FrameBuffer.MetaData["GlobalCandidateHistory"] =
        //             new List<Dictionary<string, Dictionary<string, float>>>();
        //     }
        //     // Copy in a dictionary with a string key so it can be serialized
        //     Dictionary<string, float> relMomentSerialized = new();
        //     Dictionary<string, float> costSerialized = new();
        //     foreach (var (c, m) in relMoments) {
        //         relMomentSerialized[c.ToString()] = m;
        //         costSerialized[c.ToString()] = costs[c];
        //     }
        //     Scene.FrameBuffer.MetaData["GlobalCandidateHistory"].Add(new Dictionary<string, Dictionary<string, float>>() {
        //         {"RelativeMoment", relMomentSerialized},
        //         {"Cost", costSerialized }
        //     });
        // }
        // Scene.FrameBuffer.MetaData["PerImageDecision"] = bestCandidate;

        int numLightPaths = bestCandidate.NumLightPaths;
        int? numConnect = perPixelConnect ? null : bestCandidate.NumConnections;
        bool? merge = perPixelMerge ? (bestCandidate.BuildPhotonMap.Value ? null : false) : bestCandidate.Merge;
        return (numLightPaths, numConnect, merge);
    }
}
