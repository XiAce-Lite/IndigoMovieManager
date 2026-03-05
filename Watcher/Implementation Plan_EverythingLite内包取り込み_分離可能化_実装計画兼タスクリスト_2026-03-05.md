# Implementation Plan + tasklist（EverythingLite内包取り込み・技術名リネーム・分離可能化 2026-03-05）

## 0. 背景
- 現状の `IndigoMovieManager_fork.csproj` は `..\MyLab\EverythingLite\EverythingLite.csproj` を直接参照している。
- そのため、ローカル開発・CIの両方で `MyLab` 同時配置（同時checkout）が前提になっている。
- 既存 `EverythingLiteProvider` は `EverythingLite` のコア型（`FileIndexService` など）に依存しており、UIフォーム実装自体は本体アプリで未使用。
- ただし `EverythingLite` という名前は実装技術（USNジャーナル/MFT + 標準FS）を表しておらず、取り込み後の命名混乱要因になっている。

## 1. 目的
- `EverythingLite` を `IndigoMovieManager_fork` リポジトリ内に取り込み、単独でビルド/テスト可能にする。
- 取り込み後に実装名を技術ベースへ統一し、内包名称を `UsnMft` 系へ置換する。
- 将来の再分離（別repo切り出し）に備え、境界・同期手順・参照切替手段を事前に固定する。
- 既存の `IFileIndexProvider` 契約・reasonコード契約・A/B比較観点を壊さない。

## 2. 課題（現状）
1. 参照が相対パス依存のため、作業環境ごとに前提がぶれやすい。
2. CIが `MyLab` checkout 必須で、パイプラインの失敗点が増える。
3. `EverythingLite` のUI層とコア層が同居しており、取り込み範囲が曖昧。
4. `EverythingLite` という名称が「Everything互換軽量版」と誤解されやすく、技術実体（USN/MFT）とズレる。
5. 再分離時の公式手順（どのファイルをどちらへ戻すか）が未定義。

## 3. 方針（結論）
- `IndigoMovieManager_fork` 内に **取り込みステージ** を用意し、まずコアを取り込む。
- 取り込み対象はコア層のみ（`FileIndexService` 系・Backend系・契約DTO）とし、`MainForm`/`Program` などUI層は対象外にする。
- 取り込み完了後に、内包名称を技術名ベースへリネームする。
  - 採用名（本計画）: `IndigoMovieManager.FileIndex.UsnMft`
  - 例: プロジェクト `IndigoMovieManager.FileIndex.UsnMft.csproj`、名前空間 `IndigoMovieManager.FileIndex.UsnMft`
- 参照は「内包版を既定、外部版へ戻せる」切替を設ける（MSBuildプロパティで選択）。
- 分離可能性を担保するため、ファイル対応表と同期手順（import/export）をドキュメント化する。
- 既存設定キー `everythinglite` は互換のため当面維持し、内部実装名だけ置換する。

## 4. スコープ
- IN
  - 内包用コアプロジェクトの追加（最終名: `IndigoMovieManager.FileIndex.UsnMft`）
  - `IndigoMovieManager_fork.csproj` の参照切替対応（内包/外部）
  - `scripts/run_fileindex_ab_ci.ps1` と GitHub Actions の依存解消
  - 取り込み後リネーム（プロジェクト名/名前空間/型名の技術名統一）
  - 境界定義ドキュメント（含有/除外/同期手順）追加
- OUT
  - `EverythingLite` WinFormsアプリ（UI）自体の移植
  - WhiteBrowser DB（`*.wb`）仕様変更
  - `EverythingProvider` 側の契約変更

## 5. 実装タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| ELI-INTEG-001 | 完了 | 取り込み境界（IN/OUT）とファイル対応表を定義 | `Watcher/EverythingLite内包境界定義_2026-03-05.md` | 取り込み対象/除外対象/再分離時の戻し先が一覧化されている |
| ELI-INTEG-002 | 完了 | コアソースを取り込み、内包版へ反映 | `src/IndigoMovieManager.FileIndex.UsnMft/*`（新規） | 同期スクリプト経由で取り込み可能な状態 |
| ELI-INTEG-003 | 完了 | 取り込み後に技術名へリネーム（`UsnMft`） | `src/IndigoMovieManager.FileIndex.UsnMft/*`（新規） | プロジェクト名/名前空間/主要型名が技術名へ統一される |
| ELI-INTEG-004 | 完了 | 参照切替（内包既定、外部任意）をMSBuildで実装 | `IndigoMovieManager_fork.csproj` | 既定ビルドは内包版、プロパティ指定で外部版へ切替可能 |
| ELI-INTEG-005 | 完了 | `EverythingLiteProvider` の参照前提を整理（必要最小変更） | `Watcher/EverythingLiteProvider.cs`, `Watcher/FileIndexLiteAlias.cs` | 互換キー維持のまま内部参照先だけ置換される |
| ELI-INTEG-006 | 完了 | A/Bテスト用スクリプトの `MyLab` 必須前提を解消 | `scripts/run_fileindex_ab_ci.ps1` | `MyLab` 非配置でも既定経路で実行可能 |
| ELI-INTEG-007 | 完了 | CIワークフローから `MyLab` checkout 依存を外す | `.github/workflows/fileindex-ab-tests.yml` | 単一repo checkout でA/B対象テストが走る |
| ELI-INTEG-008 | 完了 | 再分離向けの同期手順を追加 | `scripts/everythinglite_sync.ps1`（新規） | import/exportの実行手順が1手順書に固定される |
| ELI-INTEG-009 | 完了 | 回帰確認と結果記録 | 本計画書 追記欄 | `everything`/`everythinglite` の基本シナリオで退行なしを確認 |

