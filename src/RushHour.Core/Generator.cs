using System.Text.Json;

namespace RushHour.Core;

public static class Generator
{
    private static readonly string[] Colors = ["#48a9a6", "#eab44d", "#7e8dc2", "#6caa73", "#d98b60", "#9d80b9", "#5c9dbf", "#dd9eaa"];
    private static readonly Lazy<Puzzle[]> Fallbacks = new(() =>
    {
        using var stream = typeof(Generator).Assembly.GetManifestResourceStream("RushHour.Core.fallback-puzzles.json")!;
        return JsonSerializer.Deserialize<Puzzle[]>(stream)!;
    });
    public static (int Min, int Max) DifficultyRange(Difficulty difficulty) => difficulty switch
    { Difficulty.Easy => (3, 5), Difficulty.Normal => (6, 10), Difficulty.Hard => (11, 17), _ => (18, 1000) };

    public static Puzzle Generate(int size, Difficulty difficulty, int seed, CancellationToken cancellationToken = default)
        => GenerateCore(size, difficulty, seed, cancellationToken, false).GetAwaiter().GetResult();

    // Yield periodically on single-threaded browser runtimes, preserving the same work budget and seed.
    public static Task<Puzzle> GenerateAsync(int size, Difficulty difficulty, int seed, CancellationToken cancellationToken = default)
        => GenerateCore(size, difficulty, seed, cancellationToken, true);

