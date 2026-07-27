using System;
using System.Collections.Generic;
using System.Linq;

namespace MinimumErrorMap
{
    /// <summary>
    /// Tabu Search algorithm for the minimum error map problem.
    /// The goal is to find a column permutation that minimizes the total number of "errors"
    /// across all rows, where an error is a 1 that doesn't form a solid block.
    /// </summary>
    public class TabuSearch
    {
        private int[,] inputMatrix;
        private int rowCount;
        private int columnCount;

        private int[] currentPermutation;
        private int currentCost;

        private int[] bestPermutation;
        private int bestCost;

        private int maxIterations;
        private int tabuLength;
        private int stagnationLimit;
        private int neighborhoodSize;

        private int noImprovementCount;

        private Queue<(int colA, int colB)> tabuQueue;
        private int[] rowCostCache;
        private int[] columnFrequency;

        private int[] valueBuffer;
        private int[] prefixBuffer;

        private int preHeuristicCost;
        private int postHeuristicCost;
        private int refinedCost;

        public List<int> CostHistory { get; private set; }
        private Random random = new Random();

        public TabuSearch(int[,] matrix, int maxIterations, int tabuLength, int stagnationLimit, int neighborhoodSize)
        {
            if (matrix == null)
                throw new ArgumentNullException(nameof(matrix), "Input matrix cannot be null.");
            if (matrix.GetLength(0) < 1 || matrix.GetLength(1) < 1)
                throw new ArgumentException("Matrix must have at least one row and one column.", nameof(matrix));
            if (maxIterations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxIterations), "Max iterations must be at least 1.");
            if (tabuLength < 1)
                throw new ArgumentOutOfRangeException(nameof(tabuLength), "Tabu length must be at least 1.");
            if (stagnationLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(stagnationLimit), "Stagnation limit must be at least 1.");
            if (neighborhoodSize < 1)
                throw new ArgumentOutOfRangeException(nameof(neighborhoodSize), "Neighborhood size must be at least 1.");

            this.inputMatrix = matrix;
            this.rowCount = matrix.GetLength(0);
            this.columnCount = matrix.GetLength(1);
            this.maxIterations = maxIterations;
            this.tabuLength = tabuLength;
            this.stagnationLimit = stagnationLimit;
            this.neighborhoodSize = neighborhoodSize;

            valueBuffer = new int[columnCount];
            prefixBuffer = new int[columnCount + 1];
            rowCostCache = new int[rowCount];
            columnFrequency = new int[columnCount];

            tabuQueue = new Queue<(int, int)>();

            // Calculate the cost of the natural column order
            // Used as a baseline for comparison with heuristic solutions
            int[] naturalOrder = Enumerable.Range(0, columnCount).ToArray();
            preHeuristicCost = CalculateCostForPermutation(naturalOrder);

            // Generate an initial solution using the barycentric heuristic,
            // which orders columns according to the average row index of their 1s.
            currentPermutation = GenerateInitialHeuristic();
            UpdateCostCache();

            postHeuristicCost = CalculateCost();

            currentCost = postHeuristicCost;
            RefineStart(10);

            refinedCost = currentCost;

            bestPermutation = (int[])currentPermutation.Clone();
            bestCost = currentCost;

            CostHistory = new List<int> { currentCost };
            noImprovementCount = 0;
        }

        public int GetPreHeuristicCost() => preHeuristicCost;
        public int GetPostHeuristicCost() => postHeuristicCost;
        public int GetRefinedCost() => refinedCost;

        /// <summary>
        /// Recomputes the error cost of each row and updates the cache.
        /// The cached values are used to efficiently evaluate column swaps
        /// without recalculating the full objective function.
        /// </summary>
        private void UpdateCostCache()
        {
            for (int w = 0; w < rowCount; w++)
                rowCostCache[w] = MinimumRowError(w);
        }

