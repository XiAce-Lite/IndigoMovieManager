# IndigoMovieManager v1.0.4.0

アセンブリ version は `1.0.4.0` です。

## 主な更新

- Playerタブの動画再生まわりを強化しました
- WebView2による `webm` などの再生、全画面表示、音量同期を安定化しました
- 監視フォルダの取り込み、検索、一覧更新がUI操作を塞ぎにくいように調整しました
- スキン切り替え時の再描画、catalog再利用、保存分離を進めました
- インストーラーの更新判定と起動導線を整理しました

## 体感改善

- 起動直後や監視更新中でも、検索やタブ操作を優先しやすくしました
- Player音量変更時の連続保存をまとめ、細かいUI詰まりを抑えました
- Watcherの仮表示更新を低優先度へ回し、入力と描画を優先しやすくしました
- 左ドロワー表示中のWebView2重なりを避け、Playerタブ操作時の見え方を安定させました

## 使い始め方

- Release の `Assets` には、アプリ本体 ZIP とインストーラーを載せます
- ZIP を使う場合は、展開して `IndigoMovieManager.exe` を起動してください
- インストーラーを使う場合は、`IndigoMovieManager-Setup-v1.0.4.0-win-x64.exe` を実行してください
- 利用だけなら `.NET 8 Desktop Runtime` が必要です
- 開発者は `.NET 8 SDK` が入っていれば、Runtime も含まれています

## WhiteBrowser から移行する場合

- 使いたい `*.wb` をコピーします
- アプリ起動後、コピーした `*.wb` を画面にドロップすると開けます
- 元の WhiteBrowser DB を直接開かず、必ずコピーした `*.wb` を使ってください

## 注意

- `rescue-worker` はアプリ本体 ZIP の `rescue-worker` フォルダへ同梱されています
- GitHub Release 本文の先頭には、同梱 rescue worker の lock summary が自動で追加されます
- 起動しない時は、まず `.NET 8 Desktop Runtime` の有無を確認してください

## 参考

- フォーク版 Releases:
  https://github.com/T-Hamada0101/IndigoMovieManager_fork/releases
- かんたんマニュアル:
  https://github.com/T-Hamada0101/IndigoMovieManager_fork/blob/master/Docs/forHuman/%E7%B0%A1%E5%8D%98%E3%83%9E%E3%83%8B%E3%83%A5%E3%82%A2%E3%83%AB_ReleaseAssets%E3%81%8B%E3%82%89%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89%E3%81%97%E3%81%A6%E4%BD%BF%E3%81%84%E5%A7%8B%E3%82%81%E3%82%8B%E3%81%BE%E3%81%A7_2026-03-29.md
- .NET 8 ダウンロード:
  https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0
