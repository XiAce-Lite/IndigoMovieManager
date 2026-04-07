# Implementation Plan_IndigoReleaseManager導入_2026-04-05

最終更新日: 2026-04-05

変更概要:
- `src/IndigoReleaseManager` の独立 WPF app 実装を開始
- v1 の current scope として、環境カード、3 手順カード、実行ログを先行実装
- Private 側は v1 では local build / publish / package を正面入口にする

## 1. 目的

`IndigoReleaseManager` は、配布専用の薄い orchestration UI である。

役割は次に固定する。

- Private repo の engine / worker release を安全に起動する
- Public repo の preview を安全に起動する
- Public repo の release を安全に起動する
- GitHub URL、アカウント、現在の repo / branch / remote など、配布判断に必要な情報を UI 上で見える化する

重要な前提:

- `IndigoReleaseManager` 自体は build / publish / release の本体ロジックを再実装しない
- 実処理は既存の PowerShell script と GitHub Actions に委譲する
- UI は「順番ミスを減らす」「現在地を見える化する」「結果を追いやすくする」ことに集中する

## 2. 採用理由

今の配布導線は、運用としては安定してきた一方で、人が手順を追うには情報が分散している。

とくに次が分かりにくい。

- Private 側を今回更新する必要があるか
- どの GitHub repo / branch / release tag を見ているか
- preview と release の違い
- Public / Private のどちらで失敗したか

`IndigoReleaseManager` はこの 4 点を UI で見える化し、既存 script 群の正面入口を 3 手順へ束ねる。

## 3. この app の立ち位置

### 3.1 正式な責務

- 開発者向けの配布オーケストレータ
- Public / Private 2 repo の current state 可視化
- 3 手順実行の入口
- run 結果、生成物、GitHub URL の表示

### 3.2 やらないこと

- engine / worker / app の build ロジック再実装
- GitHub token や secret の保存
- lock / pin provenance の再定義
- 一般ユーザー向けランチャー化
- 自己更新
- 配布物の install / uninstall 実行

## 4. 現在の repo / GitHub 情報

2026-04-05 時点の current state を、初期表示の既定値として整理する。

### 4.1 Public repo

- app name: `IndigoMovieManager`
- local path: `%USERPROFILE%\source\repos\IndigoMovieManager`
- current branch: `workthree`
- current GitHub owner account: `T-Hamada0101`
- current GitHub repo name: `IndigoMovieManager_fork`
- origin:
  - `https://github.com/T-Hamada0101/IndigoMovieManager_fork.git`
- upstream:
  - `https://github.com/XiAce-Lite/IndigoMovieManager.git`
- private-probe:
  - `https://github.com/T-Hamada0101/IndigoMovieManager-private-probe.git`
- main release workflow:
  - `.github/workflows/github-release-package.yml`

### 4.2 Private repo

- repo name: `IndigoMovieEngine`
- local path: `%USERPROFILE%\source\repos\IndigoMovieEngine`
- current branch: `main`
- owner account: `T-Hamada0101`
- origin:
  - `https://github.com/T-Hamada0101/IndigoMovieEngine.git`
- main workflows:
  - `.github/workflows/private-engine-build.yml`
  - `.github/workflows/private-engine-publish.yml`

### 4.3 UI に必ず表示する情報

初期画面で常時見えるようにする。

- Public repo 名
- Public local path
- Public current branch
- Public `origin / upstream / private-probe`
- Public GitHub URL
- Private repo 名
- Private local path
- Private current branch
- Private `origin`
- Private GitHub URL
- Public owner account
- Private owner account
- 現在の `private_engine_release_tag`
- 現在の Public preview run URL
- 現在の Public release URL

補足:

- URL と branch は固定文言ではなく、起動時に `git` と既存設定から再取得して表示する
- 計画書に書く値は 2026-04-05 時点の初期既定値であり、将来は app 側で runtime 取得を正本にする

## 5. 実行する 3 手順

`IndigoReleaseManager` は次の 3 手順だけを正面入口にする。

### 5.1 手順1: Private release

v1 の current 実装では、厳密には `Private release 準備` である。

条件:

- Private 側に変更がある時だけ実行

目的:

- engine / worker / package の正本 release 前段となる local build / publish / pack を Private repo で行う
- その結果を Public repo の prepared dir へ同期し、続けて `Public release` へ進める状態を作る

呼び出し先:

- 既存の `build_private_engine.ps1`
- 既存の `publish_private_engine.ps1`
- 既存の `pack_private_engine_packages.ps1`

v1 にまだ含めないもの:

- Private 側 tag 作成
- Private GitHub workflow run URL 取得
- Private GitHub Release URL 表示

UI 入力:

- version
- compatibilityVersion 確認表示
- prerelease / proof 用 suffix

UI 出力:

- Public repo prepared dir の同期先 path
- worker asset 名
- package manifest 名

### 5.2 手順2: Public preview

条件:

- 毎回推奨

目的:

- Private 側成果物を同期し、Public 側 preview と WiX artifact を proof する

呼び出し先:

- `scripts/invoke_github_release_preview.ps1`

UI 入力:

- `private_engine_release_tag`
- `private_engine_run_id`
- preview 対象 branch

UI 出力:

- Public preview run URL
- preview artifact 一覧
- `github-release-package`
- `github-release-installer`
- `github-release-body-preview`

### 5.3 手順3: Public release

条件:

- preview 成功後に実行

目的:

- verify 済み app package と WiX installer を GitHub Release へ公開する

呼び出し先:

- `scripts/invoke_release.ps1`

UI 入力:

- version
- `private_engine_release_tag`
- release branch

UI 出力:

