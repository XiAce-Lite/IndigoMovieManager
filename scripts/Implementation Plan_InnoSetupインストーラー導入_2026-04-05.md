# Implementation Plan_InnoSetupインストーラー導入_2026-04-05

最終更新日: 2026-04-05

変更概要:
- `WiX v6` 正式決定に伴い、本書は Inno 版の検討履歴として残し、正本は `Implementation Plan_WiXv6再検討_GitHub連携_VSCode最新前提_2026-04-05.md` を優先する
- Inno Setup を使った旧 installer 案の比較ポイントを整理する
- 既存の ZIP 配布導線を壊さず、`IndigoMovieManager` 名で `setup exe` を追加する方針を固定
- インストーラーは `scripts/create_github_release_package.ps1` が作る verify 済み app package をそのまま包む方針に固定
- アンインストール時に `サムネイル` などの保持項目を選べる要件を、保存先単位で分解
- `install` / `upgrade` / `uninstall` の責務境界と、消してよいデータ・消してはいけないデータを明文化
- 自己更新は installer v1 と切り分け、後続フェーズとして扱う方針へ補正

注記:
- 2026-04-05 の `WiX v6` 再検討以降、installer の正本計画は WiX 側ドキュメントを優先する
- 本書は `Inno Setup` 案の比較・履歴として保持する

## 1. 目的

- ZIP 展開前提の配布に加えて、通常利用者向けの `setup exe` を追加する
- `Public repo で 1 本に束ねて配る` という今の release 方針を維持したまま、Inno Setup で install / uninstall を提供する
- uninstall 時に、`サムネイルを残す` などの保持項目を安全に選べるようにする
- installer v1 は `verify 済み app package を setup exe へ包む` ことに集中し、自己更新は後続フェーズへ切り分ける

## 2. 当時の Inno 案で置いていた要件

当時の前提:
- installer shell 候補として Inno Setup を使う
- install できる
- uninstall できる
- uninstall 時に、残す項目をユーザーが選べる
- 最低でも `サムネイル` は保持選択できる

当時の plan で同時に固定しようとしていた前提:
- ユーザー向け配布物は `1 本の setup exe` にまとめる
- `IndigoMovieEngine` 側は引き続き private artifact / package を publish し、最終的な installer 組み立ては Public repo 側で行う
- 既存の ZIP 配布は消さず、当面は併存させる
- installer は `scripts/create_github_release_package.ps1` が作る app package を唯一の入力とし、setup 用の再 publish 導線は作らない
- setup に含まれる worker / engine package の pin 情報は、package 直下の `rescue-worker.lock.json` を正本とする

今回の plan から切り分ける後続要件:
- ユーザーはアプリ内の `更新ボタン` から更新確認できる
- 起動時にもバックグラウンドで GitHub Releases API を確認できる
- 新版検知後は、最新版の `Setup.exe` をバックグラウンドで自動ダウンロードする
- ダウンロード完了後は `更新しますか？` の確認だけで進める
- OK 時はアプリ本体が自動終了し、`Setup.exe` が silent mode で上書き更新する
- 更新完了後はアプリを自動再起動する

## 3. 現在の保存場所整理

installer / uninstaller 設計で先に固定すべき保存先は次である。

### 3.1 実行フォルダ配下に残り得るもの

- `Thumb\<DB名>\...`
  - 既定サムネイル保存先
  - 根拠: `src/IndigoMovieManager.Thumbnail.Engine/ThumbRootResolver.cs`
- `layout.xml`
  - Dock 配置
  - 根拠: `Views/Main/MainWindow.xaml.cs`
- `bookmark\...`
  - DB 設定次第では外部パスだが、実行フォルダ既定の補助フォルダとして残る可能性はある

### 3.2 `%LOCALAPPDATA%` 配下のアプリ専用データ

