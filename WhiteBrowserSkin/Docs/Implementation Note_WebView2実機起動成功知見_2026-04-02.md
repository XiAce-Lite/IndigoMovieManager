# WebView2実機起動成功知見 実装メモ 2026-04-02

## 目的

- WebView2 外部スキンが、テストでは通るのに実アプリ起動で出ない時の判断材料を残す。
- 今回、実機で起動成功まで持っていけた時の知見を、次回の再発防止へ使える形で固定する。

## 結論

- 実機で最も効いた修正は、`TryNavigateAsync` より前に host control を visual tree へ仮マウントすることだった。
- `ApplySkinByName` 成功時と DB 起動完了時に、外部 skin host refresh を明示的に積むのも重要だった。
- repo 内 sample skin は UTF-8 実ファイルとして扱い、`meta charset="utf-8"` へ揃える必要があった。

## 実機で起きた実害

- `system.skin=SimpleGridWB` で保存されていても、実アプリ起動では画面が外部スキンへ切り替わらなかった。
- `skin-webview` ログが、統合テスト実行時には出るのに実アプリ起動時には出ない時間帯があった。
- `SimpleGridWB` は表示できても、日本語が文字化けしていた。

## 原因

### 1. host を visual tree へ入れる前に WebView2 初期化を始めていた

- `Views/Main/MainWindow.WebViewSkin.cs`
- 実アプリでは、host control を `ExternalSkinHostPresenter` へ載せる前に `TryNavigateAsync` を呼ぶと、
  WebView2 初期化待ちが完了せず、外部スキン表示へ進まないことがあった
- テストでは通るのに実機でだけ止まる代表例だった

### 2. 起動復元経路と設定画面経路で refresh の入口が弱かった

- `WhiteBrowserSkin/MainWindow.Skin.cs`
- `Views/Main/MainWindow.xaml.cs`
- `PropertyChanged` だけに頼ると、タイミング次第で見た目更新が抜けることがあった

### 3. sample skin の文字コード宣言が実ファイルと不一致だった

- `skin/SimpleGridWB/SimpleGridWB.htm`
- `skin/DefaultGridWB/DefaultGridWB.htm`
- repo 上は UTF-8 なのに `charset=Shift_JIS` のままで、正規化前の読込で誤判定していた

## 今回有効だった対策

### 1. 準備前に host を `Hidden` で仮マウントする

- 対象:
  - `Views/Main/MainWindow.WebViewSkin.cs`
- 方針:
  - `ExternalSkinHostPresenter.Content = hostControl`
  - `ExternalSkinHostPresenter.Visibility = Hidden`
  - を先に適用し、その後で `TryNavigateAsync`
- 効果:
  - WPF タブは見せたまま
  - host の実体だけ visual tree にぶら下がる
  - WebView2 初期化待ちが実機でも安定する

### 2. refresh を明示的に積む

- `WhiteBrowserSkin/MainWindow.Skin.cs`
  - `ApplySkinByName` 成功時に `QueueExternalSkinHostRefresh("apply-skin")`
- `Views/Main/MainWindow.xaml.cs`
  - `BootNewDb(...)` 完了時に `QueueExternalSkinHostRefresh("boot-new-db")`
- 効果:
  - 設定画面からの切替
  - DB 自動復元
  - の両方で host 更新が見た目まで届く

### 3. 実機追跡用に `skin-webview` ログを入れる

- `Views/Main/MainWindow.WebViewSkin.cs`
- 実機で見るべきログ:
  - `refresh queued: ...`
  - `host prepare begin: ...`
  - `host presentation: active=... ready=...`
- これがあると、
  - refresh 自体が積まれていない
  - 積まれているが prepare に入れていない
  - prepare は通ったが ready にならない
  を切り分けやすい

### 3.1 2026-04-11 追記: fallback 後の理由も標準ヘッダーで見せる

- `Views/Main/MainWindow.WebViewSkin.cs`
- `Views/Main/MainWindow.WebViewSkin.Chrome.cs`
- `Views/Main/MainWindow.xaml`
- fallback で WPF 一覧へ戻っても、次を見分けられる通知を標準ヘッダーへ出す
  - `WebView2RuntimeNotFound`
  - `SkinHtmlMissing`
  - その他の host 初期化失敗
- 詳細はツールチップと `debug-runtime.log` の `skin-webview` で追う
- 通知からそのまま `再試行` を押せるようにし、Runtime 導入後や skin 配置修正後の再確認を短い往復で済ませる
- 通知からそのまま `debug-runtime.log` を開けるようにし、現物ログへの到達を 1 手で済ませる
- これにより「戻ったこと」だけでなく「なぜ戻ったか」まで実機で即判断しやすくなった

### 4. repo 内 sample skin は UTF-8 宣言へ統一する

- `skin/SimpleGridWB/SimpleGridWB.htm`
- `skin/DefaultGridWB/DefaultGridWB.htm`
- `<meta charset="utf-8">` に揃える
- WhiteBrowser 実物由来 fixture は Shift_JIS でもよいが、
  repo で保守する sample は UTF-8 と明示した方が安全

## 実機で確認した成功サイン

- ログ:
  - `C:\Users\na6ce\AppData\Local\IndigoMovieManager\logs\debug-runtime.log`
- 成功確認で見えた行:
  - `host presentation: active=True ready=True skinRaw='SimpleGridWB' ... reason=boot-new-db`
- この行が出れば、
  - DB 復元
  - skin 解決
  - host 初期化
  - 表示切替
  が一通り通っている

## 実機切り分けチェックリスト

1. 起動している exe が本当に作業中の出力先か
   - `C:\Users\na6ce\source\repos\IndigoMovieManager\bin\x64\Debug\net8.0-windows10.0.19041.0\IndigoMovieManager.exe`
2. 実 DB の `system.skin` が期待値か
3. 出力先 `bin\...\skin\<SkinName>\<SkinName>.htm` があるか
4. `debug-runtime.log` に `skin-webview` が出ているか
5. `active=True ready=True` まで行っているか
6. sample skin が UTF-8 実ファイルなのに Shift_JIS 宣言になっていないか

## 実装と運用の注意点

- 外部 skin host を別トップレベル Window にしない
  - フォーカス、IME、ショートカット、下部タブ連携が難しくなる
- runtime asset は `skin`
  - source / docs は `WhiteBrowserSkin`
- `DefaultSmall` など built-in 予約名は外部 skin 名として使わない
- アプリ起動中は Debug 出力 DLL がロックされる
  - 実機原因調査では、まず通常終了してから build する

## 今回の関連コミット

- `25b8829`
  - 起動時外部 skin host の初期化安定化
- `1c4ef0c`
  - 外部 skin 動作確認用 `SimpleGridWB` 追加
- `7e3c928`
  - sample skin の文字コード宣言を UTF-8 へ修正

## 次回に活かすべき原則

- 「テストで通る」だけでは足りず、実 `MainWindow` 起動と実ログ確認までやって初めて合格
- WebView2 は「見える位置に載ってから初期化する」前提で考える
- sample skin は repo 保守都合を優先して UTF-8、WB 実物 fixture は互換検証都合で Shift_JIS と分ける
- 実機トラブル時は `skin-webview` ログを最初に見る