        /// <summary>
        /// Generates an initial column ordering using the barycentric heuristic.
        /// Columns are sorted according to the average row positions of their 1s,
        /// providing a reasonable starting solution for further optimization.
        /// </summary>
        private int[] GenerateInitialHeuristic()
        {
            double[] centroids = new double[columnCount];
            for (int j = 0; j < columnCount; j++)
            {
                int sum = 0, count = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    if (inputMatrix[i, j] == 1) { sum += i; count++; }
                }
                centroids[j] = (count > 0) ? (double)sum / count : double.MaxValue;
            }
            int[] indices = Enumerable.Range(0, columnCount).ToArray();
            Array.Sort(centroids, indices);
            return indices;
        }

        /// <summary>
        /// Refines the initial heuristic solution using greedy hill climbing.
        /// Applies improving swaps iteratively to reduce the objective function value.
        /// </summary>
        private void RefineStart(int maxSteps)
        {
            for (int k = 0; k < maxSteps; k++)
            {
                bool improved = false;
                for (int i = 0; i < columnCount - 1 && !improved; i++)
                {
                    for (int j = i + 1; j < columnCount && !improved; j++)
                    {
                        int delta = CalculateCostDelta(i, j);
                        if (delta < 0)
                        {
                            int colA = currentPermutation[i];
                            int colB = currentPermutation[j];
                            Swap(i, j);
                            currentCost += delta;
                            // Only rows where the two swapped columns differ need cache updates
                            for (int w = 0; w < rowCount; w++)
                            {
                                if (inputMatrix[w, colA] != inputMatrix[w, colB])
                                    rowCostCache[w] = MinimumRowError(w);
                            }
                            improved = true;
                        }
                    }
                }
                if (!improved) break;
            }
        }

        /// <summary>
        /// Performs a single iteration of the Tabu Search algorithm.
        /// Returns true if a valid move is applied, or false if no improving move can be found.
        /// </summary>
        public bool PerformStep()
        {
            int bestI = -1, bestJ = -1;
            int bestScore = int.MaxValue;
            int bestDelta = 0;

            // If the search stagnates for a certain number of iterations, activate diversification mode.
            // A frequency based penalty is added to encourage exploration of less visited regions
            bool diversificationPhase = (noImprovementCount >= stagnationLimit / 2);

            var randomPairs = GenerateRandomPairs(neighborhoodSize);

            foreach (var (i, j) in randomPairs)
            {
                int delta = CalculateCostDelta(i, j);
                int newCost = currentCost + delta;

                int penalty = 0;
                if (diversificationPhase)
                {
                    int colI = currentPermutation[i];
                    int colJ = currentPermutation[j];
                    // Penalize moves involving columns that have been swapped often.
                    penalty = (columnFrequency[colI] + columnFrequency[colJ]) / 2;
                }

                int score = newCost + penalty;

                // Apply the aspiration criterion: ignore the tabu restriction when a move
                // produces a solution better than the current global best.
                bool isTabu = IsTabu(i, j);
                if (isTabu && newCost < bestCost)
                    isTabu = false;

                if (!isTabu && score < bestScore)
                {
                    bestScore = score;
                    bestI = i;
                    bestJ = j;
                    bestDelta = delta;
                }
            }

            // No valid move found
            if (bestI == -1) return false;

            int bestColA = currentPermutation[bestI];
            int bestColB = currentPermutation[bestJ];

            Swap(bestI, bestJ);
            currentCost += bestDelta;

            // Update frequency counters for the diversification penalty
            columnFrequency[bestColA]++;
            columnFrequency[bestColB]++;

            // Add the swap to the tabu list
            tabuQueue.Enqueue((bestColA, bestColB));
            if (tabuQueue.Count > tabuLength)
                tabuQueue.Dequeue();

            // Update the row cache for rows where the swapped columns differ
            for (int w = 0; w < rowCount; w++)
            {
                if (inputMatrix[w, bestColA] != inputMatrix[w, bestColB])
                    rowCostCache[w] = MinimumRowError(w);
            }

            // Update the best solution if the current one has a lower cost
            if (currentCost < bestCost)
            {
                bestCost = currentCost;
                bestPermutation = (int[])currentPermutation.Clone();
                noImprovementCount = 0;
            }
            else
                noImprovementCount++;

            // Restart the search after prolonged stagnation by generating a new permutation
            // based on column frequencies, placing less frequently swapped columns first
            if (noImprovementCount >= stagnationLimit)
            {
                PerformRestart();
                noImprovementCount = 0;
            }

            CostHistory.Add(currentCost);
            return true;
        }

        /// <summary>
        /// Generates a random subset of unique column pairs (i, j) with i < j.
        /// //Used to efficiently sample the neighborhood during the search
        /// </summary>
        private List<(int, int)> GenerateRandomPairs(int limit)
        {
            int maxPairs = columnCount * (columnCount - 1) / 2;
            int count = Math.Min(limit, maxPairs);
            var set = new HashSet<(int, int)>();
            int attempts = 0;
            while (set.Count < count && attempts < count * 2)
            {
                int i = random.Next(0, columnCount - 1);
                int j = random.Next(i + 1, columnCount);
                set.Add((i, j));
                attempts++;
            }
            return set.ToList();
        }

        /// <summary>
        /// Calculates the change in total cost resulting from swapping columns at positions i and j.
        /// Uses cached row costs to efficiently compute the cost difference without recalculating
        /// the entire solution.
        /// </summary>
        private int CalculateCostDelta(int i, int j)
        {
            int colA = currentPermutation[i];
            int colB = currentPermutation[j];
            int delta = 0;

            for (int w = 0; w < rowCount; w++)
            {
                // If both bits are the same in this row, the cost doesn't change
                if (inputMatrix[w, colA] == inputMatrix[w, colB])
                    continue;

                int costBefore = rowCostCache[w];
                int costAfter = MinimumRowError(w, i, j);
                delta += (costAfter - costBefore);
            }
            return delta;
        }

        /// <summary>
        /// Generates a new permutation during restart by prioritizing rarely swapped columns.
        /// This diversification step helps the search escape local optima.
        /// </summary>
        private void PerformRestart()
        {
            int[] newPermutation = Enumerable.Range(0, columnCount).OrderBy(idx => columnFrequency[idx]).ToArray();
            currentPermutation = newPermutation;
            currentCost = CalculateCost();
            tabuQueue.Clear();
            UpdateCostCache();
        }

        private int CalculateCost()
        {
            int sum = 0;
            for (int w = 0; w < rowCount; w++)
                sum += MinimumRowError(w);
            return sum;
        }

        /// <summary>
        /// Calculates the full cost of a given permutation without using cached values.
        /// Mainly used for baseline evaluation and calculating the cost of the initial heuristic solution.
        /// </summary>
        private int CalculateCostForPermutation(int[] perm)
        {
            int sum = 0;
            for (int w = 0; w < rowCount; w++)
                sum += MinimumRowErrorForPermutation(w, perm);
            return sum;
        }

        /// <summary>
        /// Computes the minimum number of errors for a single row under a given permutation.
        /// An error occurs when a 1 lies outside the selected contiguous block.
        /// The optimal block minimizes the sum of zeros inside the block and ones outside it.
        /// </summary>
        private int MinimumRowErrorForPermutation(int row, int[] permutation)
        {
            int k = columnCount;
            int[] values = new int[k];
            for (int j = 0; j < k; j++)
                values[j] = inputMatrix[row, permutation[j]];

            int[] prefix = new int[k + 1];
            for (int j = 0; j < k; j++)
                prefix[j + 1] = prefix[j] + values[j];

            int minErrors = k;
            int totalOnes = prefix[k];
            // Try every possible block [a, b] and compute errors
            for (int a = 0; a < k; a++)
            {
                for (int b = a; b < k; b++)
                {
                    int onesInRange = prefix[b + 1] - prefix[a];
                    int length = b - a + 1;
                    int errors = (length - onesInRange) + (totalOnes - onesInRange);
                    if (errors < minErrors) minErrors = errors;
                }
            }
            return minErrors;
        }

        /// <summary>
        /// Calculates the minimum row error for a given permutation or a hypothetical column swap.
        /// Reuses pre-allocated buffers (valueBuffer and prefixBuffer) to efficiently evaluate
        /// candidate moves during the search process.
        /// </summary>
        private int MinimumRowError(int row, int swapPos1 = -1, int swapPos2 = -1)
        {
            int k = columnCount;

            // Build the bit sequence for this row, applying a virtual swap if requested
            for (int j = 0; j < k; j++)
            {
                int virtualIndex = j;
                if (j == swapPos1) virtualIndex = swapPos2;
                else if (j == swapPos2) virtualIndex = swapPos1;

                valueBuffer[j] = inputMatrix[row, currentPermutation[virtualIndex]];
            }

            // Prefix sums for fast range queries
            prefixBuffer[0] = 0;
            for (int j = 0; j < k; j++)
                prefixBuffer[j + 1] = prefixBuffer[j] + valueBuffer[j];

            int minErrors = k;
            int totalOnes = prefixBuffer[k];

            // Exhaustive search for the optimal block
            for (int a = 0; a < k; a++)
            {
                for (int b = a; b < k; b++)
                {
                    int onesInRange = prefixBuffer[b + 1] - prefixBuffer[a];
                    int length = b - a + 1;
                    int errors = (length - onesInRange) + (totalOnes - onesInRange);
                    if (errors < minErrors) minErrors = errors;
                }
            }
            return minErrors;
        }

        /// <summary>
        /// Checks if swapping the columns at positions i and j is currently tabu.
        /// A swap is tabu if the unordered pair of column indices is in the tabu list.
        /// </summary>
        private bool IsTabu(int i, int j)
        {
            int colA = currentPermutation[i];
            int colB = currentPermutation[j];
            return tabuQueue.Contains((colA, colB)) || tabuQueue.Contains((colB, colA));
        }

        private void Swap(int i, int j)
        {
            int temp = currentPermutation[i];
            currentPermutation[i] = currentPermutation[j];
            currentPermutation[j] = temp;
        }

        public int GetCurrentCost() => currentCost;
        public int[] GetBestPermutation() => (int[])bestPermutation.Clone();
        public int GetBestCost() => bestCost;

        public int[,] ApplyPermutation(int[] permutation)
        {
            int[,] result = new int[rowCount, columnCount];
            for (int i = 0; i < rowCount; i++)
                for (int j = 0; j < columnCount; j++)
                    result[i, j] = inputMatrix[i, permutation[j]];
            return result;
        }
    }
}