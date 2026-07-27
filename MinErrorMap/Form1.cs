using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MinimumErrorMap
{
    /// <summary>
    /// Simple matrix container with row/column count and a copy method.
    /// Also provides a helper to compute the percentage of ones.
    /// </summary>
    public class Matrix
    {
        public int[,] Data { get; set; }
        public int RowCount { get; private set; }
        public int ColumnCount { get; private set; }

        public Matrix(int rows, int cols)
        {
            RowCount = rows;
            ColumnCount = cols;
            Data = new int[rows, cols];
        }

        /// <summary>
        /// Creates a deep copy of this matrix.
        /// </summary>
        public Matrix Copy()
        {
            Matrix copy = new Matrix(RowCount, ColumnCount);
            for (int i = 0; i < RowCount; i++)
                for (int j = 0; j < ColumnCount; j++)
                    copy.Data[i, j] = Data[i, j];
            return copy;
        }

        /// <summary>
        /// Returns the percentage of cells that are 1.
        /// </summary>
        public double CalculatePercentageOfOnes()
        {
            int onesCount = 0;
            for (int i = 0; i < RowCount; i++)
                for (int j = 0; j < ColumnCount; j++)
                    if (Data[i, j] == 1) onesCount++;
            return (double)onesCount / (RowCount * ColumnCount) * 100.0;
        }
    }

    /// <summary>
    /// Generates test instances for the Minimum Error Map problem.
    /// Creates binary matrices with a specified density of ones and introduces random errors
    /// by flipping bits in a way that prevents trivial correction.
    /// Also applies random column shuffling to simulate an unknown initial ordering.
    /// </summary>
    public class InstanceGenerator
    {
        private readonly Random random = new Random();
        // Keeping track of the positions where errors were added for visualization
        public List<Tuple<int, int>> errorList = new List<Tuple<int, int>>();

        /// <summary>
        /// Generates a base matrix with a specific percentage of ones.
        /// The generation ensures every column has at least one 1
        /// and tries to keep the percentage within 10% of the requested value.
        /// </summary>
        public Matrix GenerateBaseMatrix(int rows, int columns, double percentageOfOnes)
        {
            int totalCells = rows * columns;
            int targetOnes = (int)(totalCells * percentageOfOnes / 100.0);

            int minimum = rows * 2;
            if (targetOnes < minimum) targetOnes = minimum;
            int maximum = rows * (columns - 1);
            if (targetOnes > maximum) targetOnes = maximum;

            int attempts = 0, maxAttempts = 1000;

            while (attempts < maxAttempts)
            {
                attempts++;
                Matrix temp = new Matrix(rows, columns);

                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < columns; j++)
                        temp.Data[i, j] = 0;

                int onesPerRow = targetOnes / rows;
                int remainder = targetOnes % rows;

                for (int i = 0; i < rows; i++)
                {
                    int count = onesPerRow;
                    if (i < remainder) count++;
                    // Each row must have at least 2 ones (to form a block)
                    if (count < 2) count = 2;
                    if (count > columns - 1) count = columns - 1;
                    int start = random.Next(0, columns - count + 1);
                    for (int j = start; j < start + count; j++)
                        temp.Data[i, j] = 1;
                }

                // Ensure every column has at least one 1
                // If a column is empty, try to add a 1 adjacent to an existing 1 in the same row
                bool allColumnsOk = true;
                for (int j = 0; j < columns && allColumnsOk; j++)
                {
                    bool columnHasOne = false;
                    for (int i = 0; i < rows; i++)
                        if (temp.Data[i, j] == 1) { columnHasOne = true; break; }

                    if (!columnHasOne)
                    {
                        bool filled = false;
                        for (int i = 0; i < rows && !filled; i++)
                        {
                            bool left = (j > 0 && temp.Data[i, j - 1] == 1);
                            bool right = (j < columns - 1 && temp.Data[i, j + 1] == 1);
                            if (left || right)
                            {
                                temp.Data[i, j] = 1;
                                filled = true;
                            }
                        }

                        if (!filled)
                        {
                            for (int i = 0; i < rows && !filled; i++)
                            {
                                for (int k = 0; k < columns && !filled; k++)
                                {
                                    if (temp.Data[i, k] == 1)
                                    {
                                        if (k > 0 && temp.Data[i, k - 1] == 0)
                                        {
                                            temp.Data[i, k - 1] = 1;
                                            if (k - 1 == j) filled = true;
                                        }
                                        else if (k < columns - 1 && temp.Data[i, k + 1] == 0)
                                        {
                                            temp.Data[i, k + 1] = 1;
                                            if (k + 1 == j) filled = true;
                                        }
                                    }
                                }
                            }
                        }

                        if (!filled)
                            allColumnsOk = false;
                    }
                }

                // If managed to cover all columns, check the percentage
                if (allColumnsOk)
                {
                    double currentPercentage = temp.CalculatePercentageOfOnes();
                    // Allow some tolerance because the distribution may not be exact
                    if (Math.Abs(currentPercentage - percentageOfOnes) <= 10.0)
                        return temp;
                }
            }

            // Fallback: return an empty matrix (should rarely happen)
            return new Matrix(rows, columns);
        }

        /// <summary>
        /// Adds a specified number of errors (bit flips) to the matrix.
        /// A flip is performed only when it creates a non-trivial error: changing a 1 to 0
        /// requires 1s on both sides, while changing a 0 to 1 requires both neighbors to be 0.
        /// </summary>
        public void AddErrors(Matrix matrix, int errorCount)
        {
            if (errorCount <= 0) return;
            errorList.Clear();
            int addedErrors = 0;
            int totalCells = matrix.RowCount * matrix.ColumnCount;
            int maxAttempts = totalCells * 10;
            int attempts = 0;

            while (addedErrors < errorCount && attempts < maxAttempts)
            {
                attempts++;
                int row = random.Next(matrix.RowCount);
                int col = random.Next(matrix.ColumnCount);
                if (CanAddError(matrix, row, col))
                {
                    // Flip the bit
                    matrix.Data[row, col] = matrix.Data[row, col] == 1 ? 0 : 1;
                    errorList.Add(new Tuple<int, int>(row, col));
                    addedErrors++;
                }
            }
        }

        /// <summary>
        /// Determines whether flipping the bit at (row, col) would create a "meaningful" error.
        /// </summary>
        private bool CanAddError(Matrix matrix, int row, int col)
        {
            int currentValue = matrix.Data[row, col];
            bool leftNeighbor = col > 0 && matrix.Data[row, col - 1] == 1;
            bool rightNeighbor = col < matrix.ColumnCount - 1 && matrix.Data[row, col + 1] == 1;
            // If it's a 1, both neighbors should be 1 so that flipping creates a hole
            // If it's a 0, both neighbors should be 0 so that flipping creates an isolated 1
            if (currentValue == 1) return (leftNeighbor && rightNeighbor);
            else return (!leftNeighbor && !rightNeighbor);
        }

        /// <summary>
        /// Randomly shuffles the columns of the matrix and returns the permutation used.
        /// This simulates the initial unknown order of columns.
        /// </summary>
        public void ShuffleColumns(Matrix matrix, out int[] permutation)
        {
            int columns = matrix.ColumnCount;
            permutation = new int[columns];
            for (int i = 0; i < columns; i++) permutation[i] = i;
            // Fisher-Yates shuffle
            for (int i = columns - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temp = permutation[i]; permutation[i] = permutation[j]; permutation[j] = temp;
            }
            int[,] newMatrix = new int[matrix.RowCount, columns];
            for (int i = 0; i < matrix.RowCount; i++)
                for (int j = 0; j < columns; j++)
                    newMatrix[i, j] = matrix.Data[i, permutation[j]];
            matrix.Data = newMatrix;
        }
    }

    /// <summary>
    /// Main GUI window. Handles instance generation, parameter setup,
    /// and running the Tabu Search metaheuristic in a background thread.
    /// </summary>
    public partial class MainWindow : Form
    {
        private Matrix? currentMatrix;
        private Matrix? optimalSolution; // The matrix before adding errors (for comparison)
        private readonly InstanceGenerator instanceGenerator = new InstanceGenerator();

        // UI components
        private TabControl tabControl = null!;
        private TabPage tabGenerator = null!;
        private TabPage tabParameters = null!;
        private TabPage tabResults = null!;

        private DataGridView dataGridView = null!;
        private NumericUpDown numericRows = null!;
        private NumericUpDown numericColumns = null!;
        private NumericUpDown numericPercentageOfOnes = null!;
        private NumericUpDown numericErrors = null!;
        private Button buttonGenerate = null!;
        private Button buttonEmpty = null!;
        private Button buttonSave = null!;
        private Button buttonLoad = null!;
        private Button buttonShuffle = null!;
        private Button buttonTransfer = null!;
        private Button buttonPause = null!;
        private Button buttonStop = null!;
        private ProgressBar progressBar = null!;
        private Label labelStatus = null!;

        private bool isMatrixShuffled = false;

        // Algorithm parameters
        private NumericUpDown numericTabuLength = null!;
        private NumericUpDown numericMaxIterations = null!;
        private NumericUpDown numericStagnationLimit = null!;
        private NumericUpDown numericNeighborhoodSize = null!;

        // Results display
        private Label labelObjectiveValue = null!;
        private Label labelSimilarity = null!;
        private Label labelDistance = null!;
        private Label labelPermutation = null!;
        private Label labelTime = null!;
        private Label labelPreHeuristicCost = null!;
        private Label labelPostHeuristicCost = null!;
        private Label labelRefinedCost = null!;
        private DataGridView resultDataGridView = null!;
        private PictureBox chartPictureBox = null!;

        // Thread control flags
        private volatile bool isPaused = false, isStopped = false;

        // Helper objects
        private MatrixDisplayHelper matrixDisplayHelper = null!;
        private ChartDrawer chartDrawer = null!;
        private AlgorithmRunner algorithmRunner = null!;

        public MainWindow()
        {
            InitializeComponents();
            UpdatePercentageRanges();
            matrixDisplayHelper = new MatrixDisplayHelper(dataGridView);
            chartDrawer = new ChartDrawer(chartPictureBox);
            algorithmRunner = new AlgorithmRunner(this);
        }

        /// <summary>
        /// Builds the form's controls and layout
        /// </summary>
        private void InitializeComponents()
        {
            this.WindowState = FormWindowState.Maximized;
            this.Text = "Minimum Error Map Instance Generator";

            tabControl = new TabControl { Dock = DockStyle.Fill };

            // ---- Generator tab ----
            tabGenerator = new TabPage("Generator");
            Panel leftPanel = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(10) };
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            int y = 20, wh = 25, gap = 10;
            // Row count
            new Label { Text = "Number of rows:", Location = new Point(10, y), Size = new Size(200, wh), Parent = leftPanel };
            y += wh;
            numericRows = new NumericUpDown { Location = new Point(10, y), Size = new Size(200, wh), Minimum = 2, Maximum = 100, Value = 10, Parent = leftPanel };
            numericRows.ValueChanged += UpdateRangesOnValueChanged;
            y += wh + gap;

            // Column count
            new Label { Text = "Number of columns:", Location = new Point(10, y), Size = new Size(200, wh), Parent = leftPanel };
            y += wh;
            numericColumns = new NumericUpDown { Location = new Point(10, y), Size = new Size(200, wh), Minimum = 2, Maximum = 100, Value = 10, Parent = leftPanel };
            numericColumns.ValueChanged += UpdateRangesOnValueChanged;
            y += wh + gap;

            // Percentage of ones
            new Label { Text = "Percentage of ones (%):", Location = new Point(10, y), Size = new Size(200, wh), Parent = leftPanel };
            y += wh;
            numericPercentageOfOnes = new NumericUpDown { Location = new Point(10, y), Size = new Size(200, wh), Minimum = 10, Maximum = 90, Value = 40, Parent = leftPanel };
            y += wh + gap;

            // Number of errors
            new Label { Text = "Number of errors:", Location = new Point(10, y), Size = new Size(200, wh), Parent = leftPanel };
            y += wh;
            numericErrors = new NumericUpDown { Location = new Point(10, y), Size = new Size(200, wh), Minimum = 0, Maximum = 100, Value = 0, Parent = leftPanel };
            y += wh + gap;

            // Buttons
            buttonGenerate = new Button { Text = "Generate", Location = new Point(10, y), Size = new Size(95, 35), Parent = leftPanel };
            buttonGenerate.Click += ButtonGenerate_Click;
            buttonEmpty = new Button { Text = "Empty", Location = new Point(115, y), Size = new Size(95, 35), Parent = leftPanel };
            buttonEmpty.Click += ButtonEmpty_Click;
            y += 35 + gap;

            buttonSave = new Button { Text = "Save to file", Location = new Point(10, y), Size = new Size(200, 35), Parent = leftPanel };
            buttonSave.Click += ButtonSave_Click;
            y += 35 + gap;

            buttonLoad = new Button { Text = "Load from file", Location = new Point(10, y), Size = new Size(200, 35), Parent = leftPanel };
            buttonLoad.Click += ButtonLoad_Click;
            y += 35 + gap;

            buttonShuffle = new Button { Text = "Shuffle columns", Location = new Point(10, y), Size = new Size(200, 35), BackColor = Color.LightSteelBlue, Parent = leftPanel };
            buttonShuffle.Click += ButtonShuffle_Click;
            y += 35 + gap;

            buttonTransfer = new Button { Text = "Transfer to metaheuristic", Location = new Point(10, y), Size = new Size(200, 35), BackColor = Color.LightGreen, Parent = leftPanel };
            buttonTransfer.Click += ButtonTransfer_Click;
            y += 35 + gap;

            buttonPause = new Button { Text = "Pause", Location = new Point(10, y), Size = new Size(95, 35), BackColor = Color.LightYellow, Enabled = false, Parent = leftPanel };
            buttonPause.Click += ButtonPause_Click;
            buttonStop = new Button { Text = "Stop", Location = new Point(115, y), Size = new Size(95, 35), BackColor = Color.LightCoral, Enabled = false, Parent = leftPanel };
            buttonStop.Click += ButtonStop_Click;
            y += 35 + gap;

            progressBar = new ProgressBar { Location = new Point(10, y), Size = new Size(200, 25), Parent = leftPanel };
            y += wh + gap;
            labelStatus = new Label { Text = "", Location = new Point(10, y), Size = new Size(200, wh), Parent = leftPanel };

            // Data grid to display the matrix
            dataGridView = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = false };
            dataGridView.CellValidating += DataGridView_CellValidating;
            dataGridView.CellValueChanged += DataGridView_CellValueChanged;
            rightPanel.Controls.Add(dataGridView);

            tabGenerator.Controls.Add(rightPanel);
            tabGenerator.Controls.Add(leftPanel);

            // ---- Parameters tab ----
            tabParameters = new TabPage("Metaheuristic Parameters");
            Panel parametersPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            int yPar = 20;
            Label header = new Label { Text = "Tabu Search algorithm settings", Location = new Point(10, yPar), Size = new Size(400, 30), Font = new Font("Arial", 10, FontStyle.Bold) };
            parametersPanel.Controls.Add(header);
            yPar += 40;

            Label labelTabuLength = new Label { Text = "Tabu list length:", Location = new Point(10, yPar), Size = new Size(200, 25) };
            numericTabuLength = new NumericUpDown { Location = new Point(220, yPar), Size = new Size(80, 25), Minimum = 1, Maximum = 200, Value = 10 };
            parametersPanel.Controls.Add(labelTabuLength); parametersPanel.Controls.Add(numericTabuLength);
            yPar += 35;

            Label labelMaxIter = new Label { Text = "Max iterations:", Location = new Point(10, yPar), Size = new Size(200, 25) };
            numericMaxIterations = new NumericUpDown { Location = new Point(220, yPar), Size = new Size(80, 25), Minimum = 10, Maximum = 2000, Value = 500 };
            parametersPanel.Controls.Add(labelMaxIter); parametersPanel.Controls.Add(numericMaxIterations);
            yPar += 35;

            Label labelStagnation = new Label { Text = "Iterations without improvement:", Location = new Point(10, yPar), Size = new Size(200, 25) };
            numericStagnationLimit = new NumericUpDown { Location = new Point(220, yPar), Size = new Size(80, 25), Minimum = 10, Maximum = 500, Value = 50 };
            parametersPanel.Controls.Add(labelStagnation); parametersPanel.Controls.Add(numericStagnationLimit);
            yPar += 35;

            Label labelNeighborhood = new Label { Text = "Neighborhood size:", Location = new Point(10, yPar), Size = new Size(200, 25) };
            numericNeighborhoodSize = new NumericUpDown { Location = new Point(220, yPar), Size = new Size(80, 25), Minimum = 1, Maximum = 900, Value = 20 };
            parametersPanel.Controls.Add(labelNeighborhood); parametersPanel.Controls.Add(numericNeighborhoodSize);
            tabParameters.Controls.Add(parametersPanel);

            // ---- Results tab ----
            tabResults = new TabPage("Tabu Search Results");
            Panel resultsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            labelObjectiveValue = new Label { Text = "Objective function value (final cost): --", Location = new Point(10, 10), Size = new Size(600, 25), Font = new Font("Arial", 10, FontStyle.Bold) };
            labelSimilarity = new Label { Text = "Similarity to error-free matrix: --%", Location = new Point(10, 35), Size = new Size(600, 25), Font = new Font("Arial", 10) };
            labelDistance = new Label { Text = "Distance from optimum: --", Location = new Point(10, 60), Size = new Size(600, 25), Font = new Font("Arial", 10) };

            labelPermutation = new Label { Text = "Permutation: --", Location = new Point(10, 85), AutoSize = false, Size = new Size(900, 50), Font = new Font("Arial", 9) };

            labelTime = new Label { Text = "Time: --", Location = new Point(10, 140), Size = new Size(300, 25), Font = new Font("Arial", 10) };

            int rightColumnX = 620;
            labelPreHeuristicCost = new Label { Text = "Cost before heuristic: --", Location = new Point(rightColumnX, 10), Size = new Size(400, 20), Font = new Font("Arial", 9) };
            labelPostHeuristicCost = new Label { Text = "Cost after barycentric heuristic: --", Location = new Point(rightColumnX, 35), Size = new Size(400, 20), Font = new Font("Arial", 9) };
            labelRefinedCost = new Label { Text = "Cost after greedy heuristic: --", Location = new Point(rightColumnX, 60), Size = new Size(400, 20), Font = new Font("Arial", 9) };

            resultDataGridView = new DataGridView { Location = new Point(10, 170), Size = new Size(900, 220), AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true };

            chartPictureBox = new PictureBox { Location = new Point(10, 410), Size = new Size(900, 220), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

            resultsPanel.Controls.Add(labelObjectiveValue);
            resultsPanel.Controls.Add(labelSimilarity);
            resultsPanel.Controls.Add(labelDistance);
            resultsPanel.Controls.Add(labelPermutation);
            resultsPanel.Controls.Add(labelTime);
            resultsPanel.Controls.Add(labelPreHeuristicCost);
            resultsPanel.Controls.Add(labelPostHeuristicCost);
            resultsPanel.Controls.Add(labelRefinedCost);
            resultsPanel.Controls.Add(resultDataGridView);
            resultsPanel.Controls.Add(chartPictureBox);
            tabResults.Controls.Add(resultsPanel);

            tabControl.TabPages.Add(tabGenerator);
            tabControl.TabPages.Add(tabParameters);
            tabControl.TabPages.Add(tabResults);
            this.Controls.Add(tabControl);
        }

        /// <summary>
        /// Dynamically updates the allowed ranges for percentage and errors based on dimensions.
        /// </summary>
        private void UpdatePercentageRanges()
        {
            if (numericPercentageOfOnes == null || numericErrors == null) return;
            int rows = (int)numericRows.Value, cols = (int)numericColumns.Value;
            int totalCells = rows * cols;
            int minOnes = rows * 2;
            double minPercent = (double)minOnes / totalCells * 100.0;
            int maxOnes = rows * (cols - 1);
            double maxPercent = (double)maxOnes / totalCells * 100.0;
            int minP = (int)Math.Ceiling(minPercent / 10.0) * 10;
            int maxP = (int)Math.Floor(maxPercent / 10.0) * 10;
            if (minP < 10) minP = 10;
            if (maxP > 90) maxP = 90;

            // Ensure valid range
            if (minP > maxP)
            {
                // Fallback: set a safe range
                minP = 10;
                maxP = 90;
            }

            numericPercentageOfOnes.Minimum = minP;
            numericPercentageOfOnes.Maximum = maxP;

            // Safely adjust current value
            decimal currentVal = numericPercentageOfOnes.Value;
            if (currentVal < minP) numericPercentageOfOnes.Value = minP;
            if (currentVal > maxP) numericPercentageOfOnes.Value = maxP;

            numericErrors.Maximum = Math.Min(100, totalCells / 5);
        }

        private void UpdateRangesOnValueChanged(object? sender, EventArgs e) => UpdatePercentageRanges();

        /// <summary>
        /// Generates a new instance with the current parameters and displays it.
        /// </summary>
        private void ButtonGenerate_Click(object? sender, EventArgs e)
        {
            try
            {
                int rows = (int)numericRows.Value, cols = (int)numericColumns.Value;
                currentMatrix = instanceGenerator.GenerateBaseMatrix(rows, cols, (double)numericPercentageOfOnes.Value);
                optimalSolution = currentMatrix.Copy(); // Save the error‑free version.
                instanceGenerator.AddErrors(currentMatrix, (int)numericErrors.Value);
                isMatrixShuffled = false;
                matrixDisplayHelper.DisplayMatrix(currentMatrix, instanceGenerator.errorList);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating matrix: {ex.Message}", "Generation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Creates an empty matrix (all zeros) for manual editing.
        /// </summary>
        private void ButtonEmpty_Click(object? sender, EventArgs e)
        {
            try
            {
                int rows = (int)numericRows.Value;
                int cols = (int)numericColumns.Value;
                currentMatrix = new Matrix(rows, cols);
                optimalSolution = null;
                instanceGenerator.errorList.Clear();
                isMatrixShuffled = false;
                matrixDisplayHelper.DisplayMatrix(currentMatrix, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating empty matrix: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Shuffles the columns of the current matrix (and clears the error list).
        /// The user must do this before running the metaheuristic.
        /// </summary>
        private void ButtonShuffle_Click(object? sender, EventArgs e)
        {
            if (currentMatrix == null)
            {
                MessageBox.Show("First generate or load a matrix.", "No matrix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                instanceGenerator.ShuffleColumns(currentMatrix, out _);
                instanceGenerator.errorList.Clear();
                isMatrixShuffled = true;
                matrixDisplayHelper.DisplayMatrix(currentMatrix, null);
                MessageBox.Show("Columns have been shuffled.", "Shuffle complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error shuffling columns: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Starts the Tabu Search in a background thread.
        /// The matrix must be shuffled first.
        /// </summary>
        private void ButtonTransfer_Click(object? sender, EventArgs e)
        {
            if (currentMatrix == null) { MessageBox.Show("No matrix."); return; }
            if (!isMatrixShuffled)
            {
                MessageBox.Show("Before transferring to metaheuristic, the columns must be shuffled.\nUse the 'Shuffle columns' button.", "Matrix not shuffled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int maxIter = (int)numericMaxIterations.Value;
            int tabuLen = (int)numericTabuLength.Value;
            int stagnation = (int)numericStagnationLimit.Value;
            int neighborhood = (int)numericNeighborhoodSize.Value;
            int errorCount = (int)numericErrors.Value;

            // Disable controls during execution
            buttonTransfer.Enabled = false;
            buttonPause.Enabled = true;
            buttonStop.Enabled = true;
            labelStatus.Text = "Starting...";
            progressBar.Value = 0;

            algorithmRunner.RunAlgorithm(currentMatrix.Data, maxIter, tabuLen, stagnation, neighborhood, errorCount,
                onProgress: (percent, cost) =>
                {
                    labelStatus.Text = percent + "%";
                    progressBar.Value = percent;
                    if (cost.HasValue) labelStatus.Text += "  Cost: " + cost.Value;
                },
                onComplete: (ts, time, errors) =>
                {
                    int[] bestPermutation = ts.GetBestPermutation();
                    int[,] resultMatrix = ts.ApplyPermutation(bestPermutation);
                    int preCost = ts.GetPreHeuristicCost();
                    int baryCost = ts.GetPostHeuristicCost();
                    int greedyCost = ts.GetRefinedCost();
                    DisplayResults(resultMatrix, ts.GetBestCost(), ts.CostHistory,
                                   bestPermutation, time, errors, preCost, baryCost, greedyCost);

                    labelStatus.Text = "Finished!";
                    progressBar.Value = 0;
                    buttonTransfer.Enabled = true;
                    buttonPause.Enabled = false;
                    buttonStop.Enabled = false;
                    buttonPause.Text = "Pause";
                    buttonPause.BackColor = Color.LightYellow;
                    isPaused = false; isStopped = false;
                    MessageBox.Show("Tabu Search metaheuristic has finished.");
                },
                getPauseFlag: () => isPaused,
                getStopFlag: () => isStopped,
                setStopFlag: (val) => { isStopped = val; }
            );
        }

        /// <summary>
        /// Fills the results tab with the final solution, statistics, and the cost chart.
        /// </summary>
        private void DisplayResults(int[,] resultMatrix, int bestCost, List<int> costHistory,
                                    int[] permutation, double time, int errorCount,
                                    int preHeuristicCost = 0, int postHeuristicCost = 0, int refinedCost = 0)
        {
            tabControl.SelectedTab = tabResults;
            int rows = resultMatrix.GetLength(0), cols = resultMatrix.GetLength(1);

            labelObjectiveValue.Text = "Objective function value (final cost): " + bestCost;

            // Distance from optimum (the number of errors we added)
            int distance = bestCost - errorCount;
            labelDistance.Text = $"Distance from optimum: {distance} (optimum = {errorCount})";

            // Compare to the error‑free matrix if we have it
            if (optimalSolution != null)
            {
                int same = 0;
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++)
                        if (optimalSolution.Data[i, j] == resultMatrix[i, j]) same++;
                double similarity = (double)same / (rows * cols) * 100.0;
                labelSimilarity.Text = "Similarity: " + similarity.ToString("F1") + "%";
            }
            else labelSimilarity.Text = "Similarity: no data";

            // Show the permutation
            if (permutation != null)
                labelPermutation.Text = "Permutation: [" + string.Join(", ", permutation) + "]";
            else
                labelPermutation.Text = "Permutation: --";

            labelTime.Text = "Time: " + time.ToString("F2") + " s";

            // Display the costs at different stages
            labelPreHeuristicCost.Text = $"Cost before heuristic: {preHeuristicCost}";
            labelPostHeuristicCost.Text = $"Cost after barycentric heuristic: {postHeuristicCost}";
            labelRefinedCost.Text = $"Cost after greedy heuristic: {refinedCost}";

            // Show the result matrix
            resultDataGridView.Columns.Clear(); resultDataGridView.Rows.Clear();
            resultDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            for (int j = 0; j < cols; j++)
            {
                DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn { Name = "C" + j, HeaderText = "C" + j, Width = 50, MinimumWidth = 50 };
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                resultDataGridView.Columns.Add(col);
            }
            for (int i = 0; i < rows; i++)
            {
                object[] row = new object[cols];
                for (int j = 0; j < cols; j++) row[j] = resultMatrix[i, j];
                resultDataGridView.Rows.Add(row);
            }

            // Draw the convergence chart
            chartDrawer.DrawChart(costHistory);
        }

        /// <summary>
        /// Displays the given matrix in the data grid view, highlighting any errors.
        /// </summary>
        private void DisplayMatrix(Matrix matrix)
        {
            matrixDisplayHelper.DisplayMatrix(matrix, instanceGenerator.errorList);
        }

        /// <summary>
        /// Validates that the user enters only 0 or 1 in the matrix cells.
        /// </summary>
        private void DataGridView_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.FormattedValue == null) return;
            string val = e.FormattedValue.ToString()?.Trim() ?? string.Empty;
            if (val != "0" && val != "1")
            {
                e.Cancel = true;
                if (currentMatrix != null && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                    dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = currentMatrix.Data[e.RowIndex, e.ColumnIndex];
            }
        }

        /// <summary>
        /// Updates the underlying matrix when the user edits a cell.
        /// </summary>
        private void DataGridView_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && currentMatrix != null)
            {
                var cell = dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell.Value != null) currentMatrix.Data[e.RowIndex, e.ColumnIndex] = Convert.ToInt32(cell.Value);
            }
        }

        /// <summary>
        /// Saves the current matrix to a text file.
        /// </summary>
        private void ButtonSave_Click(object? sender, EventArgs e)
        {
            if (currentMatrix == null) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "Text|*.txt" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string content = currentMatrix.RowCount + " " + currentMatrix.ColumnCount + "\n";
                    for (int i = 0; i < currentMatrix.RowCount; i++)
                    {
                        for (int j = 0; j < currentMatrix.ColumnCount; j++)
                            content += currentMatrix.Data[i, j] + (j < currentMatrix.ColumnCount - 1 ? " " : "");
                        content += "\n";
                    }
                    File.WriteAllText(dialog.FileName, content);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Loads a matrix from a text file (format: first line "rows cols", then rows lines with columns numbers).
        /// </summary>
        private void ButtonLoad_Click(object? sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Text|*.txt" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string[] lines = File.ReadAllLines(dialog.FileName);
                    if (lines.Length < 2)
                        throw new FormatException("File must contain at least two lines.");
                    string[] dims = lines[0].Split(' ');
                    if (dims.Length != 2)
                        throw new FormatException("First line must contain row and column count separated by space.");
                    int rows = int.Parse(dims[0]);
                    int cols = int.Parse(dims[1]);
                    Matrix matrix = new Matrix(rows, cols);
                    for (int i = 0; i < matrix.RowCount; i++)
                    {
                        if (i + 1 >= lines.Length)
                            throw new FormatException($"Expected {rows} data rows, but only {lines.Length - 1} found.");
                        string[] values = lines[i + 1].Split(' ');
                        if (values.Length < cols)
                            throw new FormatException($"Row {i} has insufficient columns.");
                        for (int j = 0; j < matrix.ColumnCount; j++)
                            matrix.Data[i, j] = int.Parse(values[j]);
                    }
                    currentMatrix = matrix;
                    instanceGenerator.errorList.Clear();
                    isMatrixShuffled = false;
                    matrixDisplayHelper.DisplayMatrix(currentMatrix, null);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Pauses or resumes of the background algorithm.
        /// </summary>
        private void ButtonPause_Click(object? sender, EventArgs e)
        {
            if (buttonPause.Text == "Pause")
            { isPaused = true; buttonPause.Text = "Resume"; buttonPause.BackColor = Color.Green; }
            else
            { isPaused = false; buttonPause.Text = "Pause"; buttonPause.BackColor = Color.LightYellow; }
        }

        /// <summary>
        /// Signals the background thread to stop.
        /// </summary>
        private void ButtonStop_Click(object? sender, EventArgs e) { isStopped = true; }

        // Helper method for algorithm runner to access UI controls
        internal void UpdateProgress(int percent, int? cost)
        {
            labelStatus.Text = percent + "%";
            progressBar.Value = percent;
            if (cost.HasValue) labelStatus.Text += "  Cost: " + cost.Value;
        }

        internal void SetFinishedState()
        {
            labelStatus.Text = "Finished!";
            progressBar.Value = 0;
            buttonTransfer.Enabled = true;
            buttonPause.Enabled = false;
            buttonStop.Enabled = false;
            buttonPause.Text = "Pause";
            buttonPause.BackColor = Color.LightYellow;
            isPaused = false; isStopped = false;
        }
    }

    #region Helper Classes

    /// <summary>
    /// Helper class responsible for displaying matrices in a DataGridView.
    /// </summary>
    public class MatrixDisplayHelper
    {
        private readonly DataGridView grid;

        public MatrixDisplayHelper(DataGridView dataGridView)
        {
            grid = dataGridView;
        }

        public void DisplayMatrix(Matrix matrix, List<Tuple<int, int>>? errorList)
        {
            grid.Columns.Clear(); grid.Rows.Clear();
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            for (int j = 0; j < matrix.ColumnCount; j++)
            {
                DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn { Name = "C" + j, HeaderText = "C" + j, Width = 50, MinimumWidth = 50 };
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns.Add(col);
            }
            for (int i = 0; i < matrix.RowCount; i++)
            {
                object[] row = new object[matrix.ColumnCount];
                for (int j = 0; j < matrix.ColumnCount; j++) row[j] = matrix.Data[i, j];
                grid.Rows.Add(row);
                grid.Rows[i].HeaderCell.Value = "R" + i;
            }
            // Highlight error cells
            if (errorList != null)
            {
                foreach (var error in errorList)
                    if (error.Item1 < grid.Rows.Count && error.Item2 < grid.Columns.Count)
                        grid.Rows[error.Item1].Cells[error.Item2].Style.BackColor = Color.LightCoral;
            }
        }
    }

    /// <summary>
    /// Helper class responsible for drawing cost convergence charts.
    /// </summary>
    public class ChartDrawer
    {
        private readonly PictureBox chartBox;

        public ChartDrawer(PictureBox pictureBox)
        {
            chartBox = pictureBox;
        }

        public void DrawChart(List<int> costs)
        {
            if (chartBox == null || costs == null || costs.Count == 0) return;

            Bitmap bmp = new Bitmap(chartBox.Width, chartBox.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                int marginLeft = 65, marginRight = 120, marginTop = 30, marginBottom = 45;
                int chartWidth = bmp.Width - marginLeft - marginRight;
                int chartHeight = bmp.Height - marginTop - marginBottom;

                int minCost = costs.Min();
                int maxCost = costs.Max();
                if (maxCost == minCost) maxCost = minCost + 1;

                // Draw axes and grid
                using (Font axisFont = new Font("Arial", 8))
                using (Pen axisPen = new Pen(Color.Gray, 1))
                using (Pen gridPen = new Pen(Color.FromArgb(230, 230, 230), 1))
                {
                    // Y axis labels and grid
                    int ySteps = 4;
                    for (int i = 0; i <= ySteps; i++)
                    {
                        float value = minCost + (float)i / ySteps * (maxCost - minCost);
                        float yPos = marginTop + chartHeight - ((value - minCost) / (maxCost - minCost) * chartHeight);
                        if (i > 0 && i < ySteps) g.DrawLine(gridPen, marginLeft, yPos, marginLeft + chartWidth, yPos);
                        string label = ((int)value).ToString();
                        SizeF labelSize = g.MeasureString(label, axisFont);
                        g.DrawString(label, axisFont, Brushes.Black, marginLeft - labelSize.Width - 7, yPos - labelSize.Height / 2);
                        g.DrawLine(axisPen, marginLeft - 4, yPos, marginLeft, yPos);
                    }

                    // X axis labels and grid
                    int xSteps = 4;
                    for (int i = 0; i <= xSteps; i++)
                    {
                        int iteration = (int)((float)i / xSteps * (costs.Count - 1));
                        float xPos = marginLeft + (float)iteration / (costs.Count - 1) * chartWidth;
                        if (i > 0 && i < xSteps) g.DrawLine(gridPen, xPos, marginTop, xPos, marginTop + chartHeight);
                        string label = iteration.ToString();
                        SizeF labelSize = g.MeasureString(label, axisFont);
                        g.DrawString(label, axisFont, Brushes.Black, xPos - labelSize.Width / 2, marginTop + chartHeight + 5);
                        g.DrawLine(axisPen, xPos, marginTop + chartHeight, xPos, marginTop + chartHeight + 4);
                    }
                    g.DrawLine(axisPen, marginLeft, marginTop, marginLeft, marginTop + chartHeight);
                    g.DrawLine(axisPen, marginLeft, marginTop + chartHeight, marginLeft + chartWidth, marginTop + chartHeight);
                }

                // Draw the cost line
                using (Pen linePen = new Pen(Color.Blue, 2))
                {
                    for (int i = 0; i < costs.Count - 1; i++)
                    {
                        float x1 = marginLeft + (float)i / (costs.Count - 1) * chartWidth;
                        float y1 = marginTop + chartHeight - ((float)(costs[i] - minCost) / (maxCost - minCost) * chartHeight);
                        float x2 = marginLeft + (float)(i + 1) / (costs.Count - 1) * chartWidth;
                        float y2 = marginTop + chartHeight - ((float)(costs[i + 1] - minCost) / (maxCost - minCost) * chartHeight);
                        g.DrawLine(linePen, x1, y1, x2, y2);
                    }
                }

                // Add titles and legend
                using (Font titleFont = new Font("Arial", 8, FontStyle.Bold))
                using (Font legendFont = new Font("Arial", 8))
                {
                    g.DrawString("Iteration", titleFont, Brushes.Black, marginLeft + chartWidth / 2 - 25, marginTop + chartHeight + 23);
                    // Rotated Y-axis label
                    System.Drawing.Drawing2D.GraphicsState state = g.Save();
                    g.TranslateTransform(15, marginTop + chartHeight / 2);
                    g.RotateTransform(-90);
                    g.DrawString("Objective function value", titleFont, Brushes.Black, -g.MeasureString("Objective function value", titleFont).Width / 2, -10);
                    g.Restore(state);

                    // Simple legend
                    int legX = marginLeft + chartWidth + 15;
                    int legY = marginTop + 10;
                    int legWidth = 100;
                    int legHeight = 25;
                    g.FillRectangle(Brushes.White, legX, legY, legWidth, legHeight);
                    g.DrawRectangle(Pens.LightGray, legX, legY, legWidth, legHeight);
                    using (Pen legPen = new Pen(Color.Blue, 2))
                        g.DrawLine(legPen, legX + 8, legY + 15, legX + 28, legY + 15);
                    g.DrawString("Cost", legendFont, Brushes.Black, legX + 33, legY + 8);
                }
            }
            chartBox.Image = bmp;
        }
    }

    /// <summary>
    /// Helper class that encapsulates the background execution of the Tabu Search algorithm.
    /// </summary>
    public class AlgorithmRunner
    {
        private readonly MainWindow owner;

        public AlgorithmRunner(MainWindow mainWindow)
        {
            owner = mainWindow;
        }

        public void RunAlgorithm(int[,] matrixData, int maxIter, int tabuLen, int stagnation,
                                 int neighborhood, int errorCount,
                                 Action<int, int?> onProgress,
                                 Action<TabuSearch, double, int> onComplete,
                                 Func<bool> getPauseFlag,
                                 Func<bool> getStopFlag,
                                 Action<bool> setStopFlag)
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.WorkerSupportsCancellation = true;

            bw.DoWork += (sender, args) =>
            {
                BackgroundWorker b = (BackgroundWorker)sender!;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                TabuSearch ts = new TabuSearch(matrixData, maxIter, tabuLen, stagnation, neighborhood);

                for (int iter = 1; iter <= maxIter; iter++)
                {
                    if (getStopFlag()) { setStopFlag(false); break; }
                    while (getPauseFlag()) { Thread.Sleep(100); if (getStopFlag()) break; }
                    if (getStopFlag()) break;
                    ts.PerformStep();
                    int percent = (int)(iter * 100.0 / maxIter);
                    b.ReportProgress(percent, ts.GetCurrentCost());
                }
                stopwatch.Stop();
                args.Result = new Tuple<TabuSearch, double, int>(ts, stopwatch.Elapsed.TotalSeconds, errorCount);
            };

            bw.ProgressChanged += (o, args) =>
            {
                int percent = args.ProgressPercentage;
                int? cost = args.UserState as int?;
                onProgress(percent, cost);
            };

            bw.RunWorkerCompleted += (o, args) =>
            {
                if (args.Result is Tuple<TabuSearch, double, int> result)
                {
                    onComplete(result.Item1, result.Item2, result.Item3);
                }
                else
                {
                    owner.SetFinishedState();
                    MessageBox.Show("Algorithm execution was interrupted.");
                }
            };

            bw.RunWorkerAsync();
        }
    }

    #endregion

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }
    }
}