- `%LOCALAPPDATA%\{AppIdentity}\logs`
- `%LOCALAPPDATA%\{AppIdentity}\QueueDb`
- `%LOCALAPPDATA%\{AppIdentity}\FailureDb`
- `%LOCALAPPDATA%\{AppIdentity}\RescueWorkerSessions`
  - 根拠: `src/IndigoMovieManager.Thumbnail.Runtime/AppLocalDataPaths.cs`
- `%LOCALAPPDATA%\{AppIdentity}\WebView2Cache`
  - 根拠: `Views/Main/MainWindow.WebViewSkin.cs`
- `user.config`
  - `Properties.Settings` の User スコープ設定
  - 実体パスは exe identity に依存し、固定文字列で決め打ちしにくい
  - 根拠: `Properties/Settings.settings`, `src/IndigoMovieManager.Thumbnail.Engine/AppIdentityRuntime.cs`

### 3.3 アンインストーラーが触ってはいけないもの

- `.wb` 本体
- DB の `system.thum` が指す外部サムネイルフォルダ
- WhiteBrowser 同居 DB の `DBフォルダ\thum\<DB名>`
- DB の `system.bookmark` が指す外部 bookmark フォルダ
- ユーザーが明示設定した外部プレイヤーや外部ツール本体

結論:
- uninstall で選択削除してよいのは、`アプリ所有だと断言できる場所` に限定する
- 外部 DB / 外部 thum / 外部 bookmark は `常に残す` を正本にする

## 4. 当時の採用方針メモ

## 4.1 install 方針

- Inno Setup を installer shell に使う想定だった
- 既存の `scripts/create_github_release_package.ps1` が作る app package を staging input にする
- その staging から Inno Setup script をコンパイルして `setup exe` を作る
- 既定の install 先は `%LOCALAPPDATA%\Programs\IndigoMovieManager` 系の per-user install を第一候補にする
- setup 表示名・asset 名・install dir は `IndigoMovieManager` を正本にし、branch / fork 固有名は焼き込まない

理由:
- 現状は `AppContext.BaseDirectory` 基準の `Thumb` / `layout.xml` / 一部補助ファイル運用がまだ残る
- `Program Files` 既定にすると、書き込み権限・UAC・保持データの扱いが一気に重くなる
- まずは per-user install で、現在の体感テンポと実装前提を壊さず入れる方が安全

## 4.2 uninstall 方針

- uninstall の既定動作は `アプリ本体だけ削除` とする
- 追加データの削除は、明示選択したものだけ行う
- UI は `削除する項目` ではなく、`残す項目` のチェック方式を正本にする
- 既定値は `すべて残す` とし、事故を防ぐ

理由:
- 今回の要求は「残す項目を選べること」であり、安全側既定が合う
- `Thumb` や `user.config` は消すと復元コストが高い
- upgrade 時の旧版アンインストールで誤って消えない設計にもつながる

## 4.3 upgrade 方針

- 同一 `AppId` で上書き upgrade できる形にする
- upgrade 時は `保持項目選択 UI` を出さない
- upgrade 経由の旧版 uninstall では、追加データ削除を一切走らせない

理由:
- version update のたびに `Thumb` や `layout.xml` を消す挙動は論外
- uninstall と upgrade で UI / 削除条件を分けておかないと事故る

## 4.4 後続フェーズの自己更新方針

- 更新確認の入口は 2 つ持つ
  - アプリ内 `更新ボタン`
  - 起動時バックグラウンド確認
- 両方とも同じ `UpdateCheckService` を通し、判定を二重実装しない
- 更新確認は GitHub の Releases API を使い、HTML スクレイピングはしない
- 新版が見つかったら、最新版 release asset のうち `Setup.exe` と update manifest をバックグラウンドで取得する
- 更新適用は、アプリ本体が自分で上書きせず、別プロセスの `UpdateApplyBridge` が担当する
- `UpdateApplyBridge` が
  - 親アプリ終了待ち
  - `Setup.exe` の silent 実行
  - 成功時のアプリ再起動
  を担う

