# Rush Hour Garage

**[ブラウザーで遊ぶ](https://memoria-ll.github.io/rush-hour-puzzle/)**

車をスライドして赤い車を右の出口へ出すパズルです。Windows版（**WPF + Blazor Hybrid**）とブラウザー版（**Blazor WebAssembly**）があります。画面・車両グラフィック・ゲームロジックは共通です。Windows版はオフラインで動作し、ブラウザー版はGitHub Pagesで静的サイトとして公開できます。

## ブラウザー版・GitHub Pages

1. このフォルダーの内容をGitHubリポジトリのルートに配置して、`main`（または`master`）へpushします。
2. リポジトリの **Settings → Pages → Build and deployment → Source** で **GitHub Actions** を選びます。
3. **Actions → Deploy web app to GitHub Pages → Run workflow** を実行します。以降はデフォルトブランチへのpushで自動公開されます。
4. 完了後、デプロイ結果に表示されるURL（通常は `https://<ユーザー名>.github.io/<リポジトリ名>/`）を開きます。

ワークフローはゲームのテスト、Releaseビルド、公開ファイルのブラウザー検証を行ってからデプロイします。PRでは検証だけを実行します。デフォルトブランチが`main`・`master`以外の場合は、[ワークフロー](.github/workflows/pages.yml)のブランチ指定を変更してください。

`<base href="./" />`を使う単一画面のため、リポジトリ配下・ユーザーサイト・カスタムドメインで同じ公開ファイルが使えます。初回は.NET WebAssemblyのダウンロードが必要です。起動後のゲーム処理はブラウザー内で行い、バックエンドやAPIキーは不要です。オフライン起動やプレイ状況の保存機能はありません。

### ローカル起動・静的ファイルの生成

ブラウザー版だけならWindows以外でも.NET 8 SDKでビルドできます。

```powershell
dotnet run --project src/RushHour.Web --urls http://localhost:5080

# 静的サイトを dist/web/wwwroot に生成（PowerShell 7でも利用可）
./scripts/publish-web.ps1
# 公開パスを固定したい場合
./scripts/publish-web.ps1 -BasePath /rush-hour-puzzle/
```

PowerShellなしでも `dotnet publish src/RushHour.Web -c Release -o dist/web` で同じ静的ファイルを生成できます。配信対象は **`dist/web/wwwroot`の中身** です。`file://`では起動できないため、確認にはHTTPサーバーを使ってください。例えば `python -m http.server 5080 --directory dist/web/wwwroot` を実行して `http://localhost:5080/` を開きます（標準の相対ベースパス使用時）。

公開物のブラウザーテスト:

```powershell
./scripts/publish-web.ps1
npm ci
npx playwright install chromium
npm run test:web
```

参考: [GitHub Pagesのカスタムワークフロー](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)、[Blazorのベースパス](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-8.0)。

## 起動

`dist/RushHourGarage.exe` をダブルクリックします。exe単体を別のフォルダーへコピーしても遊べます。

Windows 10/11（x64）と Microsoft Edge WebView2 Runtime が必要です。公開版には.NETランタイムを同梱するため、.NET SDKのインストールは不要です。

## 遊び方

- 赤い車を右へドラッグして、盤面の外へ出すとクリアです。
- 車両は向いている方向にだけ動きます。ショベルカーの移動方向も矢印で確認できます。
- 1回のドラッグを1手として数えます。何マス動かしても1手です。
- 車両をクリック／Tabで選択し、矢印キーでも操作できます。Shift＋矢印キーで移動可能な端まで動かせます。
- 「一手戻す」「やり直す」「ヒント」が使えます。
- 盤面サイズと難易度を選び「新しいパズル」を押すと反映されます。
- シードを空欄にするとランダムに生成します。同じサイズ・難易度・整数シードで同じ盤面を再現できます。

標準は6×6。7×7〜12×12に変更でき、8×8以上には幅のあるショベルカーと長いトレーラーが登場します。

| 難易度 | 最短手数（出口への移動を含む） |
| --- | --- |
| やさしい | 3〜5手 |
| ふつう | 6〜10手 |
| むずかしい | 11〜17手 |
| エキスパート | 18手以上 |

## 開発

.NET 8以上をビルドできるSDKを用意し、Windowsで実行します。

```powershell
dotnet build RushHour.sln -c Release
dotnet run --project src/RushHour.Desktop
dotnet run --project tests/RushHour.Tests -c Release -- --full
dotnet run --project tests/RushHour.Tests -c Release -- --sweep
node tests/drag.test.cjs
./scripts/publish.ps1
```

- `src/RushHour.Core`: ルール、盤面生成、探索、操作履歴。
- `src/RushHour.Desktop`: Windowsホスト、Blazor画面、SVG車両、ドラッグ操作。
- `src/RushHour.Web`: ブラウザーホスト、スマートフォン向けレイアウト。DesktopのRazor・CSS・JSをリンクして共用し、WPFプロジェクトには依存しません。
- `tests`: ルール、解の再生、生成した盤面の難易度、ドラッグ操作の検証。

ドラッグ中はDOMのtransformだけを更新し、操作を確定したときにC#が移動を検証します。重い3Dエンジンや常時描画ループは使いません。盤面生成とヒント探索はWindows版ではバックグラウンドで実行し、ブラウザー版では探索を小分けにして画面操作・描画の時間を確保します。

UIと車両グラフィックはClaude Opusに実装を依頼しています。ゲームロジックとWindowsアプリ統合はCodexが担当しています。

生成方式はAstra（xhigh）の助言を反映しています。詳しくは [生成アルゴリズム](docs/algorithm.md) を参照してください。

WPFホストの構成参考: [MicrosoftのBlazor Hybridチュートリアル](https://learn.microsoft.com/ja-jp/aspnet/core/blazor/hybrid/tutorials/wpf?view=aspnetcore-8.0)
