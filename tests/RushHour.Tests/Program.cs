using System.Diagnostics;
using RushHour.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 4 && args[0] == "--generate")
{
    var generated = Generator.Generate(int.Parse(args[1]), Enum.Parse<Difficulty>(args[2]), int.Parse(args[3]));
    Console.WriteLine(JsonSerializer.Serialize(generated));
    return;
}
if (args.Length == 2 && args[0] == "--catalog")
{
    var catalog = new List<Puzzle>();
    foreach (int size in Enumerable.Range(6, 7))
    foreach (Difficulty difficulty in Enum.GetValues<Difficulty>())
    {
        var puzzle = Generator.Generate(size, difficulty, 12345 + size * 100 + (int)difficulty);
        catalog.Add(puzzle);
        Console.WriteLine($"Certified fallback {size}/{difficulty}: {puzzle.OptimalMoves}");
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(catalog));
    return;
}

if (args.Length == 2 && args[0] == "--solve")
{
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    var input = JsonSerializer.Deserialize<Puzzle>(File.ReadAllText(args[1]), options)!;
    Console.WriteLine(JsonSerializer.Serialize(Solver.Solve(input, 1000000), options));
    return;
}

int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
var simple = new Puzzle(6, 2, 1, Difficulty.Easy, 2, [new(0, 0, 2, 2, 1, Axis.Horizontal, VehicleKind.Car, "red", true), new(1, 3, 1, 1, 2, Axis.Vertical, VehicleKind.Car, "blue")]);
var session = new GameSession(simple);
Check(!session.TryMove(0, 4), "Cannot jump blocker");
Check(!session.TryMove(1, -2), "Cannot leave board");
Check(!session.TryMove(99, 1), "Invalid ID");
Check(session.TryMove(1, 2), "Move blocker");
Check(session.TryMove(0, 5) && session.Won && session.Moves == 2, "Exit in one slide");
Check(!session.TryMove(0, -1), "No movement after winning");
Check(session.Undo() && !session.Won && session.Moves == 1, "Undo win");
session.Restart(); Check(session.Puzzle == simple && session.Moves == 0, "Restart");
Check(Solver.Solve(simple)!.Count == 2, "Shortest solution");
var wide = new Puzzle(8, 3, 1, Difficulty.Easy, 0, [new(0, 0, 3, 2, 1, Axis.Horizontal, VehicleKind.Car, "red", true), new(1, 3, 2, 2, 2, Axis.Vertical, VehicleKind.Excavator, "gold"), new(2, 4, 5, 2, 1, Axis.Horizontal, VehicleKind.Car, "blue")]);
Check(Rules.Range(wide, wide.Vehicles[1]).Max == 1, "Wide vehicle checks whole leading edge");
var deterministicA = Generator.Generate(6, Difficulty.Normal, 91721);
var deterministicB = Generator.Generate(6, Difficulty.Normal, 91721);
Check(deterministicA.Vehicles.SequenceEqual(deterministicB.Vehicles), "Same seed reproduces board");
var cooperative = await Generator.GenerateAsync(6, Difficulty.Normal, 91721);
Check(cooperative.Vehicles.SequenceEqual(deterministicA.Vehicles) && cooperative.Solution.SequenceEqual(deterministicA.Solution), "Browser and desktop generation agree");
Check((await Solver.FindHintPathAsync(cooperative))!.SequenceEqual(Solver.FindHintPath(cooperative)!), "Browser and desktop hints agree");
using (var pending = new CancellationTokenSource())
{
    var generation = Generator.GenerateAsync(12, Difficulty.Expert, 91721, pending.Token);
    pending.Cancel();
    bool interrupted = false;
    try { await generation; } catch (OperationCanceledException) { interrupted = true; }
    Check(interrupted, "Cooperative generation can be canceled after starting");
}
int BreadthFirstDistance(Puzzle puzzle)
{
    string Key(Puzzle p) => string.Join(',', p.Vehicles.Select(v => v.Axis == Axis.Horizontal ? v.X : v.Y));
    var seen = new HashSet<string> { Key(puzzle) };
    var queue = new Queue<(Puzzle Board, int Depth)>(); queue.Enqueue((puzzle, 0));
    while (queue.TryDequeue(out var node))
    {
        var target = node.Board.Vehicles.Single(v => v.IsTarget);
        if (target.X + target.Width + Rules.Range(node.Board, target).Max > puzzle.Size) return node.Depth + 1;
        foreach (var v in node.Board.Vehicles)
        {
            var range = Rules.Range(node.Board, v);
            for (int delta = range.Min; delta <= range.Max; delta++)
            {
                if (delta == 0) continue;
                var game = new GameSession(node.Board); game.TryMove(v.Id, delta);
                if (seen.Add(Key(game.Puzzle))) queue.Enqueue((game.Puzzle, node.Depth + 1));
            }
        }
    }
    return -1;
}
Check(Solver.Solve(deterministicA)!.Count == BreadthFirstDistance(deterministicA), "A* matches independent breadth-first optimum");
var walk = new GameSession(deterministicA);
var rng = new Random(762);
for (int i = 0; i < 200; i++)
{
    var vehicle = walk.Puzzle.Vehicles[rng.Next(walk.Puzzle.Vehicles.Count)];
    var bounds = Rules.Range(walk.Puzzle, vehicle);
    var before = walk.Puzzle;
    int delta = rng.Next(bounds.Min, bounds.Max + 1);
    if (delta == 0) continue;
    Check(walk.TryMove(vehicle.Id, delta), "Legal random move accepted");
    Rules.Validate(walk.Puzzle);
    Check(walk.Undo() && walk.Puzzle == before, "Random move undo restores exact state");
}
bool canceled = false;
try { Generator.Generate(6, Difficulty.Easy, 1, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
Check(canceled, "Generation cancellation");
var sizes = args.Contains("--full") || args.Contains("--sweep") ? Enumerable.Range(6, 7).ToArray() : args.Contains("--small") ? new[] { 6 } : new[] { 6, 8, 12 };
foreach (int round in Enumerable.Range(0, args.Contains("--sweep") ? 5 : 1))
foreach (int size in sizes)
foreach (Difficulty difficulty in Enum.GetValues<Difficulty>())
{
    var watch = Stopwatch.StartNew();
    var puzzle = Generator.Generate(size, difficulty, 12345 + size * 100 + (int)difficulty + round * 9973);
    Rules.Validate(puzzle);
    var solution = Solver.Solve(puzzle)!;
    Check(solution is not null, "Solvable");
    var range = Generator.DifficultyRange(difficulty);
    Check(solution!.Count == puzzle.OptimalMoves && solution.Count >= range.Min && solution.Count <= range.Max, "Certified difficulty");
    var game = new GameSession(puzzle);
    foreach (var move in solution) Check(game.TryMove(move.VehicleId, move.Delta), "Solution move legal");
    Check(game.Won, "Solution wins");
    Check(puzzle.Solution.Count == puzzle.OptimalMoves, "Certificate length");
    var certificate = new GameSession(puzzle);
    foreach (var move in puzzle.Solution) Check(certificate.TryMove(move.VehicleId, move.Delta), "Certificate move legal");
    Check(certificate.Won, "Certificate wins");
    if (size >= 8) Check(puzzle.Vehicles.Any(v => v.Width == 2 && v.Height == 2) && puzzle.Vehicles.Any(v => v.Width == 4 || v.Height == 4), "Large vehicles");
    if (size >= 8)
    {
        var shifted = new GameSession(puzzle);
        foreach (var v in puzzle.Vehicles.Where(v => v.Axis == Axis.Horizontal))
        {
            var bounds = Rules.Range(puzzle, v);
            int delta = bounds.Min <= -1 ? -1 : bounds.Max >= 1 && v.X + v.Width < size ? 1 : 0;
            if (delta != 0) { shifted.TryMove(v.Id, delta); break; }
        }
        var hints = Solver.FindHintPath(shifted.Puzzle);
        var asyncHints = await Solver.FindHintPathAsync(shifted.Puzzle);
        Check(asyncHints is not null && asyncHints.SequenceEqual(hints!), "Cooperative scaled hints agree after player movement");
        Check(hints is not null, "Hint from odd physical positions");
        foreach (var move in hints!) Check(shifted.TryMove(move.VehicleId, move.Delta), "Hint path legal");
        Check(shifted.Won, "Hint path wins");
        if (size <= 9 && difficulty == Difficulty.Normal)
            Check(Solver.Solve(puzzle with { HorizontalScale = 1 })?.Count == puzzle.OptimalMoves, "Physical A* independently matches logical optimum");
    }
    Console.WriteLine($"{size}x{size} {difficulty}: {solution.Count} moves, {puzzle.Vehicles.Count} vehicles, {watch.ElapsedMilliseconds}ms");
}
Console.WriteLine($"PASS: {assertions} assertions");
