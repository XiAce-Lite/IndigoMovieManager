# Implementation Plan_WiXv6再検討_GitHub連携_VSCode最新前提_2026-04-05

最終更新日: 2026-04-05

変更概要:
- installer 正本を `Inno Setup` から `WiX v6` へ正式に切り替える
- `GitHub Releases API` を使う自己更新要件と、`WiX v6 Burn bundle` の相性を再評価
- `VS Code 最新安定版 + GitHub Pull Requests and Issues 拡張` を前提にした開発運用を明文化
- 旧 `Inno Setup` 計画は履歴扱いとし、本書を installer 正本計画に昇格する

## 1. 目的

- 2026-04-05 時点の公式情報と本線実装を前提に、installer 正本を `WiX v6` へ固定する
- このリポジトリの要件
  - install
  - upgrade
  - uninstall
  - uninstall 時の保持項目選択
  - GitHub Releases API を使う自己更新
  に対して、`WiX v6` をどう安全に段階導入するかを固定する
- 開発体験は `VS Code 最新安定版 + GitHub 連携` を正面入口にする

## 2. 先に結論

結論は次である。

1. installer 正本は `WiX v6 + Burn bundle` へ切り替える
2. v1 は `install / upgrade / uninstall` に絞り、自己更新は v2、保持項目 UI は v3 へ分離する
3. installer は `scripts/create_github_release_package.ps1` が作る verify 済み app package だけを入力にする
4. package 内の `rescue-worker.lock.json` と `privateEnginePackages` を provenance 正本のまま継承する
5. 自己更新の最新版判定正本は引き続き `GitHub Releases API` に置く
6. 開発とレビューの正面入口は `VS Code 最新安定版` と `GitHub Pull Requests and Issues` 拡張にする
7. v1 の install scope は per-user に固定し、`Program Files / per-machine` は保存場所再設計後まで対象外にする
8. 2026-04-05 時点で local proof は `verify 済み app package -> MSI + bundle exe` まで通り、release workflow 接続も着手済みである

要するに、

- `配布エンジン` は `WiX v6`
- `更新判定の正本` は `GitHub Releases API`
- `開発導線` は `VS Code + GitHub`
- `worker / engine package の pin 正本` は既存 app package 内の lock

この 3 本立てがいちばん筋が良い。

## 3. 2026-04-05 時点の公式情報

### 3.1 WiX v6

- FireGiant の公式 release notes では、`WiX v6.0.2` が `2025-08-28` 公開
- 同 docs では、WiX は `MSBuild SDK` として `dotnet build` でビルドできる
- `Using WiX` の公式 docs には、最小 `.wixproj` として `Sdk="WixToolset.Sdk/6.0.2"` の例がある
- 同 docs には、公式 WiX package は署名されており、`nuget.config` で署名検証を要求できるとある
- `About the WiX Toolset` の docs には、`Open Source Maintenance Fee` への参加条件が明記されている

### 3.2 Burn / BA

- FireGiant docs では、`Burn bundle` は chain / bootstrapper application / prerequisite をまとめる top-level 入口
- 同 docs では、`WixStdBA` は stock BA で、install / modify / uninstall / progress / success / failure の各ページを持つ
- `Out-of-process bootstrapper applications` の docs では、WiX repo に C++ の `WixStandardBootstrapperApplication` と C# の `WixBA` 実装例がある
- 同 docs では、managed BA 用の API は `WixToolset.BootstrapperApplicationApi` に整理されている

### 3.3 VS Code 最新安定版

- `Visual Studio Code 1.115` の公式 update ページでは、`Last updated: April 3, 2026`
- よって、2026-04-05 時点の最新安定版は `VS Code 1.115`

### 3.4 GitHub 連携 in VS Code

- VS Code 公式 docs `Working with GitHub in VS Code` では、GitHub 連携の中心は `GitHub Pull Requests and Issues` 拡張
- 同 docs では、clone / issue / PR 作成 / review / merge を VS Code 内で扱える
- Copilot coding agent docs では、同じ拡張から issue を `@copilot` に委譲し、PR と session log を VS Code 側で追える

