using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RushHour.Core;

namespace RushHour.Desktop.Components;

public partial class Game : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    protected int SelectedSize = 6;
    protected Difficulty SelectedDifficulty = Difficulty.Normal;
    protected string SeedText = "";
    protected GameSession? Session;
    protected bool IsBusy, ShowHelp, HintVisible;
    protected string? Error, HintText;
    protected string Status = "赤い車を右の出口へ";
    protected string BusyMessage = "盤面を生成しています…";
    protected string ElapsedText => $"{(int)clock.Elapsed.TotalMinutes:00}:{clock.Elapsed.Seconds:00}";
    protected Puzzle? Board => Session?.Puzzle;
    protected IReadOnlyList<Vehicle> Vehicles => Board?.Vehicles ?? [];
    protected int Moves => Session?.Moves ?? 0;
    protected bool Won => Session?.Won ?? false;
    protected int ExitRow => Board?.ExitRow ?? 2;
    protected int BestMoves => Board?.OptimalMoves ?? 0;
    protected string PuzzleCode => Board is null ? "—" : $"{Board.Size}-{(int)Board.Difficulty + 1}-{Board.Seed}";
    protected string DifficultyLabel => (Board?.Difficulty ?? SelectedDifficulty) switch { Difficulty.Easy => "やさしい", Difficulty.Normal => "ふつう", Difficulty.Hard => "むずかしい", _ => "エキスパート" };
    protected static string Css(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private readonly Stopwatch clock = new();
    private readonly CancellationTokenSource lifetime = new();
    private DotNetObjectReference<Game>? reference;
    private Task? timerTask;
    private bool disposed;
    private bool needsSync;
    private bool needsHelpSync;
    private int revision;
    private int hintId = -1;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            reference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("rushHour.init", reference);
            timerTask = Tick();
            await NewGame();
        }
        if (needsSync && Board is not null && !disposed)
        {
            needsSync = false;
            await JS.InvokeVoidAsync("rushHour.sync", Board.Size, Board.Vehicles.Select(v => new { v.Id, v.X, v.Y, v.Width, v.Height, horizontal = v.Axis == Axis.Horizontal, v.IsTarget }), IsBusy || Won, revision);
            await JS.InvokeVoidAsync("rushHour.hint", hintId);
        }
        if (needsHelpSync && !disposed)
        {
            needsHelpSync = false;
            await JS.InvokeVoidAsync("rushHour.help", ShowHelp);
        }
    }
    private async Task Tick()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(lifetime.Token)) if (clock.IsRunning) await InvokeAsync(StateHasChanged); }
        catch (OperationCanceledException) { }
    }
    protected async Task NewGame()
    {
        if (IsBusy) return;
        int seed;
        if (string.IsNullOrWhiteSpace(SeedText)) seed = Random.Shared.Next(1, int.MaxValue);
        else if (!int.TryParse(SeedText, out seed)) { Error = "シードは整数で入力してください。空欄ならランダムです。"; return; }
        IsBusy = true; Error = null; ClearHint(); clock.Stop();
        BusyMessage = "盤面を生成しています…";
        Status = "解ける盤面を生成しています…"; Sync();
        try
        {
            int size = SelectedSize; Difficulty difficulty = SelectedDifficulty;
            var puzzle = OperatingSystem.IsBrowser()
                ? await Generator.GenerateAsync(size, difficulty, seed, lifetime.Token)
                : await Task.Run(() => Generator.Generate(size, difficulty, seed, lifetime.Token), lifetime.Token);
            if (disposed) return;
            Session = new GameSession(puzzle); clock.Restart(); Status = "赤い車を右の出口へ";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = ex.Message; Status = "生成できませんでした"; if (Session is not null && !Won) clock.Start(); }
        finally { IsBusy = false; if (!disposed) Sync(); }
    }
    protected Task Restart()
    {
        if (!IsBusy && Session is not null) { Session.Restart(); clock.Restart(); ClearHint(); Status = "同じ盤面でもう一度"; Sync(); }
        return Task.CompletedTask;
    }
    protected Task Undo()
    {
        if (!IsBusy && Session?.Undo() == true) { clock.Start(); ClearHint(); Status = "一手戻しました"; Sync(); }
        return Task.CompletedTask;
    }
    protected async Task Hint()
    {
        if (IsBusy || Won || Board is null) return;
        IsBusy = true; BusyMessage = "ヒントを探しています…"; ClearHint(); Sync();
        try
        {
            var puzzle = Board;
            var solution = OperatingSystem.IsBrowser()
                ? await Solver.FindHintPathAsync(puzzle, lifetime.Token)
                : await Task.Run(() => Solver.FindHintPath(puzzle, lifetime.Token), lifetime.Token);
            if (disposed) return;
            if (solution is null || solution.Count == 0) { HintText = "ヒントの探索上限に達しました。一手戻すと見つかる場合があります。"; HintVisible = true; }
            else
            {
                var move = solution[0]; var vehicle = Vehicles.First(v => v.Id == move.VehicleId);
                string direction = vehicle.Axis == Axis.Horizontal ? (move.Delta > 0 ? "右" : "左") : (move.Delta > 0 ? "下" : "上");
                HintText = $"光っている{vehicle.Name}を{direction}へ{Math.Abs(move.Delta)}マス動かします。";
                HintVisible = true; hintId = move.VehicleId;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; if (!disposed) Sync(); }
    }
    protected void ToggleHelp() { ShowHelp = !ShowHelp; needsHelpSync = true; StateHasChanged(); }
    [JSInvokable] public void CloseHelp() { if (ShowHelp) ToggleHelp(); }
    private void ClearHint() { HintVisible = false; HintText = null; hintId = -1; }
    private void Sync() { revision++; needsSync = true; StateHasChanged(); }
    [JSInvokable]
    public bool CommitMove(int id, int delta, int expectedRevision)
    {
        if (disposed || IsBusy || expectedRevision != revision || Session is null || !Session.TryMove(id, delta)) return false;
        ClearHint();
        if (Won) { clock.Stop(); Status = "脱出成功！"; } else Status = "赤い車を右の出口へ";
        Sync(); return true;
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true; await lifetime.CancelAsync();
        if (timerTask is not null) await timerTask;
        try { await JS.InvokeVoidAsync("rushHour.dispose"); } catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or TaskCanceledException) { }
        reference?.Dispose(); lifetime.Dispose();
    }
}
