# Release準備メモ v1.0.4.0 2026-04-25

最終更新日: 2026-04-25

## 目的

v1.0.4.0 公開前に、version、利用者向け文面、ローカル検証の現在地を固定する。

## 現在の結論

- app version は `1.0.4.0` へ更新済み
- installer の MSI upgrade 判定を考慮し、`1.0.3.x` の4桁目更新ではなく先頭3桁を進める
- GitHub Release 本文案は `Docs/forHuman/GitHubRelease/GitHubRelease本文_v1.0.4.0_2026-04-25.md` に作成済み
- README の最近の更新へ `v1.0.4.0` を追記済み
- 本番 tag push 前に GitHub Actions preview と手動スモークを行う

## ローカル検証結果

### 成功

- `dotnet msbuild IndigoMovieManager.sln /t:Build /p:Configuration=Release /p:Platform=x64 /m`
  - 成功
- `dotnet test Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false --filter "FullyQualifiedName~AppDispatcherTimerExceptionPolicyTests"`
  - 5件成功
- `dotnet test Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false --filter "FullyQualifiedName~WhiteBrowserSkinRuntimeBridgeIntegrationTests"`
  - 単独実行では191件成功

### 注意

- Release full test は `1355 passed / 261 failed / 4 skipped`
- 失敗の大半は WebView2 / skin 統合テストの timeout 系
- `WhiteBrowserSkinCompatScriptIntegrationTests` は Debug / Release の単独実行でも 7件失敗
- `WhiteBrowserSkinRuntimeBridgeIntegrationTests` は単独実行では成功したが、広めの同時フィルタでは1件 timeout

## 修正済み

- `App.ShouldSuppressKnownDispatcherTimerWin32Exception(...)` が `SetWin32Timer` 文字列だけで握り潰していたため、`DispatcherTimer.Start` 経路まで見えている場合に限定した
- これにより `SetWin32Timer` 単独 stack は握り潰さない契約へ戻した

## 公開前に見る項目

- GitHub Actions preview を `private_engine_release_tag=v1.0.3.6-private.1` など既存のPrivate Engine release assetで通す
- app tag `v1.0.4.0` と engine tag がズレる場合は、本番 tag 実行前に `PRIVATE_ENGINE_RELEASE_TAG` の一時指定が必要か確認する
- 手動スモークで次を確認する
  - 起動
  - DBを開く
  - 検索
  - Watcher取り込み
  - Playerタブ再生
  - WebView2動画再生
  - 音量変更
  - 左ドロワー開閉
  - skin切り替え
  - 終了

## 判断

現時点で Release build は通るが、WebView2 / skin 統合テストに既知の失敗が残る。
本番 tag push は、GitHub Actions preview と手動スモークで問題が出ないことを確認してから行う。