## 4. なぜ Inno より WiX v6 へ寄せるのか

### 4.1 upgrade / repair / prerequisite が本題だから

今回ほしいのは単なる `setup exe 化` ではない。

- `.NET Desktop Runtime` 前提
- upgrade 時に旧版を安全に置き換える
- silent update で上書き更新する
- 更新後に自動再起動する
- uninstall 時に保持項目を選ぶ

この要件は、単発 installer より `installer engine + bundle orchestration` が強い。
`Burn` はまさにそこが守備範囲である。

### 4.2 自己更新要件と bundle が噛み合う

ユーザー要件は次である。

- アプリが GitHub Releases API を確認
- 新版 `Setup.exe` を裏でダウンロード
- OK で自分は終了
- setup が silent で上書き
- 自動再起動

この時、setup 側に `upgrade の責務` をきちんと持たせた方が強い。
`WiX bundle` は install / upgrade / prerequisite をまとめて扱えるため、自己更新の apply 側と噛み合う。

### 4.3 VS Code + GitHub で完結しやすい

`WiX v6` は SDK-style project で `dotnet build` できる。
つまりこの repo では、

- VS Code で編集
- GitHub PR で review
- GitHub Actions で build / release

の既存導線に自然に乗る。
`Visual Studio 固定` にしなくてよい点が大きい。

## 5. このプロジェクトでの採用形

### 5.1 採用形の結論

v1 は次で進める。

- `MSI`: verify 済み app package の payload を install 管理する
- `Burn Bundle EXE`: prerequisite / upgrade の正面入口
- `GitHub Releases API`: 将来の自己更新 v2 で最新版判定と download metadata の正本に使う
- `UpdateApplyBridge`: v2 以降で running app から bundle exe へ責務を渡す小さな別プロセスとして追加する

v1 に含めるもの:
- install
- upgrade
- uninstall
- package 内 lock/pin の継承
- ZIP と bundle exe の release 併存

v1 に含めないもの:
- 背景 download
- silent apply 自動更新
- custom managed BA の保持項目 UI
- uninstall 時の保持項目選択

v1 の install scope:
- `%LOCALAPPDATA%\Programs\IndigoMovieManager` 系の per-user install を第一候補にする
- `Program Files / per-machine` は、`Thumb / layout.xml / bookmark` などの保存場所再設計が済むまで採らない
- per-user harvest は ICE38 / ICE64 と衝突するため、v1 は `SuppressValidation=true` の暫定運用で downstream proof を優先する

### 5.2 installer 入力境界

- installer は `scripts/create_github_release_package.ps1` が出した verify 済み app package directory を唯一の入力にする
- setup / bundle 用の再 publish 導線は作らない
- 同 package 内の `rescue-worker.lock.json` と `privateEnginePackages` を、installer 側でも provenance 正本として継承する
- `workerExecutableSha256` と package version は app package 側の verify 結果をそのまま使う
- WiX 側は Private worker / Private packages を自分では取りに行かず、同期済み app package の後段だけを担う

理由:
- Public 本線はすでに `Private sync -> verify 済み app package -> release` で live 成功している
- installer だけ別 staging を持つと、worker / engine package pin の追跡が二重化する
- installer は `配布形態の追加` に留め、pin / provenance の正本は既存 package に寄せた方が管理しやすい

### 5.3 使わないもの

v1 では次を正本にしない。

- `Inno Setup`
- HTML スクレイピングによる release 判定
- Visual Studio 専用 wizard 依存の WiX authoring
- `WixStdBA` の update feed だけに依存した更新判定

理由:
- 更新正本は GitHub Releases API に 1 本化したい
- 開発環境を VS Code 最新系で揃えたい

## 6. GitHub Releases API と WiX の責務分離

ここは混ぜない方が強い。

### 6.1 GitHub 側の責務