理由:
- 本体プロセスは自分が消えると、その後の setup 完了待ちと再起動制御を握れない
- silent setup 後の再起動まで安定させるには、外側の小さな橋渡しプロセスが必要
- ここを batch や PowerShell に寄せるより、配布物に含める専用 helper の方が事故りにくい

補足:
- これは installer v1 の必須スコープではない
- installer 導入の初回実装では `install / upgrade / uninstall保持` を優先し、自己更新は v2 として分ける

## 5. 保持項目の仕様

uninstall 時に出す保持項目は、v1 では次の 4 群に絞る。

| 項目 | 既定 | 対象 | 備考 |
| --- | --- | --- | --- |
| サムネイルを残す | ON | `{app}\Thumb\**` | 既定サムネだけを対象にする |
| レイアウトと補助ファイルを残す | ON | `{app}\layout.xml`, `{app}\layout.missing-*.xml`, `{app}\bookmark\**` | app 配下だけ対象 |
| ローカルキャッシュとログを残す | ON | `%LOCALAPPDATA%\{AppIdentity}\logs`, `QueueDb`, `FailureDb`, `RescueWorkerSessions`, `WebView2Cache` | アプリ専用フォルダのみ |
| ユーザー設定を残す | ON | `user.config` 系 | 実パスは resolver で解決する |

仕様上の注意:
- `サムネイルを残す=OFF` でも、外部 `thum` は消さない
- `レイアウトと補助ファイルを残す=OFF` でも、外部 bookmark は消さない
- `ユーザー設定を残す=OFF` は、この exe identity に紐づく `user.config` だけを対象にする

## 6. 実装方針

### 6.1 追加するもの

- `scripts/create_inno_setup_installer.ps1`
  - verify 済み app package を入力に受ける
  - Inno Setup コンパイル呼び出し
  - 出力先整理
- `scripts/installer/IndigoMovieManager.iss`
  - installer 本体
- `scripts/installer/IndigoMovieManager.InstallerConstants.iss`
  - version / app名 / exe名 / artifact パスなどの生成定数
- `scripts/installer/README.md`
  - ローカル生成手順

必要なら追加:
- `scripts/installer/UninstallDataResolver.ps1` または小さな helper
  - `user.config` 実体パスの解決
  - AppIdentity ごとの LocalAppData ルート解決

### 6.2 release asset と update manifest

- setup asset は更新クライアントが機械的に見つけやすい安定命名にする
- 追加する release asset 候補:
  - `IndigoMovieManager-Setup-<version>-win-x64.exe`
  - `IndigoMovieManager-update-manifest-<version>-win-x64.json`
- update manifest には最低でも次を入れる
  - `version`
  - `tag`
  - `publishedAt`
  - `setupAssetName`
  - `setupDownloadUrl`
  - `sha256`
  - `size`
  - `minimumSupportedVersion`
  - `appPackageWorkerLockFile`

方針:
- アプリは Releases API で `latest` release を見た後、manifest asset を見つけて setup asset を決定する
- setup exe を直接名前推測だけで拾わず、manifest を正本にする
- auto-download 対象が executable なので、`sha256` 検証を必須にする
- ただし worker / engine package の pin 情報は setup manifest へ再定義せず、package 内の `rescue-worker.lock.json` を正本のまま使う
- setup は verify 済み app package を包むだけとし、worker / engine package の provenance は既存 lock を継承する

### 6.3 既存スクリプトへの追加

- `scripts/create_inno_setup_installer.ps1`
  - `scripts/create_github_release_package.ps1` が出した package dir を受けて setup exe を作る
  - worker / engine package の再 build / 再 publish はしない
- `scripts/create_github_release_package.ps1`
  - Inno 用へ渡しやすい package dir / metadata 出力整理を追加
- `scripts/invoke_release.ps1`
  - ZIP に加えて setup exe を作る導線を追加