## 6. 設計メモ（分離可能性の担保）
- 取り込み直後は差分追跡のため原名維持、その後 `UsnMft` へ改名する2段階で進める。
- 内包最終名は `IndigoMovieManager.FileIndex.UsnMft` とし、技術内容が名称で分かるようにする。
- 設定値やテスト名など外部契約（`everythinglite` キー）は互換のため段階的に扱う。
- UI依存ファイル（`Program.cs`, `MainForm.cs`, `AppUserSettingsStore.cs`, `App.config`）は内包対象から除外する。
- 将来再分離時に備え、以下を固定する。
  - ファイル単位の対応表（内包先 <-> 外部repo先）
  - 参照切替フラグの運用ルール（既定値、CI値、ローカル上書き方法）
  - 同期時の差分確認手順（`git diff` 対象パス固定）

## 7. 受け入れ基準（DoD）
- `IndigoMovieManager_fork` 単独で `UsnMft` 内包実装を含めてビルド可能。
- 既存の `EverythingLiteProviderTests` / `FileIndexProviderAbDiffTests` が成功または妥当なスキップ理由で完了。
- `MyLab` 非配置時でも既定のCIスクリプトが失敗しない。
- 外部版切替モードを有効にした場合に、従来の `..\MyLab\EverythingLite` 構成でもビルド可能。
- 命名確認で `EverythingLite` は互換キー用途以外に新規追加されていない。

## 8. 手動回帰観点
1. 設定 `FileIndexProvider=everythinglite` で監視実行し、動画候補収集が成立する。
2. `FileIndexProvider=everything` に戻して同等シナリオを確認し、切替で機能退行がない。
3. `watch-check` ログの reason 形式（`ok:*`, `*_error:*` など）が従来契約どおりである。
4. ルート切替を繰り返しても `EverythingLiteProvider` のキャッシュ関連テストが成立する。
5. 新規内包コードのプロジェクト名・名前空間が `UsnMft` 系で統一されている。

## 9. 検証コマンド（予定）
- 本体ビルド
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~EverythingLiteProviderTests|FullyQualifiedName~FileIndexProviderAbDiffTests|FullyQualifiedName~FileIndexReasonTableTests"`

## 10. リスクと対策
- リスク: 取り込み後リネームで差分追跡が困難になる
  - 対策: 「取り込みコミット」と「リネームコミット」を分離して履歴を明確化する。
- リスク: 内包時に外部版との実装差が拡大し、再分離コストが上がる
  - 対策: 対応表 + 同期手順 + 切替ビルドを運用に組み込む。
- リスク: UI層まで取り込んで依存が肥大化する
  - 対策: コア層限定取り込みを明文化し、レビューで除外対象をチェックする。
- リスク: CI移行時にA/B回帰網が欠落する
  - 対策: 既存フィルタ（`EverythingLiteProviderTests` 等）を維持したままワークフローのみ置換する。

## 10.1 実行メモ（2026-03-05）
- 取り込み/リネーム実施:
  - `scripts/everythinglite_sync.ps1 -Mode Import` でコア9ファイルを内包へ同期。
  - 同期時に `namespace EverythingLite` -> `namespace IndigoMovieManager.FileIndex.UsnMft` へ変換。
- ビルド:
  - 1回目: `MSBuild ... /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1` は `project.assets.json` 未生成で失敗。
  - 2回目: `MSBuild ... /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1` は成功（警告2 / エラー0）。
- テスト:
  - `dotnet test ... --filter "EverythingLiteProviderTests|FileIndexProviderAbDiffTests|FileIndexReasonTableTests"` は成功（11 passed / 1 skipped / 0 failed）。
  - `pwsh -NoProfile -File .\scripts\run_fileindex_ab_ci.ps1 -Configuration Debug -Platform x64` は成功（内包版既定で完走）。
  - `pwsh -NoProfile -File .\scripts\run_fileindex_ab_ci.ps1 -Configuration Debug -Platform x64 -UseExternalEverythingLite -MyLabRoot C:\Users\na6ce\source\repos\MyLab` は成功（外部版切替でも完走）。