- latest release を返す
- `prerelease=false` の stable release を正本にする
- asset metadata を返す
  - `name`
  - `browser_download_url`
  - `size`
  - `digest`

補足:
- GitHub の release / asset API サンプルには `browser_download_url` と `digest` が含まれている
- したがって v2 の自己更新では、まず GitHub asset metadata の `digest` を使う方針でよい
- もし live で不足があれば、その時だけ repo 側 `update-manifest.json` を追加する

### 6.2 WiX 側の責務

- install
- upgrade
- uninstall
- prerequisite
- v1 では package lock/pin を壊さず bundle / MSI へ包む
- v2 以降で silent apply
- v2 以降で reboot / restart の最終制御

### 6.3 アプリ側の責務

- v1 では installer 本体に直接依存しない
- v2 以降で GitHub Releases API を確認する
- v2 以降で背景 download / digest 検証を行う
- v2 以降で `更新しますか？` の最終確認、`UpdateApplyBridge` 起動、自分自身の終了を担う

### 6.4 既存 lock / pin の継承

- installer は package 直下の `rescue-worker.lock.json` をそのまま同梱する
- `rescue-worker.lock.json` に入る `privateEnginePackages` もそのまま残す
- bundle / setup 側で worker / engine package の provenance を再定義しない
- 更新診断時も、まず package 内 lock を見てから bundle metadata を補助的に見る

結論:
- `更新があるかを決める` のは GitHub API
- `更新を適用する` のは WiX bundle
- `worker / engine package が何であるかを決める` のは既存 app package 内 lock

## 7. uninstall 保持選択をどう実装するか

ここが `WiX v6` 採用時の本丸である。

### 7.1 現実的な進め方

最初から custom BA 全実装に行くのは重い。
したがって 2 段で進める。

1. `Phase 1`
   - `Bundle + WixStdBA + MSI` で install / upgrade / silent apply を通す
2. `Phase 2`
   - custom managed BA へ切り替え
   - uninstall 時の `保持項目選択 UI` を入れる

### 7.2 custom managed BA を使う理由

保持項目選択はアプリ固有要件である。

- `Thumb`
- `layout.xml`
- `%LOCALAPPDATA%\{AppIdentity}\logs`
- `%LOCALAPPDATA%\{AppIdentity}\QueueDb`
- `%LOCALAPPDATA%\{AppIdentity}\FailureDb`
- `%LOCALAPPDATA%\{AppIdentity}\RescueWorkerSessions`
- `%LOCALAPPDATA%\{AppIdentity}\WebView2Cache`
- `user.config`

これを安全に出し分けるには、stock UI より custom UI の方が筋が良い。

### 7.3 削除ポリシー

削除対象は従来どおり `アプリ所有だと断言できる場所` に限定する。

常に触らないもの:
- `.wb`
- 外部 `thum`
- WhiteBrowser 同居 DB の `thum`
- 外部 bookmark
- 外部 player / 外部 tool

v1 の明示方針:
- v1 の uninstall は標準 uninstall の成立確認を優先する
- `Thumb / layout.xml / LocalAppData / user.config` の保持 UI はまだ入れない
- install-managed file と runtime-generated file の分離、および保持選択は v3 custom managed BA で扱う

## 8. v2 自己更新の採用形

### 8.1 update check

- 起動時はバックグラウンドで check
- 手動 `更新ボタン` は即時 check
- 同じ `UpdateCheckService` を使う

### 8.2 download

- `%LOCALAPPDATA%\{AppIdentity}\Updates\pending`
へ bundle exe を保存
- `*.partial` -> 完了後 rename
- GitHub asset `digest` で検証

### 8.3 apply

- 本体は `UpdateApplyBridge` を起動
- bridge は親 PID を受け取って終了待ち
- その後、bundle exe を silent mode で起動
- 成功後に本体を再起動

v1 との差:
- v1 で必要なのは installer engine として quiet 実行が成立することまでである
- GitHub Releases API 連携、background download、`UpdateApplyBridge`、自動再起動は v2 から入れる

