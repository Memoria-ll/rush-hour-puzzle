# UI/UX implementation delegated to Claude Opus

Build a polished Japanese Windows Rush Hour puzzle UI. You own ONLY these files (create directories as needed):
- src/RushHour.Desktop/Components/Game.razor
- src/RushHour.Desktop/Components/VehicleArt.razor
- src/RushHour.Desktop/wwwroot/app.css
- src/RushHour.Desktop/wwwroot/index.html

Do not implement logic or JS. Main agent is doing core, WPF host, Game.razor.cs, drag.js, tests, packaging.
Use Blazor .NET 8 Razor. Namespace RushHour.Desktop.Components, partial class Game. Core namespace RushHour.Core. _Imports will be supplied. No external fonts, images, CDN, npm, or dependencies. Lightweight inline SVG vehicle art with 3D bevels/shadows, distinctive red target sports car, cars, trucks, trailer (length4), excavator (2x2 or 3x2). Top-down-ish dimensional miniature parking lot, strong polished playful editorial game aesthetic. Japanese UI, warm off-white and dark asphalt board, orange accent, compact left settings/control column, generous board, distinct status and typography. Actual usable controls. Fit 1180x820 Windows and responsive down to 850x620. SVG vehicles must fill whole cells and show directional arrow, particularly square excavator. SVG render layout horizontal internally and rotate visually for vertical. Avoid physically tilting board (drag coordinates are axis aligned). Gradients and highlights give 3D look. Don't create huge bitmap graphics.

Game.razor.cs API (provided by main agent):
- fields: int SelectedSize=6 (6..12); Difficulty SelectedDifficulty=Difficulty.Normal; string SeedText="";
- GameSession? Session; bool IsBusy; string? Error; string Status; string ElapsedText; bool ShowHelp; bool HintVisible; string? HintText;
- properties: Puzzle? Board => Session?.Puzzle; IReadOnlyList<Vehicle> Vehicles => Board?.Vehicles ?? []; int Moves; bool Won; int ExitRow; int BestMoves; string DifficultyLabel; string PuzzleCode;
- methods: Task NewGame(); Task Restart(); Task Undo(); Task Hint(); void ToggleHelp();
- choose size via @bind="SelectedSize", difficulty via @bind="SelectedDifficulty", seed via @bind="SeedText". Options Easy/Normal/Hard/Expert displayed やさしい/ふつう/むずかしい/エキスパート. Explain seed optional, same seed recreates puzzle. Difficulty shows certified shortest solution count BestMoves (includes final exit move).
- Board DOM contract: id="game-board" style="--size: @Board.Size"; position:relative, aspect-ratio:1, NO padding or border included in measurable rect (outer frame separate). Empty cell grid can be background or .grid-cell elements.
- EACH vehicle root BUTTON .vehicle with data-id="@vehicle.Id", data-x/y/w/h="@vehicle.X" etc and data-axis="h" or "v" and data-target="true"/"false"; absolute left/top/width/height calculated as 100 * X/Size etc with invariant culture via helper Css(double value) supplied in Game.cs returning invariant string. data attributes MUST use integer values. aria-label Japanese with vehicle type and arrow instructions. JS handles all pointer and keyboard; no Blazor move event handlers. disabled when IsBusy or Won. @key=vehicle.Id. .vehicle has transparent background; inner .vehicle-art inset around 5%; JS moves using transform translate pixels; do NOT CSS transform vehicle root. VehicleArt parameters: [Parameter] public Vehicle Vehicle {get;set;} = default!;
- Vehicle record: int Id, int X,Y,Width,Height; Axis Axis (Horizontal/Vertical); VehicleKind Kind (Car,Truck,Trailer,Excavator); string Color; bool IsTarget; string Name. Immutable record.
- Puzzle: int Size, ExitRow, Seed; Difficulty Difficulty; int OptimalMoves; IReadOnlyList<Vehicle> Vehicles.
- Exit indicator to right at ExitRow, make obvious red car should reach right edge. JS allows target extra +1 cell past right boundary to escape.
- Buttons: 新しいパズル, 一手戻す, やり直す, ヒント; disabled sensible; help expandable or modal; board loading overlay; inline errors; completion overlay with moves/time/optimal and next puzzle button.
- Hint highlights via JS adding .hint to vehicle root (CSS style this).
- Header small brand and muted badge OFFLINE / DESKTOP; footer short drag/keyboard hints. No meaningless buttons.
- host index.html: html lang=ja, meta charset UTF-8, viewport, title Rush Hour Garage, base href="/", link app.css, div id=app with attractive loading placeholder, div id=blazor-error-ui with reload link, script src="_framework/blazor.webview.js" autostart="false", script src="drag.js". Blazor error UI hidden normally.

Implement these 4 files directly. Do not build or touch others. Summarize decisions and files written. User explicitly requested Claude Opus to own graphics and UX.
