using System.Numerics;

namespace RushHour.Core;

// Four bits per movable coordinate: up to 32 vehicles on a 12 x 12 board.
internal sealed class SearchBoard(Puzzle puzzle, int? horizontalLimit = null)
{
    public readonly Puzzle Puzzle = puzzle;
    public UInt128 Initial => Pack(Puzzle.Vehicles);
    public static UInt128 Pack(IReadOnlyList<Vehicle> vehicles)
    {
        UInt128 state = 0;
        for (int i = 0; i < vehicles.Count; i++) state |= (UInt128)(vehicles[i].Axis == Axis.Horizontal ? vehicles[i].X : vehicles[i].Y) << (i * 4);
        return state;
    }
    public int Position(UInt128 state, int i) => (int)((state >> (i * 4)) & 15);
    public Puzzle Unpack(UInt128 state) => Puzzle with { Vehicles = Puzzle.Vehicles.Select((v, i) => v.Axis == Axis.Horizontal
        ? v with { X = Position(state, i) } : v with { Y = Position(state, i) }).ToArray() };
    public bool Goal(UInt128 state)
    {
        var target = Puzzle.Vehicles[0];
        int start = Position(state, 0) + target.Width;
        for (int i = 1; i < Puzzle.Vehicles.Count; i++)
        {
            var v = Puzzle.Vehicles[i]; int pos = Position(state, i);
            int x = v.Axis == Axis.Horizontal ? pos : v.X, y = v.Axis == Axis.Vertical ? pos : v.Y;
            if (y <= Puzzle.ExitRow && y + v.Height > Puzzle.ExitRow && x + v.Width > start) return false;
        }
        return true;
    }
    // Every car in the exit corridor must move, plus the target. For each
    // blocker at least one clearing direction must be possible. Its swept
    // rectangle gives additional mandatory vehicles; use the largest lower
    // bound, never a sum that would double-count shared dependencies.
    public int LowerBound(UInt128 state)
    {
        Span<int> xs = stackalloc int[32], ys = stackalloc int[32];
        uint blockers = 0;
        int count = Puzzle.Vehicles.Count, row = Puzzle.ExitRow;
        int start = Position(state, 0) + Puzzle.Vehicles[0].Width;
        for (int i = 0; i < count; i++)
        {
            var v = Puzzle.Vehicles[i]; int pos = Position(state, i);
            xs[i] = v.Axis == Axis.Horizontal ? pos : v.X; ys[i] = v.Axis == Axis.Vertical ? pos : v.Y;
            if (i > 0 && ys[i] <= row && ys[i] + v.Height > row && xs[i] + v.Width > start) blockers |= 1u << i;
        }
        int extra = 0;
        for (int i = 1; i < count; i++)
        {
            if ((blockers & (1u << i)) == 0) continue;
            var v = Puzzle.Vehicles[i];
            if (v.Axis != Axis.Vertical) return 1000; // Order on the target's horizontal rail cannot change.
            int best = 1000;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int newY = dir < 0 ? row - v.Height : row + 1;
                if (newY < 0 || newY + v.Height > Puzzle.Size) continue;
                int top = dir < 0 ? newY : ys[i] + v.Height;
                int bottom = dir < 0 ? ys[i] : newY + v.Height;
                uint mask = 0;
                for (int j = 1; j < count; j++)
                {
                    var other = Puzzle.Vehicles[j];
                    if (j != i && (blockers & (1u << j)) == 0 && xs[j] < xs[i] + v.Width && xs[j] + other.Width > xs[i] && ys[j] < bottom && ys[j] + other.Height > top) mask |= 1u << j;
                }
                best = Math.Min(best, BitOperations.PopCount(mask));
            }
            extra = Math.Max(extra, best);
        }
        return 1 + BitOperations.PopCount(blockers) + extra;
    }
    public void Neighbors(UInt128 state, Action<UInt128, int, int> visit)
    {
        Span<byte> cells = stackalloc byte[144]; cells.Clear();
        int size = Puzzle.Size;
        for (int i = 0; i < Puzzle.Vehicles.Count; i++)
        {
            var v = Puzzle.Vehicles[i]; int pos = Position(state, i);
            int x = v.Axis == Axis.Horizontal ? pos : v.X, y = v.Axis == Axis.Vertical ? pos : v.Y;
            for (int yy = y; yy < y + v.Height; yy++) for (int xx = x; xx < x + v.Width; xx++) cells[yy * size + xx] = (byte)(i + 1);
        }
        for (int i = 0; i < Puzzle.Vehicles.Count; i++)
        {
            var v = Puzzle.Vehicles[i]; int pos = Position(state, i);
            for (int direction = -1; direction <= 1; direction += 2)
            {
                for (int delta = direction; ; delta += direction)
                {
                    int edge = pos + delta + (direction > 0 ? (v.Axis == Axis.Horizontal ? v.Width : v.Height) - 1 : 0);
                    if (edge < 0 || edge >= (v.Axis == Axis.Horizontal ? horizontalLimit ?? size : size)) break;
                    bool blocked = false;
                    int cross = v.Axis == Axis.Horizontal ? v.Y : v.X;
                    int thickness = v.Axis == Axis.Horizontal ? v.Height : v.Width;
                    for (int k = cross; k < cross + thickness; k++)
                    { int c = cells[v.Axis == Axis.Horizontal ? k * size + edge : edge * size + k]; if (c != 0 && c != i + 1) { blocked = true; break; } }
                    if (blocked) break;
                    UInt128 next = (state & ~((UInt128)15 << (i * 4))) | ((UInt128)(pos + delta) << (i * 4));
                    visit(next, i, delta);
                }
            }
        }
    }
}