- `.github/workflows/github-release-package.yml`
  - windows-latest 上で Inno Setup compiler を入れる step を追加する
  - release asset に `setup exe` を追加
  - `update manifest` を追加
  - ZIP は継続添付

### 6.4 Inno Setup script 側の考え方

- app 本体ファイルは通常どおり uninstall 管理下へ置く
- `layout.xml` のように `保持選択` の対象にしたいファイルは、通常削除に任せず制御可能にする
- user 生成データへ広く効く `UninstallDelete` の乱用はしない
- `保持チェックが外れた項目だけ` を uninstall code で削除する
- silent update 用に、setup はコマンドライン実行を前提に扱う
- silent 実行時の細かい引数セットは実装時に確定するが、少なくとも `ユーザー対話なしで上書き更新できる` ことを完了条件に含める

重要:
- `Thumb` はインストール時に配るファイルではないため、既定では自然に残る
- 逆に `layout.xml` のような配布ファイルは、そのままだと uninstall で消える
- したがって `保持選択したい install-managed file` と `実行後生成データ` を分けて扱う必要がある

### 6.5 v2 自己更新コンポーネント

- `UpdateCheckService`
  - GitHub Releases API を確認する
  - 現在 version と latest version を比較する
- `UpdateDownloadService`
  - setup exe と manifest を `%LOCALAPPDATA%\{AppIdentity}\Updates\pending\` へ保存する
  - 途中ダウンロードの `.partial` を扱い、完了時だけ rename する
  - `sha256` を検証する
- `UpdateCoordinator`
  - 更新ボタンと起動時チェックの入口を統一する
  - 通知状態を管理する
  - `更新しますか？` ダイアログの表示を担当する
- `UpdateApplyBridge`
  - 親プロセス ID を受け取る
  - 親終了を待つ
  - `Setup.exe` を silent mode で起動する
  - setup 成功後にアプリを再起動する

保存先:
- `%LOCALAPPDATA%\{AppIdentity}\Updates\pending`
- `%LOCALAPPDATA%\{AppIdentity}\Updates\applied`
- `%LOCALAPPDATA%\{AppIdentity}\Updates\logs`

## 7. 実装の流れ

### Phase 1: packaging 境界追加

目的:
- 既存 release package の後段に Inno Setup を差し込める形を作る

やること:
- `scripts/create_github_release_package.ps1` が出す package dir を installer 正本入力に固定する
- Inno 用定数ファイル生成を追加する
- ローカルで `ISCC.exe` を叩ける helper を作る
- GitHub Actions 上で Inno Setup compiler を入れる step を決める

完了条件:
- ローカルで `setup exe` が 1 本生成できる
- CI でも同じ package dir から `setup exe` を生成できる
- 既存 ZIP 生成は壊さない

### Phase 2: install 実装

目的:
- setup exe から普通に入れられる状態を作る

やること:
- install dir
- shortcut
- uninstall entry
- 初回起動 exe
- `.NET 8 Desktop Runtime` の確認導線
を script へ入れる

完了条件:
- クリーン環境で install して起動できる

### Phase 3: uninstall 保持選択

目的:
- `残す項目を選べる uninstall` を実現する

やること:
- 保持項目 UI を追加する
- `Thumb`
- `layout.xml`
- LocalAppData 配下
- `user.config`
の削除判定を実装する
- upgrade 時は削除 UI / 削除処理を抑止する

完了条件:
- 明示 uninstall 時だけ保持選択が出る
- 保持 ON のものは残る
- 保持 OFF のものだけ削除される

### Phase 4: release 統合

目的:
- GitHub Release へ ZIP と setup exe を並べる

やること:
- workflow 更新
- release 手順 doc 更新
- 利用者向け手順更新
- setup exe を release asset へ載せる
- 必要なら update manifest はここで追加する

完了条件:
- tag release で ZIP / setup exe が揃う
- setup 生成経路が既存 package の lock/pin を壊さない

### Phase 5: v2 自己更新基盤

目的:
- アプリ内から新版検知、ダウンロード、silent 適用、再起動までを通す

やること:
- GitHub Releases API client を追加する
- update manifest を release asset へ載せる
- setup exe の background download を実装する
- `UpdateApplyBridge` を追加する
- `更新ボタン` と `起動時バックグラウンド確認` を同じ coordinator に寄せる

完了条件:
- 起動中アプリが最新版を検知できる
- setup exe を裏で取り切れる
- OK で本体終了 -> silent setup -> 自動再起動まで通る

## 8. `.NET 8 Desktop Runtime` の扱い

v1 方針:
- まずは現在どおり `runtime 依存 build` を維持する
- installer 起動中に runtime の有無を確認する
- 無ければ案内または bootstrapper 起動へ進める

入れないもの:
- self-contained 化
- runtime の同梱巨大化

理由:
- 今回の主眼は install / uninstall UX の改善であり、配布サイズの大幅増は先にやらない

## 9. テスト観点

- install 後にアプリが起動できる
- `rescue-worker` と `tools\ffmpeg-shared` を含む現在の配布内容で起動できる
- package 内の `rescue-worker.lock.json` と同梱 worker / engine package 情報が、そのまま setup 導線でも残る
- 上書き install で既存 `Thumb` / `layout.xml` / `LocalAppData` が壊れない
- 明示 uninstall で保持 UI が出る
- `サムネイルを残す=ON` で `{app}\Thumb` が残る
- `サムネイルを残す=OFF` で `{app}\Thumb` が消える
- `ローカルキャッシュとログを残す=ON` で `%LOCALAPPDATA%\{AppIdentity}` 配下の対象が残る
- `ユーザー設定を残す=OFF` で対象 exe identity の `user.config` だけ消える
- `.wb` と外部 `thum` / 外部 bookmark が常に無傷である
- upgrade 時に保持 UI が出ず、データ削除も走らない
- v2 範囲:
  - 更新ボタンで latest release を確認できる
  - 起動時チェックが UI 初動を止めない
  - setup exe のダウンロード途中中断時に壊れた exe を採用しない
  - manifest の `sha256` と一致しない setup exe を弾ける
  - `更新しますか？` OK 後に、本体終了 -> silent setup -> 再起動が通る
  - 更新失敗時に、旧版が壊れず理由をログで追える
  - 既に最新版なら通知を過剰に出さない

## 10. リスク

### 10.1 `user.config` の実体パスが固定でない

- exe identity / version / URL hash でパスが揺れる
- Inno script の固定文字列だけで消しにいくと事故る

対策:
- resolver を 1 か所に寄せる
- wildcard を使う場合も、対象 identity の prefix に厳しく絞る

### 10.2 `layout.xml` は install-managed file なので、そのままだと勝手に消える

対策:
- 保持選択対象として明示的に制御する
- uninstall の自動削除へ丸投げしない

### 10.3 upgrade 時の旧版 uninstall で保持データを消す事故

対策:
- `explicit uninstall` と `upgrade uninstall` を分岐する
- upgrade 側では追加データ削除処理を走らせない

### 10.4 実行フォルダ依存がまだ残っている

- `Thumb` と `layout.xml` は `AppContext.BaseDirectory` 前提がある
- ここを無視して `Program Files` 既定へ倒すと権限問題が出やすい

対策:
- まず per-user install を正本にする
- 将来 `Program Files` へ寄せる時は保存場所再設計を別計画でやる

### 10.5 自己更新で exe を自分自身から差し替えようとして詰まる

- 本体単独では、終了後の setup 完了待ちと再起動制御ができない

対策:
- `UpdateApplyBridge` を別プロセスで持つ
- 本体は更新適用直前に責務を bridge へ渡して終了する

### 10.6 GitHub API / ダウンロード失敗で通知がうるさくなる

- 起動のたびにエラー通知すると体感が悪い

対策:
- 起動時チェック失敗は silent log 中心にする
- 手動更新時だけ積極的にエラー表示する
- 再試行間隔と成功時キャッシュを持つ

### 10.7 ダウンロード済み setup exe の改ざん / 取り違え

- executable を自動取得する以上、TLS だけに頼るのは弱い

対策:
- release asset に update manifest を追加し、`sha256` を必ず検証する
- setup asset 名も manifest で固定し、曖昧な pattern match を避ける

## 11. 先に決めること

1. setup exe の表示名を何にするか
2. setup 表示名 / install dir / asset 名を `IndigoMovieManager` へどう統一するか
3. `.NET Runtime` 未導入時を
   - 案内だけにするか
   - 自動起動補助までやるか
4. Desktop shortcut を既定 ON にするか OFF にするか
5. 起動時チェックを毎回行うか、最短間隔を持つか
6. update manifest を installer v1 と同時に入れるか、v2 自己更新で入れるか

おすすめ:
- v1 は `per-user install`
- `保持項目は全部 ON`
- Desktop shortcut は OFF
- runtime は `案内 + 起動補助あり` を第一候補
- installer v1 では自己更新を抱え込まず、setup / uninstall を先に固める

## 12. 参考タスクリスト

- [ ] Task 1: `scripts/create_inno_setup_installer.ps1` を追加する
- [ ] Task 2: `scripts/installer/IndigoMovieManager.iss` を追加する
- [ ] Task 3: Inno 用定数生成ファイルを追加する
- [ ] Task 4: install dir / shortcut / uninstall entry を実装する
- [ ] Task 5: `.NET 8 Desktop Runtime` 確認導線を追加する
- [ ] Task 6: uninstall の保持項目 UI を実装する
- [ ] Task 7: `Thumb` / `layout.xml` / LocalAppData / `user.config` の削除判定を実装する
- [ ] Task 8: upgrade 時に保持 UI を抑止する分岐を実装する
- [ ] Task 9: setup asset の naming と表示名を `IndigoMovieManager` 基準で固定する
- [ ] Task 10: `scripts/invoke_release.ps1` と workflow を setup exe 出力対応へ更新する
- [ ] Task 11: `Docs/forHuman` のダウンロード手順を setup exe 対応へ更新する
- [ ] Task 12: install / upgrade / uninstall の手動確認チェックリストを作る

## 12.1 当時想定していた後続 v2 タスク

- [ ] V2 Task A: update manifest の schema を固定する
- [ ] V2 Task B: GitHub Releases API を叩く `UpdateCheckService` を実装する
- [ ] V2 Task C: background download と `sha256` 検証を実装する
- [ ] V2 Task D: `UpdateApplyBridge` を追加する
- [ ] V2 Task E: `更新ボタン` と `起動時バックグラウンド確認` を同一 coordinator へ寄せる
- [ ] V2 Task F: self-update の手動確認チェックリストを作る

## 13. この計画の結論

- 当時の Inno 案の本命は `verify 済み app package` の後段へ `Inno Setup` を薄く載せ、`uninstall で何を残し、何を消してよいか` を事故らない粒度で固定することであった
- `Thumb` を残せる uninstall は、単純な `setup exe 化` より一段難しい
- だからこそ、`app 所有の場所だけを選択削除する`、`外部 DB / 外部 thum / 外部 bookmark は常に触らない` を最初に固定する
- setup は package 内の既存 `rescue-worker.lock.json` と Private pin 情報をそのまま継承し、worker / engine package の正本を再定義しない
- そこへさらに `自己更新` を載せるなら、`更新確認` と `silent apply bridge` を installer 本体とは別責務で切るのが安全である
- ただし 2026-04-05 時点の正式判断では、installer 正本は WiX v6 側へ移した
