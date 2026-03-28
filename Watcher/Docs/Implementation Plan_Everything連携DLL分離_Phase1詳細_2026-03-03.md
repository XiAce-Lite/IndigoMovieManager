# Implementation Plan（Everything連携DLL分離 Phase 1 詳細）

## 0. Phase1で変更予定のコードファイルリスト
- 方針:
  - Phase1は「契約固定」が目的のため、原則ドキュメント更新のみとする。
  - コード変更が必要になった場合でも、既存挙動を変えない最小差分に限定する。
- 変更候補（最小）:
  - `Watcher/EverythingFolderSyncService.cs`
    - reasonコード抽出元として参照し、必要時のみ定数参照化の下準備を行う。
  - `Watcher/MainWindow.Watcher.cs`
    - `DescribeEverythingDetail` のreason解釈を契約と突合し、必要時のみ文言解釈テーブルを整理する。
  - `MainWindow.xaml.cs`
    - `ShouldRunEverythingWatchPoll` など呼び出し境界の確認用に参照し、必要時のみFacade導入準備の最小修正を行う。

## 1. 目的
- Phase 1 のゴールである「契約固定」を、実装前に完了させる。
- Phase 2（EverythingProvider + Facade 実装）で迷わないよう、reasonコードとAPI境界を確定する。

## 2. スコープ
- 対象
  - reasonコード互換表の作成
  - `IFileIndexProvider` 契約の確定
  - `IndexProviderFacade` の責務定義
  - フォールバック条件の明文化
- 非対象
  - 実コード移植
  - `ScratchProvider` 実装
  - UI文言の変更

## 3. 成果物
- `Watcher/Everything_reason_code契約_2026-03-03.md`
- `Watcher/Everything_DLL_API案_2026-03-03.md`
- `Watcher/Everything_フォールバック条件表_2026-03-03.md`

## 4. タスクリスト（Done定義つき）

### T1: 現行reasonコード抽出
- [x] `EverythingFolderSyncService` と `DescribeEverythingDetail` から現行reasonコードを全抽出する。
- Done定義:
  - 重複排除済み一覧が作成され、コード出典（メソッド名）付きで列挙されている。

### T2: reasonコード互換ポリシー確定
- [x] 「既存コードは文字列互換固定」「新規追加時の命名規則」を定義する。
- [x] unknown系の扱い（ホスト側でのデフォルト解釈）を定義する。
- Done定義:
  - 互換ルールが文書化され、変更可否の判断基準が1つに定まっている。

### T3: `IFileIndexProvider` API案作成
- [x] `CollectMoviePaths` / `CollectThumbnailBodies` / `CheckAvailability` の入出力を定義する。
- [x] 失敗時の表現（例外を投げるか、reason返却か）を統一する。
- [x] nullable/空集合/時刻の扱い（UTC）を明示する。
- Done定義:
  - 各APIに「入力」「出力」「失敗時挙動」「注意点」が記載されている。

### T4: `IndexProviderFacade` 責務定義
- [x] OFF/AUTO/ONの分岐責務を定義する。
- [x] Provider選択責務と、ホストへ返す情報（strategy/reason）を定義する。
- [x] DB保存や通知生成をFacade責務外と明記する。
- Done定義:
  - Facadeの責務境界が1ページで説明可能な状態になっている。

### T5: フォールバック条件表作成
- [x] 利用不可理由（setting disabled, not available, truncated, exception, path not eligible）を表形式で整理する。
- [x] 期待動作（Everything継続/FSフォールバック）を条件ごとに確定する。
- Done定義:
  - 全条件に対して期待動作と返却reasonが1対1で対応している。

### T6: 親計画との整合確認
- [x] `Implementation Plan_Everything連携DLL分離_棚卸し含有範囲決定_2026-03-03.md` のPhase 1要件と齟齬がないか確認する。
- [x] 差分があれば親計画へ反映する。
- Done定義:
  - 親計画のPhase 1記述と本詳細計画で矛盾がない。

### T7: レビュー完了判定
- [x] 開発者視点で「この契約でPhase 2実装に着手できるか」を確認する。
- [x] 未決事項を `Open Questions` として列挙する。
- Done定義:
  - 未決事項がゼロ、または保留理由と期限が明記されている。

## 5. 受け入れ条件（Phase 1 完了条件）
- reasonコード互換表が完成し、既存コードが全てマッピングされている。
- `IFileIndexProvider` と `IndexProviderFacade` の契約が文書化されている。
- フォールバック条件表により、失敗時挙動が一意に決まる。
- Phase 2実装者が追加設計なしで着手できる。

## 6. 実施順（推奨）
1. T1
2. T2
3. T3
4. T4
5. T5
6. T6
7. T7

## 7. レビュー結果（T7）
- Phase 2着手可否: **着手可**
- 判断理由:
  - reason契約、API案、フォールバック条件表の3点が揃い、実装境界が固定できた。
  - ホスト責務（DB/通知/UI）とFacade責務（選択/判定/reason返却）が分離されている。

## 8. Open Questions（2026-03-03時点）
- `Strategy` は現行互換を優先して `string`（`everything` / `filesystem`）で固定し、enum化はPhase2以降の検討事項とする。
- `CollectThumbnailBodiesWithFallback` の「フォールバック有無」フラグ追加は、Phase2実装時の必要性で判断する。
- 期限:
  - 上記2件は Phase2実装開始レビュー（予定: 2026-03-06）で最終確定する。

## 9. 決定事項（Phase1）
- `CheckAvailability` は同期APIで固定する。
- `CollectMoviePaths` の時刻条件は `DateTime? changedSinceUtc`（UTC）で固定する。
- `IntegrationMode` 判定（OFF/AUTO/ON）はFacade責務に固定し、Providerはmode非依存とする。

<!-- Codex: 変更通知リンク生成のための最小更新 (2026-03-03) -->