### 8.4 なぜ bridge が必要か

running app 自身では

- 自分の終了後
- setup 完了待ち
- 再起動

を安定して扱いにくい。
ここは別プロセスへ責務を逃がす。

## 9. VS Code 最新 + GitHub 連携の採用形

### 9.1 標準開発環境

- `VS Code 1.115` を標準とする
- 必須拡張:
  - `GitHub Pull Requests and Issues`
- 推奨:
  - GitHub Copilot / coding agent 連携

### 9.2 この構成でやること

- `wixproj` / `wxs` / `theme` / BA の C# を VS Code で編集
- PR 作成、review、issue 参照は VS Code 内で回す
- 重い定型作業は GitHub issue -> `@copilot` 委譲も選べる

### 9.3 リポジトリ構成

候補:
- `installer/wix/IndigoMovieManager.Bundle.wixproj`
- `installer/wix/Bundle.wxs`
- `installer/wix/Product.wxs`
- `installer/wix/Bootstrapper/`
- `installer/wix/Themes/`

ポイント:
- VS 専用 `.wixproj` wizard に寄せず、SDK-style を正本にする
- `dotnet build` で CI / ローカル双方を揃える

## 10. セキュリティ方針

### 10.1 WiX package trust

- FireGiant docs に従い、WiX package 署名検証を有効にする
- `nuget.config` で `signatureValidationMode=require` を検討する

### 10.2 update asset trust

- GitHub asset `digest` を検証する
- digest 不一致の bundle は実行しない
- 手動更新時だけユーザーへ明示し、起動時 check は log 主体にする

## 11. 運用前提

`WiX v6` 採用はこの計画で決定とする。運用上は次を継続確認する。

1. `Open Source Maintenance Fee` を含む運用条件が将来変わった時は再評価する
2. その時も、installer 入力境界は `verify 済み app package 1 本` を維持する
3. もし WiX 継続不能になっても、自己更新責務分離と lock/pin 継承設計は流用できるように保つ

## 12. フェーズ計画

### Phase 0: 方針固定

やること:
- installer 正本を `WiX v6` に切り替える
- 旧 Inno 計画を履歴扱いにする
- `verify 済み app package を唯一の入力にする` を固定する

完了条件:
- docs 上で方針衝突がない

### Phase 1: WiX installer v1

やること:
- SDK-style `.wixproj`
- MSI 1 本
- Bundle 1 本
- `.NET Desktop Runtime` prerequisite
- `scripts/create_github_release_package.ps1` が出す verify 済み package dir を入力にする
- package 内 `rescue-worker.lock.json` と `privateEnginePackages` をそのまま継承する
- per-user install 前提で install / uninstall / upgrade を通す
- ローカル install / uninstall / upgrade

完了条件:
- `dotnet build` で bundle exe が出る
- クリーン環境で install / upgrade が通る
- package 内 lock/pin が setup 導線でも壊れない
- `Program Files / per-machine` 前提の保存場所事故を持ち込まない

現状:
- 2026-04-05 に `installer/wix` の SDK-style 骨格を追加した
- 2026-04-05 に `scripts/create_wix_installer_from_release_package.ps1` を追加し、verify 済み app package を唯一入力に `MSI + bundle exe` を作る local proof を通した
- 2026-04-05 に v1 は `SuppressValidation=true` で per-user harvest の ICE38 / ICE64 を暫定抑止する方針を固定した
- `.NET Desktop Runtime` prerequisite はまだ未了である

### Phase 2: GitHub release 連携

やること:
- GitHub Actions で bundle exe を release asset に載せる
- asset naming を固定する
- app ZIP は継続
- 既存 app package から bundle 生成する導線を workflow に固定する

完了条件:
- tag release で `zip + bundle exe` が揃う
- release asset が既存 package の lock/pin を壊さない

