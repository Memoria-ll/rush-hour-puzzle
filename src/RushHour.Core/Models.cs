namespace RushHour.Core;

public enum Axis { Horizontal, Vertical }
public enum VehicleKind { Car, Truck, Trailer, Excavator }
public enum Difficulty { Easy, Normal, Hard, Expert }
public sealed record Vehicle(int Id, int X, int Y, int Width, int Height, Axis Axis,
    VehicleKind Kind, string Color, bool IsTarget = false)
{
    public string Name => IsTarget ? "赤い車" : Kind switch
    { VehicleKind.Truck => "トラック", VehicleKind.Trailer => "トレーラー", VehicleKind.Excavator => "ショベルカー", _ => "乗用車" };
}
public sealed record Move(int VehicleId, int Delta);
public sealed record Puzzle(int Size, int ExitRow, int Seed, Difficulty Difficulty,
    int OptimalMoves, IReadOnlyList<Vehicle> Vehicles)
{
    public int HorizontalScale { get; init; } = 1;
    public IReadOnlyList<Move> Solution { get; init; } = [];
}

public sealed class GameSession
{
    private readonly Puzzle initial;
    private readonly Stack<(Puzzle Puzzle, bool Won)> history = new();
    public Puzzle Puzzle { get; private set; }
    public bool Won { get; private set; }
    public int Moves => history.Count;
    public GameSession(Puzzle puzzle) { Rules.Validate(puzzle); initial = puzzle; Puzzle = puzzle; }
    public bool TryMove(int id, int delta)
    {
        if (Won || delta == 0) return false;
        var vehicle = Puzzle.Vehicles.FirstOrDefault(v => v.Id == id);
        if (vehicle is null) return false;
        var (min, max) = Rules.Range(Puzzle, vehicle);
        if (delta < min || delta > max) return false;
        history.Push((Puzzle, Won));
        if (vehicle.IsTarget && vehicle.X + delta + vehicle.Width > Puzzle.Size) Won = true;
        else Puzzle = Puzzle with { Vehicles = Puzzle.Vehicles.Select(v => v.Id != id ? v : v with
        { X = v.X + (v.Axis == Axis.Horizontal ? delta : 0), Y = v.Y + (v.Axis == Axis.Vertical ? delta : 0) }).ToArray() };
        return true;
    }
    public bool Undo()
    {
        if (!history.TryPop(out var previous)) return false;
        Puzzle = previous.Puzzle; Won = previous.Won; return true;
    }
    public void Restart() { Puzzle = initial; Won = false; history.Clear(); }
}

public static class Rules
{
    public static void Validate(Puzzle puzzle)
    {
        if (puzzle.Size is < 6 or > 12 || puzzle.Vehicles.Count is < 1 or > 32) throw new ArgumentException("盤面のサイズが不正です。");
        if (puzzle.Vehicles.Select(v => v.Id).Distinct().Count() != puzzle.Vehicles.Count) throw new ArgumentException("車両IDが重複しています。");
        var occupied = new bool[puzzle.Size * puzzle.Size];
        foreach (var v in puzzle.Vehicles)
        {
            if (v.Width < 1 || v.Height < 1 || v.X < 0 || v.Y < 0 || v.X + v.Width > puzzle.Size || v.Y + v.Height > puzzle.Size)
                throw new ArgumentException("車両が盤面の外にあります。");
            for (int y = v.Y; y < v.Y + v.Height; y++) for (int x = v.X; x < v.X + v.Width; x++)
            { if (occupied[y * puzzle.Size + x]) throw new ArgumentException("車両が重なっています。"); occupied[y * puzzle.Size + x] = true; }
        }
        var targets = puzzle.Vehicles.Where(v => v.IsTarget).ToArray();
        if (targets.Length != 1 || targets[0].Axis != Axis.Horizontal || targets[0].Height != 1 || targets[0].Y != puzzle.ExitRow)
            throw new ArgumentException("出口と赤い車の位置が不正です。");
    }
    public static (int Min, int Max) Range(Puzzle puzzle, Vehicle vehicle)
    {
        var min = 0; var max = 0;
        bool Fits(int delta)
        {
            int x = vehicle.X + (vehicle.Axis == Axis.Horizontal ? delta : 0);
            int y = vehicle.Y + (vehicle.Axis == Axis.Vertical ? delta : 0);
            if (x < 0 || y < 0 || x + vehicle.Width > puzzle.Size || y + vehicle.Height > puzzle.Size) return false;
            return !puzzle.Vehicles.Any(v => v.Id != vehicle.Id && x < v.X + v.Width && x + vehicle.Width > v.X && y < v.Y + v.Height && y + vehicle.Height > v.Y);
        }
        while (Fits(min - 1)) min--;
        while (Fits(max + 1)) max++;
        if (vehicle.IsTarget && vehicle.X + max + vehicle.Width == puzzle.Size) max++;
        return (min, max);
    }
}