- Public tag
- Public release workflow run URL
- Public GitHub Release URL
- `ZIP`
- `installer.exe`

補足:

- `private_engine_release_tag` を入れた時は、release 実行前に Public 側で worker / package 同期を先に走らせる
- 空欄時は、すでに同期済みの `artifacts/rescue-worker/publish/Release-win-x64` と `artifacts/private-engine-packages/Release` をそのまま使う

## 6. 画面構成

v1 は 1 window で十分である。

### 6.1 上部: 現在地カード

- Public repo card
- Private repo card
- owner / branch / remote / URL
- secret / token の存在確認
- `gh auth` 状態

v1 では、repo / remote / token の current state を先に見せることを優先する。
repo 存在、script 存在、branch 異常の詳細 preflight は、実行時エラーとログで先に返す。

### 6.2 中央: 実行カード

3 つの大きなカードを並べる。

- `1. Private release`
- `2. Public preview`
- `3. Public release`

各カードで、

- 必須入力
- 事前条件
- 実行ボタン
- 実行結果

をまとめて見せる。

### 6.3 下部: ログと成果物

- stdout
- stderr
- 成功 / 失敗の要約
- 生成物 path
- GitHub URL
- コピー用ボタン

## 7. 実装方針

### 7.1 薄い orchestration UI に徹する

アプリ側で持つのは次だけにする。

- 入力値の保持
- script 起動
- 出力の表示
- URL / path の整形
- 事前条件チェック

持たないもの:

- GitHub API の深い再実装
- build / publish / release ロジック
- update 判定ロジック
- worker / package pin 解釈の再実装

### 7.2 既存 script を正本にする

Public 側の正面入口:

- `scripts/invoke_github_release_preview.ps1`
- `scripts/invoke_release.ps1`

Private 側の正面入口:

- 既存 script を束ねる façade script を追加する

理由:

- app の責務を UI orchestration に限定できる
- CLI でも同じ処理を再利用できる
- script 単体テストと UI テストを分けやすい

### 7.3 structured output を足す

管理しやすさのため、script 側には `-EmitJson` か `result.json` 出力を追加する。

最小でも次を返したい。

- `status`
- `step`
- `runUrl`
- `releaseUrl`
- `tag`
- `artifactPaths`
- `message`

v1 の UI は、この JSON を読んで表示を更新する。

## 8. 事前条件チェック

起動時と実行前に、最低限これを見る。

- Public repo が存在する
- Private repo が存在する
- `git` が使える
- `pwsh` が使える
- `gh` の利用可否
- Public branch が想定どおりか
- Private branch が想定どおりか
- 必須 script が存在する
- `INDIGO_ENGINE_REPO_TOKEN` など必須 token が設定済みか

大事な点:

- token の値は表示しない
- 表示は `Configured / Missing` の 2 値だけにする

## 9. 保存する情報

v1 は DB を持たない。

保存は最小にする。

- 最後に使った version
- 最後に使った `private_engine_release_tag`
- 最後に使った branch
- 直近の run URL

保存場所候補:

- `%LOCALAPPDATA%\IndigoReleaseManager\settings.json`

ただし secret は保存しない。

## 10. 置き場所

`IndigoReleaseManager` は開発者用配布ツールなので、Public repo 配下に置く。

候補:

- `%USERPROFILE%\source\repos\IndigoMovieManager\src\IndigoReleaseManager\`

理由:

- Public repo の release / WiX / GitHub 導線と近い
- 一般ユーザー向け app 本体とは別責務で分けやすい
- 将来 solution へ追加しても main app と混ざりにくい

## 11. v1 / v2 / v3

### 11.1 v1

- 現在地カード
- 3 手順ボタン
- script 実行
- ログ表示
- GitHub URL 表示
- path / tag / run URL のコピー

### 11.2 v2

- `result.json` ベースの structured progress
- run 履歴の保存
- 失敗パターンごとの再実行導線

### 11.3 v3

- reviewer / operator 向けの proof checklist 内蔵
- release notes preview の表示
- WiX v2 / v3 の進行状況表示

## 12. タスクリスト

### TASK-001

Private release 用 façade script の責務を確定する

### TASK-002

Public preview / release script に `-EmitJson` もしくは結果ファイル出力を足す

### TASK-003

`IndigoReleaseManager` の環境カードに表示する repo / GitHub 情報取得処理を実装する

### TASK-004

3 手順 UI の最小画面を作る

### TASK-005

stdout / stderr / URL / path を見やすく出す結果ペインを作る

### TASK-006

token / branch / script 存在確認の preflight を作る

### TASK-007

Private release -> Public preview -> Public release の通し proof を取る

## 13. 結論

`IndigoReleaseManager` は、新しい release ロジックを作る app ではない。

既存の

- Private release
- Public preview
- Public release

を、安全に順番どおり実行し、現在地と結果を見える化するための配布専用 app である。

管理しやすさを優先するなら、

- 本体ロジックは script / workflow
- `IndigoReleaseManager` は薄い UI orchestration

に固定するのが最も強い。

## 14. current state

2026-04-05 時点で、次は実装済みである。

- `src/IndigoReleaseManager` の独立 WPF app 追加
- Public / Private repo 情報の再取得
- `gh auth` / token 状態の表示
- `Private local build / publish / pack` ボタン
- `Public preview` ボタン
- `Public release` ボタン
- 実行ログと URL 表示
- `private_engine_release_tag` 指定時の事前同期
- `ReleaseBranch` と current branch の一致確認

まだ後続で詰めるもの:

- Private 本番 release 用 façade script
- `-EmitJson` など structured result
- run 履歴保存
- proof checklist 連携
- 起動時に空の項目
  - `private_engine_release_tag`
  - preview run URL
  - release URL