現状:
- 2026-04-05 に `github-release-package.yml` へ WiX installer 作成 step を追加した
- 2026-04-05 に `invoke_release.ps1` も app package 後に WiX bundle exe を作るよう更新した
- 2026-04-05 に preview run `23995516296` で `github-release-installer` artifact の live 成功を確認した
- tag release で WiX bundle exe を GitHub Release asset へ載せる本番 proof はまだ未了である

### Phase 3: v2 自己更新

やること:
- `UpdateCheckService`
- `UpdateDownloadService`
- `UpdateApplyBridge`
- `更新ボタン`
- 起動時バックグラウンド確認

完了条件:
- GitHub Releases API -> download -> silent apply -> restart が通る

### Phase 4: v3 custom managed BA

やること:
- uninstall 時の保持項目 UI
- app 専用の文言
- 保持 ON/OFF に応じた cleanup 制御

完了条件:
- `サムネイルを残す` を含む保持選択 uninstall が成立する

## 13. タスクリスト

- [ ] Task 1: 旧 Inno 計画を履歴扱いへ更新する
- [x] V1 Task 1: `installer/wix` の SDK-style 骨格を追加する
- [x] V1 Task 2: `WixToolset.Sdk/6.0.2` 前提の MSI / bundle PoC を作る
- [ ] V1 Task 3: `.NET Desktop Runtime` prerequisite の導線を入れる
- [x] V1 Task 4: verify 済み app package dir を WiX 入力へ固定する
- [x] V1 Task 5: package 内 `rescue-worker.lock.json` と `privateEnginePackages` の継承方法を固める
- [x] V1 Task 6: bundle exe の release asset naming を固定する
- [x] V1 Task 7: GitHub Actions と release 手順 doc を WiX v1 版へ更新する
- [ ] V2 Task 1: GitHub Releases API client を実装する
- [ ] V2 Task 2: asset `digest` 検証付き download を実装する
- [ ] V2 Task 3: `UpdateApplyBridge` を実装する
- [ ] V2 Task 4: app 終了 -> silent bundle apply -> 再起動を通す
- [ ] V3 Task 1: custom managed BA の骨格を追加する
- [ ] V3 Task 2: uninstall 保持項目 UI を実装する

## 14. リスク

### 14.1 custom BA は重い

- uninstall 保持選択まで入れると、PoC より一段重くなる

対策:
- `WixStdBA` で先に bundle 本線を確認し、その後 custom BA へ進む

### 14.2 WiX の built-in update 周りへ寄せすぎると GitHub 正本がぶれる

対策:
- v1 の更新判定は GitHub Releases API に固定する
- WiX の update feed は補助候補に留める

### 14.3 app-owned data cleanup が MSI/Burn 自動削除と衝突する

対策:
- install-managed file と runtime-generated file を分離して扱う
- 削除対象は app-owned path に限定する

### 14.4 WiX 導入条件が運用上合わない可能性

- 公式 docs に `Open Source Maintenance Fee` が明記されている
- 技術的に正しくても、運用条件が合わなければ採用できない

対策:
- 先に適用条件を確認する
- 条件不一致なら Inno 案へ戻せるよう、更新責務分離は docs 上で維持する

## 15. この計画の結論

- 2026-04-05 時点の公式情報で見ると、`WiX v6` は `dotnet build`、`Burn bundle`、`managed BA` の組み合わせが使え、今ほしい upgrade / self-update 要件と相性が良い
- `VS Code 1.115 + GitHub Pull Requests and Issues` を前提にしても無理がない
- installer 正本は `WiX v6` に切り替える
- ただし一気に custom BA へ飛ばず、`v1: install/upgrade/uninstall -> v2: 自己更新 -> v3: custom BA` の順で積むのが安全である
- さらに installer は既存 `create_github_release_package.ps1` の verify 済み package を唯一入力とし、worker / engine package の provenance は package 内 lock を継承するのが本線に合う
- 現行配布の正面入口はまだ ZIP であり、bundle exe はこれから Phase 7 で追加する
