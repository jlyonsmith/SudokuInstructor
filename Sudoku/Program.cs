using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sudoku
{
    class Program
    {
        private struct Box
        {
            private int row;
            private int col;
            
            public Box(int row, int col)
            {
                this.row = row;
                this.col = col;
            }
            
            public void FromCell(int cellRow, int cellCol)
            {
                row = (cellRow - 1) / 3 + 1;
                col = (cellCol - 1) / 3 + 1;
            }

            public int Top
            {
                get
                {
                    return (row - 1) * 3 + 1;
                }
            }

            public int Bottom
            {
                get
                {
                    return Top + 2;
                }
            }

            public int Left
            {
                get
                {
                    return (col - 1) * 3 + 1;
                }
            }

            public int Right
            {
                get
                {
                    return Left + 2;
                }
            }
        }

        private class Cell : ICloneable
        {
            private uint digitMask;

            public Cell()
            {
                IncludeAllDigits();
            }
            
            protected Cell(uint digitMask)
            {
                this.digitMask = digitMask;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder(9);
                
                for (int i = 1; i <= 9; i++)
                    if ((digitMask & (1 << (i - 1))) != 0)
                        sb.Append(i.ToString());
                    else
                        sb.Append('.');
                        
                return sb.ToString();
            }
            
            public int Digit
            {
                get
                {
                    switch (digitMask)
                    {
                        case 1 << 0:
                            return 1;
                        case 1 << 1:
                            return 2;
                        case 1 << 2:
                            return 3;
                        case 1 << 3:
                            return 4;
                        case 1 << 4:
                            return 5;
                        case 1 << 5:
                            return 6;
                        case 1 << 6:
                            return 7;
                        case 1 << 7:
                            return 8;
                        case 1 << 8:
                            return 9;
                        default:
                            throw new ArgumentException("Cell is not yet solved or is unsolvable");
                    }
                }
                set
                {
                    if (!IsValidDigit(value))
                        throw new ArgumentException("Cell value must be between 1 and 9");

                    digitMask = (uint)(1 << (value - 1));
                }
            }
            
            public bool Solved
            {
                get
                {
                    switch (digitMask)
                    {
                        case 1 << 0:
                        case 1 << 1:
                        case 1 << 2:
                        case 1 << 3:
                        case 1 << 4:
                        case 1 << 5:
                        case 1 << 6:
                        case 1 << 7:
                        case 1 << 8:
                            return true;
                        default:
                            return false;
                    }
                }
            }
            
            public bool Unsolvable
            {
                get
                {
                    return digitMask == 0;
                }
            }

            public int PossibleDigitCount
            {
                get
                {
                    // See http://en.wikipedia.org/wiki/Hamming_weight for a discussion of this algorithm
                    // here converted for use with 32-bit integers.
                    uint x = digitMask;
                    x -= (x >> 1) & 0x55555555; // Put count of each 2 bits into those 2 bits
                    x = (x & 0x33333333) + ((x >> 2) & 0x33333333);  // Put count of each 4 bits into those 4 bits 
                    x = (x + (x >> 4)) & 0X0F0F0F0F; // Put count of each 8 bits into those 8 bits 
                    return (int)(x * 0x01010101) >> 24; // Return left 8 bits of x + (x<<8) + (x<<16) + (x<<24)
                }
            }
            
            public int[] GetPossibleDigits()
            {
                List<int> list = new List<int>(9);
                
                for (int n = 0; n < 10; n++)
                {
                    if ((digitMask & (1 << n)) != 0)
                        list.Add(n + 1);
                }
                
                return list.ToArray();
            }
            
            public void IncludeAllDigits()
            {
                digitMask = 0x1FF; // All possible digits
            }

            public void ExcludeDigit(int digit)
            {
                if (!IsValidDigit(digit))
                    throw new ArgumentException();

                digitMask &= (uint)~(1 << (digit - 1));
            }

            public void ExcludeDigits(Cell cell)
            {
                this.digitMask &= ~(cell.digitMask);
            }

            public static bool IsValidDigit(int digit)
            {
                return (digit >= 1 && digit <= 9);
            }
            
            public bool HasNoPossibleDigits
            {
                get
                {
                    return digitMask == 0;
                }
            }

            public object Clone()
            {
                return new Cell(this.digitMask);
            }
        }

        private class Grid : ICloneable
        {
            public Grid() { }

            public Grid(SharedAsyncState sharedState)
            {
                this.SharedState = sharedState;
            }
            
            private Cell[,] cells = new Cell[9, 9];

            public Cell this[int row, int col]
            {
                get
                {
                    return cells[row - 1, col - 1];
                }
                set
                {
                    cells[row - 1, col - 1] = value; 
                }
            }
            
            private static Cell[,] CloneCells(Cell[,] otherCells)
            {
                Cell[,] cells = new Cell[9,9];

                for (int i = 0; i < 9; i++)
                {
                    for (int j = 0; j < 9; j++)
                    {
                        cells[i, j] = (Cell)otherCells[i, j].Clone();
                    }
                }
                
                return cells;
            }

            public bool ReduceByExclusion()
            {
                bool reduced = false;
                
                // Go through each cell in the 9x9 cells and figure out what cells are excluded
                // by virtual of being in the same row, column or 3x3 box.
                for (int row = 1; row <= 9; row++)
                {
                    for (int col = 1; col <= 9; col++)
                    {
                        Cell rootCell = this[row, col];
                        
                        if (rootCell.Solved)
                        {
                            int digit = rootCell.Digit;
                        
                            // Exclude digits from same row & column
                            for (int i = 1; i <= 9; i++)
                            {
                                if (i != col)
                                {
                                    Cell cell = this[row, i];

                                    if (!cell.Solved)                                       
                                    {
                                        cell.ExcludeDigit(digit);

                                        if (cell.Solved)
                                            reduced = true;
                                    }
                                }
                                
                                if (i != row)
                                {
                                    Cell cell = this[i, col];

                                    if (!cell.Solved)
                                    {
                                        cell.ExcludeDigit(digit);

                                        if (cell.Solved)
                                            reduced = true;
                                    }
                                }
                            }
                            
                            // Exclude digits from same 3x3 box
                            Box box = new Box();
                            
                            box.FromCell(row, col);
                            
                            for (int i = box.Top; i <= box.Bottom; i++)
                            {
                                for (int j = box.Left; j <= box.Right; j++)
                                {
                                    // We already checked cells in the same row and column, so don't check them again
                                    if (!(i == row || j == col))
                                    {
                                        Cell cell = this[i, j];

                                        if (!cell.Solved)
                                        {
                                            cell.ExcludeDigit(digit);

                                            if (cell.Solved)
                                                reduced = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                return reduced;
            }

            public bool ReduceUniquePossibilities()
            {
                bool reduced = false;

                // Go through each row and search for unique possible digits in each cell.  
                for (int row = 1; row <= 9; row++)
                {
                    for (int col = 1; col <= 9; col++)
                    {
                        if (!this[row, col].Solved)
                        {
                            Cell cell = (Cell)this[row, col].Clone();

                            for (int n = 1; n <= 9; n++)
                            {
                                if (n != col)
                                {
                                    cell.ExcludeDigits(this[row, n]);
                                    
                                    // If we have no digits left, then there was nothing
                                    // unique to just this cell.
                                    if (cell.HasNoPossibleDigits)
                                    {
                                        break;
                                    }
                                }
                            }
                            
                            if (cell.Solved)
                            {
                                this[row, col] = cell;
                                reduced = true;
                            }
                        }
                    }
                }
                
                // Go through each column and search for unique possible digits in each cell
                for (int col = 1; col <= 9; col++)
                {
                    for (int row = 1; row <= 9; row++)
                    {
                        Cell cell = (Cell)this[row, col].Clone();

                        if (!cell.Solved)
                        {
                            for (int m = 1; m <= 9; m++)
                            {
                                if (m != row)
                                {
                                    cell.ExcludeDigits(this[m, col]);
                                    
                                    if (cell.HasNoPossibleDigits)
                                    {
                                        break;
                                    }
                                }
                            }
                            
                            if (cell.Solved)
                            {
                                this[row, col] = cell;
                                reduced = true;
                            }
                        }
                    }
                }
                
                // Go through each 3x3 box and search for unique possible digits in each cell
                for (int i = 1; i <= 3; i++)
                {
                    for (int j = 1; j <= 3; j++)
                    {
                        Box box = new Box(i, j);

                        for (int row = box.Top; row <= box.Bottom; row++)
                        {
                            for (int col = box.Left; col <= box.Right; col++)
                            {
                                Cell cell = (Cell)this[row, col].Clone();

                                if (!cell.Solved)
                                {
                                    for (int m = box.Top; m <= box.Bottom; m++)
                                    {
                                        for (int n = box.Left; n <= box.Right; n++)
                                        {
                                            // Don't include the cell we started from!
                                            if (!(m == row && n == col))
                                            {
                                                cell.ExcludeDigits(this[m, n]);
                                                
                                                if (cell.HasNoPossibleDigits)
                                                {
                                                    cell = null;
                                                    break;
                                                }
                                            }
                                        }
                                        
                                        if (cell == null)
                                            break;
                                    }
                                    
                                    if (cell != null && cell.Solved)
                                    {
                                        this[row, col] = cell;
                                        reduced = true;
                                    }
                                }
                            }
                        }
                    }
                }
                
                return reduced;
            }

            public bool Solved
            {
                get
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        for (int j = 1; j <= 9; j++)
                        {
                            if (!this[i, j].Solved)
                                return false;
                        }
                    }
                    
                    return true;
                }
            }

            public bool InvalidState
            {
                get
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        for (int j = 1; j <= 9; j++)
                        {
                            Cell cell = this[i, j];
                            
                            // Check if cell is drained of possibilities
                            if (cell.HasNoPossibleDigits)
                                return true;
                                
                            // Make sure that Sudoku rules are satisfied for solved cells, i.e. the same
                            // number doesn't appear in the same row, column or 3x3 box.
                            if (cell.Solved)
                            {
                                for (int n = 1; n <= 9; n++)
                                {
                                    if (n != j && this[i, n].Solved && cell.Digit == this[i, n].Digit)
                                        return true;
                                    else if (n != i && this[n, j].Solved && cell.Digit == this[n, j].Digit)
                                        return true;
                                }
                                
                                Box box = new Box();

                                box.FromCell(i, j);

                                for (int m = box.Top; m <= box.Bottom; m++)
                                {
                                    for (int n = box.Left; n <= box.Right; n++)
                                    {
                                        // Don't check cells in the same row or column as we already did that above
                                        if (!(m == i || n == j))
                                        {
                                            if (this[m, n].Solved && this[m, n].Digit == cell.Digit)
                                                return true;
                                        }
                                    }
                                } 
                            }
                        }
                    }
                    
                    return false;
                }
            }

            public void ReadFromFile(string fileName)
            {
                using (StreamReader reader = new StreamReader(fileName))
                {
                    for (int row = 1; row <= 9; row++)
                    {
                        string line = reader.ReadLine();

                        line = line.TrimEnd('\r', '\n', '\t', ' ');

                        if (line.Length != 9)
                        {
                            Console.WriteLine("ERROR: Line {0} is not 9 characters long", row + 1);
                            throw new ApplicationException();
                        }

                        for (int col = 1; col <= 9; col++)
                        {
                            char c = line[col - 1];
                            Cell cell = new Cell();

                            if (c >= '1' && c <= '9')
                                cell.Digit = (c - '1' + 1);
                            else if (c != '.')
                                Console.WriteLine("ERROR: Line {0} contains character other than '1' through '9' or '.'");

                            this[row, col] = cell;
                        }
                    }
                }

                // TOOD: Ensure that the cells read is valid, i.e. that it conforms to the SuDoku cells rules
            }

            public void Reduce()
            {
                for (; ; )
                {
                    bool reduced = false;

                    reduced |= ReduceByExclusion();

                    //Print();

                    reduced |= ReduceUniquePossibilities();

                    //Print();

                    if (!reduced)
                        break;
                }
            }
            
            public bool GuessAndReduce()
            {
                //Console.WriteLine("Guessing and reducing:");
            
                Cell[,] savedCells = Grid.CloneCells(cells);

                for (int i = 1; i <= 9; i++)
                {
                    for (int j = 1; j <= 9; j++)
                    {
                        Cell cell = this[i, j];
                        
                        if (cell.PossibleDigitCount == 2)
                        {
                            int[] digits = cell.GetPossibleDigits();
                            
                            for (int n = 0; n < 2; n++)
                            {
                                this[i, j].ExcludeDigit(digits[n]);
                                
                                Reduce();

                                //Console.WriteLine("Result after guessing {0} from [{0}, {1}]:", digits[n], i, j);
                                //PrintState();
                                
                                if (!InvalidState)
                                {
                                    if (Solved || GuessAndReduce())
                                        return true;
                                }
                                    
                                cells = savedCells;
                            }                            
                        }
                    }
                }
                
                return false;
            }

            public delegate bool SolveDelegate();

            public class SharedAsyncState
            {
                private int total;
                private int active;
                private EventWaitHandle waitHandle;
                
                public SharedAsyncState()
                {
                    active = 0;
                    total = 0;
                    waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                }

                public int Total { get { return Interlocked.Exchange(ref total, total); } }
                public int Active { get { return Interlocked.Exchange(ref active, active); } }
                public EventWaitHandle CompleteEvent { get { return waitHandle; } }

                public void IncrementTotal()
                {
                    Interlocked.Increment(ref total);
                }

                public void IncrementActive()
                {
                    Interlocked.Increment(ref active);
                }

                public void DecrementActive()
                {
                    Interlocked.Decrement(ref active);

                    CompleteEvent.Set();
                }
            }

            public SharedAsyncState SharedState { get; private set; }

            public class AsyncState
            {
                public AsyncState(SolveDelegate solveDelegate)
                {
                    this.SolveDelegate = solveDelegate;
                    Interlocked.Exchange(ref this.solved, 0);
                }

                private int solved;

                public bool Solved 
                {
                    get { return (Interlocked.Exchange(ref solved, solved) == 1); }
                    set { Interlocked.Exchange(ref solved, value == true ? 1 : 0); } 
                }
                public SolveDelegate SolveDelegate { get; private set; }
            }

            public IAsyncResult BeginSolve()
            {
                SolveDelegate solveDelegate = new SolveDelegate(Solve);

                if (SharedState != null)
                    SharedState.IncrementTotal();

                return solveDelegate.BeginInvoke(SolveComplete, new AsyncState(solveDelegate));
            }

            public bool EndSolve(IAsyncResult ar)
            {
                AsyncState asyncState = (AsyncState)ar.AsyncState;

                asyncState.Solved = asyncState.SolveDelegate.EndInvoke(ar);

                if (SharedState != null)
                    SharedState.DecrementActive();

                return asyncState.Solved;
            }

            private void SolveComplete(IAsyncResult ar)
            {
                // Solve is done, so EndSolve won't block
                EndSolve(ar);
            }

            public bool Solve()
            {
                if (SharedState != null)
                    SharedState.IncrementActive();
                
                Reduce();
                
                if (Solved)
                {
                    return true;
                }
                else
                {
                    //PrintState();
                    return GuessAndReduce();
                }
            }

            public void PrintState()
            {
                for (int row = 1; row <= 9; row++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        for (int col = 1; col <= 9; col++)
                        {
                            Console.Write(this[row, col].ToString().Substring(3 * i, 3));
                            if (col == 3 || col == 6)
                                Console.Write('|');
                            else if (col != 9)
                                Console.Write(' ');
                        }

                        Console.WriteLine();
                    }

                    if (row == 3 || row == 6)
                        Console.WriteLine("-----------+-----------+-----------");
                    else if (row != 9)
                        Console.WriteLine("           |           |           ");
                }

                Console.WriteLine();
            }

            public void Print()
            {
                for (int row = 1; row <= 9; row++)
                {
                    for (int col = 1; col <= 9; col++)
                    {
                        Console.Write(this[row, col].Solved ? this[row, col].Digit.ToString() : " ");
                    
                        if (col == 3 || col == 6)
                            Console.Write('|');
                    }

                    Console.WriteLine();

                    if (row == 3 || row == 6)
                        Console.WriteLine("---+---+---");
                }

                Console.WriteLine();
            }

            #region ICloneable Members

            public object Clone()
            {
                Grid grid = new Grid();

                for (int row = 1; row <= 9; row++)
                    for (int col = 1; col <= 9; col++)
                        grid[row, col] = (Cell)this[row, col].Clone();

                return grid;
            }

            #endregion
        }

        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Syntax: sudoku [-s] [-np] <cells-file> [<cells-file> ...]\n        sudoku [-s] @<rsp-file>");
                return 0;
            }
            
            Program program = new Program();
            
            program.sequential = false;
            program.printResults = true;
            program.files = new List<string>();
            
            foreach (var arg in args)
            {
                if (arg.StartsWith("@"))
                {
                    try
                    {
                        using (StreamReader reader = new StreamReader(arg.Substring(1)))
                        {
                            string line = null;

                            while ((line = reader.ReadLine()) != null)
                            {
                                line = line.Trim();

                                if (line.Length > 0 && line[0] != ';')
                                {
                                    if (!File.Exists(line))
                                        Console.WriteLine("error: file {0} does not exist", line);
                                    else
                                        program.files.Add(line);
                                }
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        Console.WriteLine("error: file {0} does not exist", arg.Substring(1));
                        return -1;
                    }
                }
                else if (arg.StartsWith("-") || arg.StartsWith("/"))
                {
                    switch (arg.Substring(1))
                    {
                        case "s":
                            program.sequential = true;
                            break;

                        case "np":
                            program.printResults = false;
                            break;

                        default:
                            Console.WriteLine("error: unknown option '{0}'", arg);
                            return -1;
                    }
                }
                else
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (!File.Exists(args[i]))
                            Console.WriteLine("error: file {0} does not exist", args[i]);
                        else
                            program.files.Add(args[i]);
                    }
                }
            }

            program.SolvePuzzles();

            return 0;
        }

        bool sequential;
        bool printResults;
        List<string> files;
        List<Grid> grids;
        List<Grid> originalGrids;
        Stopwatch stopwatch;
        ProcessCycleStopwatch cycleStopwatch;
        int totalSolved;

        private void SolvePuzzles()
        {
            grids = new List<Grid>(files.Count);
            originalGrids = new List<Grid>(files.Count);

            if (sequential)
                ProcessSequentially();
            else
                ProcessInThreadPool();
        }

        private void PrintResults()
        {
            if (printResults)
            {
                for (int i = 0; i < grids.Count; i++)
                {
                    Console.WriteLine("Puzzle: {0}\n", files[i]);

                    originalGrids[i].Print();

                    if (grids[i] != null)
                        grids[i].Print();
                    else
                        Console.WriteLine("Puzzle was not solved!\r\n");
                }
            }
        }

        private void PrintSummary()
        {
            Console.WriteLine("{0} of {1} puzzles solved", totalSolved, files.Count);

            double seconds = (double)stopwatch.ElapsedTicks / Stopwatch.Frequency;

            if (cycleStopwatch != null)
                Console.WriteLine("Total Time {0:N6} secs, {1} cycles", seconds, cycleStopwatch.ElapsedCycles);
            else
                Console.WriteLine("Total Time {0:N6} secs", seconds);
        }

        private void StartTiming()
        {
            stopwatch = Stopwatch.StartNew();
            cycleStopwatch = null;

            if (ProcessCycleStopwatch.IsSupported)
                cycleStopwatch = ProcessCycleStopwatch.StartNew();
        }

        private void StopTiming()
        {
            if (cycleStopwatch != null)
                cycleStopwatch.Stop();

            stopwatch.Stop();
        }

        private void ProcessSequentially()
        {
            totalSolved = 0;

            StartTiming();

            for (int i = 0; i < files.Count; i++)
            {
                Grid grid = new Grid();

                grid.ReadFromFile(files[i]);

                grids.Add(grid);
                originalGrids.Add((Grid)grid.Clone());

                if (grid.Solve())
                {
                    totalSolved++;
                }
                else
                {
                    grids[i] = null;
                }
            }

            StopTiming();
            PrintResults();
            PrintSummary();
        }

        private void ProcessInThreadPool()
        {
            totalSolved = 0;
            
            List<IAsyncResult> ars = new List<IAsyncResult>(files.Count);
            Grid.SharedAsyncState sharedState = new Grid.SharedAsyncState();

            StartTiming();

            // Read all the puzzles and queue them
            for (int i = 0; i < files.Count; i++)
            {
                Grid grid = new Grid(sharedState);

                grid.ReadFromFile(files[i]);

                grids.Add(grid);
                originalGrids.Add((Grid)grid.Clone());
                ars.Add(grid.BeginSolve());
            }

            // Now wait for them all to start and all to finish
            while (true)
            {
                sharedState.CompleteEvent.WaitOne(Timeout.Infinite);

                if (sharedState.Total == files.Count && sharedState.Active == 0)
                    break;
            }

            // Now go through and check the results
            for (int i = 0; i < files.Count; i++)
            {
                Grid.AsyncState state = (Grid.AsyncState)ars[i].AsyncState;

                if (state.Solved)
                {
                    totalSolved++;
                }
                else
                {
                    grids[i] = null;
                }
            }

            StopTiming();
            PrintResults();
            PrintSummary();
        }
    }
}