    private static async Task<Puzzle> GenerateCore(int size, Difficulty difficulty, int seed, CancellationToken cancellationToken, bool cooperative)
    {
        if (size is < 6 or > 12) throw new ArgumentOutOfRangeException(nameof(size));
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var random = new Random(seed);
        var range = DifficultyRange(difficulty);
        int work = 0;
        bool large = size >= 8;
        int width = large ? size / 2 : size;
        for (int attempt = 0; attempt <= 1024; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cooperative) await Task.Delay(1, cancellationToken);
            // Deterministic work budget: seed reproduction must not depend on CPU speed.
            // A certified complete component supplies fresh states when random layouts
            // use up the budget, avoiding long retries or difficulty downgrades.
            bool fallback = work >= 400000 || attempt == 1024;
            var layout = fallback ? KnownLayout(size, difficulty, seed) : Layout(size, width, difficulty, seed, random, large);
            if (large && (!layout.Vehicles.Any(v => v.Axis == Axis.Horizontal && v.Width == 2) || !layout.Vehicles.Any(v => v.Axis == Axis.Vertical && v.Height == 2))) continue;
            var board = new SearchBoard(layout, width);
            const int limit = 20000;
            var states = new List<UInt128> { board.Initial };
            var indices = new Dictionary<UInt128, int> { [board.Initial] = 0 };
            bool overflow = false;
            for (int head = 0; head < states.Count && !overflow; head++)
            {
                work++;
                if ((head & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (cooperative && (work & 255) == 0) await Task.Delay(1, cancellationToken);
                board.Neighbors(states[head], (next, _, _) =>
                {
                    if (overflow || indices.ContainsKey(next)) return;
                    if (states.Count >= limit) { overflow = true; return; }
                    indices.Add(next, states.Count); states.Add(next);
                });
            }
            if (overflow) continue;
            var distances = new int[states.Count]; Array.Fill(distances, -1);
            var queue = new Queue<int>();
            for (int i = 0; i < states.Count; i++) if (board.Goal(states[i])) { distances[i] = 1; queue.Enqueue(i); }
            while (queue.TryDequeue(out int current))
            {
                work++;
                if ((current & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (cooperative && (work & 255) == 0) await Task.Delay(1, cancellationToken);
                board.Neighbors(states[current], (next, _, _) =>
                {
                    int index = indices[next];
                    if (distances[index] < 0) { distances[index] = distances[current] + 1; queue.Enqueue(index); }
                });
            }
            var candidates = Enumerable.Range(0, states.Count).Where(i => distances[i] >= range.Min && distances[i] <= range.Max).ToArray();
            if (candidates.Length == 0) continue;
            int chosen = candidates[random.Next(candidates.Length)];
            var logical = board.Unpack(states[chosen]);
            var path = new List<Move>();
            int position = chosen;
            while (distances[position] > 1)
            {
                int nextPosition = -1; Move? move = null;
                board.Neighbors(states[position], (next, vehicle, delta) =>
                {
                    int index = indices[next];
                    if (nextPosition < 0 && distances[index] == distances[position] - 1) { nextPosition = index; move = new(logical.Vehicles[vehicle].Id, large && logical.Vehicles[vehicle].Axis == Axis.Horizontal ? delta * 2 : delta); }
                });
                path.Add(move!); position = nextPosition;
            }
            Puzzle result = large ? Lift(logical) : logical;
            result = result with { OptimalMoves = distances[chosen] };
            var replay = new GameSession(result);
            foreach (var move in path) if (!replay.TryMove(move.VehicleId, move.Delta)) throw new InvalidOperationException("生成した解の検証に失敗しました。");
            var target = replay.Puzzle.Vehicles[0];
            path.Add(new(target.Id, size - target.X - target.Width + 1));
            if (!replay.TryMove(path[^1].VehicleId, path[^1].Delta) || !replay.Won) throw new InvalidOperationException("出口への経路を検証できませんでした。");
            return result with { Solution = path.ToArray(), HorizontalScale = large ? 2 : 1 };
        }
        throw new InvalidOperationException("この条件の盤面を時間内に生成できませんでした。もう一度「新しいパズル」を押してください。");
    }

    private static Puzzle KnownLayout(int size, Difficulty difficulty, int seed)
    {
        var puzzle = Fallbacks.Value.Single(p => p.Size == size && p.Difficulty == difficulty) with { Seed = seed, Solution = [], HorizontalScale = 1 };
        if (size < 8) return puzzle;
        return puzzle with { Vehicles = puzzle.Vehicles.Select(v => v.Axis == Axis.Horizontal
            ? v with { X = v.X / 2, Width = v.Width / 2 }
            : v with { X = (v.X + v.Width) / 2 - 1, Width = 1 }).ToArray() };
    }

    private static Puzzle Layout(int size, int width, Difficulty difficulty, int seed, Random random, bool large)
    {
        int exitRow = size / 2 - 1;
        var vehicles = new List<Vehicle> { new(0, large ? width - 1 : 0, exitRow, large ? 1 : 2, 1, Axis.Horizontal, VehicleKind.Car, "#e65b48", true) };
        int desired = (int)(width * size * (large || size == 7 ? .82 + random.NextDouble() * .15 : .64 + random.NextDouble() * .16));
        int occupied = vehicles[0].Width;
        for (int t = 0; t < 900 && occupied < desired && vehicles.Count < 32; t++)
        {
            Axis axis = random.Next(2) == 0 ? Axis.Horizontal : Axis.Vertical;
            int length = large ? (axis == Axis.Horizontal ? (random.NextDouble() < .35 ? 1 : 2) : (random.NextDouble() < .2 ? 2 : random.NextDouble() < .45 ? 3 : 4)) : (random.NextDouble() < .30 ? 3 : 2);
            int w = axis == Axis.Horizontal ? length : 1, h = axis == Axis.Vertical ? length : 1;
            int x = random.Next(width - w + 1), y = random.Next(size - h + 1);
            if (axis == Axis.Horizontal && y == exitRow) continue;
            if (vehicles.Any(v => x < v.X + v.Width && x + w > v.X && y < v.Y + v.Height && y + h > v.Y)) continue;
            vehicles.Add(new(vehicles.Count, x, y, w, h, axis, length >= 4 ? VehicleKind.Trailer : length == 3 ? VehicleKind.Truck : VehicleKind.Car, Colors[(vehicles.Count - 1) % Colors.Length]));
            occupied += w * h;
        }
        return new(size, exitRow, seed, difficulty, 0, vehicles.ToArray());
    }

    private static Puzzle Lift(Puzzle logical)
    {
        // Every physical solution projects via floor(X/2) to a logical one;
        // every logical slide lifts to the even-X physical positions. Thus
        // the optimal slide count is equal (also on odd-width boards).
        var vehicles = logical.Vehicles.Select(v =>
        {
            bool horizontal = v.Axis == Axis.Horizontal;
            int width = horizontal ? v.Width * 2 : v.Height == 2 ? 2 : 1;
            int x = horizontal ? v.X * 2 : 2 * (v.X + 1) - width;
            var kind = !horizontal && width == 2 ? VehicleKind.Excavator : Math.Max(width, v.Height) >= 4 ? VehicleKind.Trailer : Math.Max(width, v.Height) == 3 ? VehicleKind.Truck : VehicleKind.Car;
            return v with { X = x, Width = width, Kind = kind };
        }).ToArray();
        return logical with { Vehicles = vehicles };
    }
}
