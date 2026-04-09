# 障害対応 WebView2サムネ契約 GDI枯渇抑制 2026-04-09

## 背景

- 例外表面は `Dispatcher.SetWin32Timer` / `DispatcherTimer.Start()` だった。
- ただし `debug-runtime.log` では `user_objects=41-42` に対して `gdi_objects=10000` に張り付いていた。
- つまり `DispatcherTimer` 自体が主因ではなく、GDI 枯渇の結果として WPF 内部タイマー確保が失敗していた。
- 4/7 時点で導入済みの WebView2 スキン経路では、`WhiteBrowserSkinThumbnailContractService` が `wb.update` / `wb.getInfo` ごとにサムネイル寸法を再解析しており、同一画像への連続アクセスが多かった。

## 今回の対応

- `WhiteBrowserSkinThumbnailContractService` に、`サムネ絶対パス + sourceKind + file stamp` 単位のサイズ情報キャッシュを追加した。
- 画像サイズ取得を `System.Drawing.Image.FromStream(...)` から `BitmapDecoder.Create(...)` へ置き換え、GDI 依存を減らした。
- `ThumbInfo.GetThumbInfo(...)` が失敗した時も、幅高さだけ拾って契約生成を継続するようにした。

## 期待効果

- WebView2 スキン更新ごとの GDI/WIC churn を減らす。
- 同一サムネイルを何度も再解析しないため、一覧更新時の体感テンポ悪化も抑えやすい。
- サムネイル更新後は file stamp 差分で再解析されるため、古い寸法情報は残り続けない。

## 確認

- `dotnet build IndigoMovieManager.csproj -c Debug -p:Platform=x64` 成功。
- `WhiteBrowserSkinThumbnailContractServiceTests` 向けの追加テストは作成済み。
- ただし `dotnet test Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WhiteBrowserSkinThumbnailContractServiceTests"` は、既存の `Tests/IndigoMovieManager.Tests/SearchServiceTests.cs` 構文エラーで途中停止した。