public static class Solver
{
    // A move is one slide of any distance; the last move drives through the exit.
    public static IReadOnlyList<Move>? Solve(Puzzle puzzle, int maxStates = 180000, CancellationToken cancellationToken = default)
    {
        if (puzzle.HorizontalScale == 2 && puzzle.Vehicles.Where(v => v.Axis == Axis.Horizontal).All(v => v.X % 2 == 0))
            return SolveScaled(puzzle, maxStates, cancellationToken).GetAwaiter().GetResult();
        return Search(puzzle, maxStates, cancellationToken).GetAwaiter().GetResult();
    }

    // Hints need a valid route, not necessarily an optimal route after a player
    // leaves the canonical even-X positions. Align left-to-right, then solve
    // the much smaller logical board. Canonical starting puzzles stay optimal.
    public static IReadOnlyList<Move>? FindHintPath(Puzzle puzzle, CancellationToken cancellationToken = default) =>
        (puzzle.HorizontalScale == 2 ? SolveScaled(puzzle, 50000, cancellationToken) : Search(puzzle, 100000, cancellationToken)).GetAwaiter().GetResult();

    public static Task<IReadOnlyList<Move>?> FindHintPathAsync(Puzzle puzzle, CancellationToken cancellationToken = default) =>
        puzzle.HorizontalScale == 2 ? SolveScaled(puzzle, 50000, cancellationToken, true) : Search(puzzle, 100000, cancellationToken, cooperative: true);

    private static async Task<IReadOnlyList<Move>?> SolveScaled(Puzzle puzzle, int maxStates, CancellationToken cancellationToken, bool cooperative = false)
    {
        var game = new GameSession(puzzle);
        var path = new List<Move>();
        foreach (var v in puzzle.Vehicles.Where(v => v.Axis == Axis.Horizontal && v.X % 2 != 0).OrderBy(v => v.X))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!game.TryMove(v.Id, -1)) return null;
            path.Add(new(v.Id, -1));
        }
        var logical = game.Puzzle with
        {
            HorizontalScale = 1,
            Vehicles = game.Puzzle.Vehicles.Select(v => v.Axis == Axis.Horizontal ? v with { X = v.X / 2, Width = v.Width / 2 } : v with { X = (v.X + v.Width) / 2 - 1, Width = 1 }).ToArray()
        };
        var solution = await Search(logical, maxStates, cancellationToken, puzzle.Size / 2, cooperative);
        if (solution is null) return null;
        foreach (var move in solution.Take(solution.Count - 1))
        {
            var v = puzzle.Vehicles.First(v => v.Id == move.VehicleId);
            var lifted = move with { Delta = v.Axis == Axis.Horizontal ? move.Delta * 2 : move.Delta };
            if (!game.TryMove(lifted.VehicleId, lifted.Delta)) return null;
            path.Add(lifted);
        }
        var target = game.Puzzle.Vehicles.Single(v => v.IsTarget);
        path.Add(new(target.Id, puzzle.Size - target.X - target.Width + 1));
        if (!game.TryMove(path[^1].VehicleId, path[^1].Delta) || !game.Won) return null;
        return path;
    }

    private static async Task<IReadOnlyList<Move>?> Search(Puzzle puzzle, int maxStates, CancellationToken cancellationToken, int? horizontalLimit = null, bool cooperative = false)
    {
        Rules.Validate(puzzle);
        cancellationToken.ThrowIfCancellationRequested();
        if (cooperative) await Task.Delay(1, cancellationToken);
        // The packed engine uses the target as its first vehicle.
        puzzle = puzzle with { Vehicles = puzzle.Vehicles.OrderByDescending(v => v.IsTarget).ToArray() };
        var board = new SearchBoard(puzzle, horizontalLimit);
        var nodes = new List<(UInt128 State, int Parent, int Vehicle, int Delta, int Cost)> { (board.Initial, -1, 0, 0, 0) };
        var best = new Dictionary<UInt128, int> { [board.Initial] = 0 };
        var open = new PriorityQueue<int, (int Estimate, int Tie)>();
        open.Enqueue(0, (board.LowerBound(board.Initial), 0));
        int expanded = 0;
        bool exhausted = false;
        while (open.TryDequeue(out int head, out _) && expanded < maxStates)
        {
            var current = nodes[head];
            if (best[current.State] != current.Cost) continue;
            if ((expanded++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (cooperative && (expanded & 255) == 0) await Task.Delay(1, cancellationToken);
            if (board.Goal(current.State))
            {
                var path = new List<Move>();
                for (int at = head; nodes[at].Parent >= 0; at = nodes[at].Parent)
                    path.Add(new Move(puzzle.Vehicles[nodes[at].Vehicle].Id, nodes[at].Delta));
                path.Reverse();
                path.Add(new Move(puzzle.Vehicles[0].Id, (horizontalLimit ?? puzzle.Size) - puzzle.Vehicles[0].Width - board.Position(current.State, 0) + 1));
                return path;
            }
            board.Neighbors(current.State, (next, vehicle, delta) =>
            {
                int cost = current.Cost + 1;
                if (best.TryGetValue(next, out int previous) && previous <= cost) return;
                if (nodes.Count >= maxStates * 5) { exhausted = true; return; }
                int bound = board.LowerBound(next);
                if (bound >= 1000) return;
                best[next] = cost;
                nodes.Add((next, head, vehicle, delta, cost));
                open.Enqueue(nodes.Count - 1, (cost + bound, -cost));
            });
            if (exhausted) return null;
        }
        return null;
    }
}
