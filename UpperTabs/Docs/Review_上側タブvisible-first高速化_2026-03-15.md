# レビュー: Implementation Plan 上側タブ visible-first 高速化

レビュー日: 2026-03-15

対象: UpperTabs/Docs/Implementation Plan_上側タブvisible-first高速化_2026-03-15.md

---

## 全体評価

計画として **着手可能な状態** 🟢

Phase 構成の順序が良い。「decode 最適化 + 非アクティブ停止」を visible-first queue より先に置いた判断は、
既存コードとの差分が小さい割に体感効果が大きいため正しい。
非目標の設定も手堅く、「ListView 全廃しない」「custom viewport に進まない」で scope creep を防いでいる。

以下、Phase ごとの所見と、コードの現状から見えるギャップを報告する。

---

## A. Phase 0: 計測の足場

### 良い点

**1. 計測から入る設計**
体感改善を「速くなった気がする」で終わらせず、visible 先頭/末尾 index と decode hit を記録に残す。
Phase 1〜3 の前後比較ができるため、改善が退行かの判断に使える。

### 指摘

**A-1 (中): 計測ポイントの具体定義がない**

「ログ点を追加する」としか書かれていない。最低限これだけは欲しい:

- タブ切替 → 最初の visible サムネが描画完了するまでの ms
- スクロール停止 → visible 範囲のサムネが全部出揃うまでの ms
- NoLockImageConverter の cache hit/miss 比率（tab 別）
- decode 所要時間（ms / item）

計測フォーマットを Phase 0 の時点で `DebugRuntimeLog` に固定しておかないと、
Phase 1 の効果測定ができない。

**A-2 (低): Phase 0 の完了条件が「最小ログが取れる」だけ**

「比較できる」とあるが、何と何を比較するのかが未定。
Phase 0 完了時点のベースライン数値を記録として残す運用ルールも欲しい。

---

## B. Phase 1: 軽量化の即効薬

### 良い点

**1. DecodePixelHeight 導入の方向性は正しい**
現状 `NoLockImageConverter` は `ConverterParameter` に `IsExists`（bool）しか受けておらず、
デコードはフルサイズで行われている。
tab ごとの表示物理サイズに合わせた decode に切り替えるだけで、
メモリ消費と decode 時間が大幅に減る。

**2. 非アクティブタブ停止の効果が大きい**
全 5 タブが同一 `FilteredMovieRecs` にバインドされているため、
`ReplaceFilteredMovieRecs`（全件 Clear + Add）が走ると全タブの UI が反応する。
非アクティブタブの更新を止めるだけで、影響範囲が 1/5 に減る。

### 指摘

**B-1 (中): `IsExists` と `DecodePixelHeight` をどう束ねるかの設計判断が未記載**

現行の `NoLockImageConverter.ParseOptions` は:
- bool → `IsExists`（グレー表示判定）
- int/long/double/string → `DecodePixelHeight`

XAML で渡しているのは `{Binding IsExists}` のみ。
Phase 1 で `DecodePixelHeight` も渡す場合、`IsExists` と `DecodePixelHeight` の両方が必要になる。
ただし、ここは変換基盤の作り直しが必要という話ではない。
現行の `ConverterBindableParameter` は内部で `MultiBinding` を使っており、
問題は「2 値を 1 パラメータとしてどう束ねるか」である。

→ `UpperTabDecodeProfile.cs` として切り出す予定はあるが、
文字列、軽量 DTO、既存アダプタ拡張のどれで渡すかが未定。
ここの設計判断を Phase 1 着手前に固めるべき。

**B-2 (高): FilteredMovieRecs の全件差し替え構造が残る**

`ReplaceFilteredMovieRecs` は `Clear() + foreach Add()` で全件差し替えする。
非アクティブタブの更新を「止める」には、以下のどちらかが要る:

1. タブ切替時に `ItemsSource` を差し替える（非アクティブは `null` にする）
2. タブ切替時にコレクション変更通知を抑止する

(1) は `VirtualizingWrapPanel` との相性問題（再仮想化でスクロール位置リセット）がある。
(2) は `CollectionChanged` を suppress する仕組みが `ObservableCollection` にない。

どちらの戦略を取るか、あるいは `UpperTabActivationGate` がどの層で制御するかが
計画書に書かれていない。これは **Phase 1 の最大の技術リスク** だと思われる。

**B-3 (高): `Refresh()` による全体再描画が論点から漏れている**

`ReplaceFilteredMovieRecs` の後に `Refresh()` が走っているため、
非アクティブタブ停止だけではアクティブタブ側の全体再描画コストが残る。
この問題は `FilteredMovieRecs` の全件差し替えと別に扱うべき。

→ 上側タブの高速化では、
- `FilteredMovieRecs` 全件差し替え
- `Refresh()` による全体再描画

の 2 本を分けて追う必要がある。

**B-4 (中): LRU キャッシュ 256 件の扱いが未言及**

現在の `NoLockImageConverter` は LRU 256 件固定（`MaxCacheEntries = 256`）。
1 万件表示で 5 タブ共有だと、タブ切替のたびにキャッシュが全部追い出される。
decode サイズを小さくしてもキャッシュ miss が多ければ再 decode が走る。

→ Phase 1 でキャッシュサイズの拡大 or タブ別キャッシュの検討を入れるべき。
decode サイズ縮小とキャッシュ拡大はセットで効く施策。

**B-5 (低): UpperTabActivationGate の粒度が不明**

「共通活性判定」とあるが、制御粒度が以下のどれかが未定:
- タブ単位（このタブは活性 / 非活性）
- 項目単位（この MovieRecords は更新すべき / すべきでない）
- ImageSource 単位（この画像だけ decode すべき / すべきでない）

Phase 2 の visible range と組み合わせると項目単位まで必要になるが、
Phase 1 ではタブ単位で十分。Phase 1 scope でのゴールを明確にすると混乱を防げる。

---

## C. Phase 2: 可視範囲の取得

### 良い点

**1. visible / near-visible / hidden の 3 段分類**
near-visible を設けたことで、スクロール直後に空白が見えるのを緩和できる。
プリフェッチ領域として機能する。

**2. スクロールイベントの間引き (16〜33ms)**
`ScrollChanged` イベントは高頻度で発火するため、throttle は必須。
16ms は 60fps 相当、33ms は 30fps 相当で、WPF のレンダリング周期に合った値。

### 指摘

**C-1 (高): VirtualizingWrapPanel での visible range 取得方法が未定**

標準 `VirtualizingStackPanel` なら `ItemContainerGenerator.ContainerFromIndex` で
visible 先頭/末尾を求められるが、`VirtualizingWrapPanel` は行あたり複数アイテムが並ぶため、
先頭/末尾 index の求め方が異なる。

`ScrollViewer.VerticalOffset` / `ViewportHeight` / `ExtentHeight` から行数を割って
index 範囲を推定する方法か、`ItemContainerGenerator` を走査して
`IsVisible` な container を列挙する方法かで、精度とコストが大きく違う。

→ `UpperTabViewportTracker` の設計判断として、どちらの方式を取るかを計画書に明記するべき。

**C-2 (中): List タブの DataGrid への適用**

List タブだけ `DataGrid` で、他の 4 タブは `ListView + VirtualizingWrapPanel`。
`DataGrid` は `VirtualizingStackPanel` ベースなので visible range の取得方法が異なる。
`UpperTabViewportTracker` が両方の ScrollViewer 型を扱えるかの設計判断が必要。

**C-3 (低): 間引き周期の決め方**

16ms と 33ms の幅があるが、どの条件で何 ms にするかが未定。
スクロール中は 33ms、スクロール停止後は即時、くらいのルールがあると良い。

---

## D. Phase 3: visible-first サムネ優先制御

### 良い点

**1. 既存 TabIndex 優先に上乗せする設計**
現在の `QueueDbService.GetPendingAndLease` は `preferredTabIndex` で現在タブを優先している。
これを壊さず、同一タブ内で visible 領域を最上位にするのは理にかなっている。

**2. 3 段 priority の明示**
`Visible` > `NearVisible` > `Background` を明示したことで、
queue の ORDER BY に組み込みやすい。

### 指摘

**D-1 (中): QueueDb スキーマ変更を前提にしすぎている**

priority フィールドを `QueueDbUpsertItem` / `QueueDbLeaseItem` に追加し、
`GetPendingAndLease` の ORDER BY に入れるには、QueueDb スキーマの ALTER TABLE が要る。
ただし visible-first の初手として、DB 列追加は必須ではない。
既存の `preferredTabIndexResolver` と同じく、lease 時に visible 情報を参照する
低リスク案もある。

→ 最初に比較すべきは:
1. lease-time resolver 方式（スキーマ変更なし）
2. DB priority 列追加方式

初期段階では 1 を本命候補として記載した方が、実装リスクを正しく表せる。

**D-2 (中): priority の動的更新を DB UPDATE 前提で見すぎている**

visible range はスクロールのたびに変わる。既に queue に入った項目の priority を
スクロールに追従して更新するには、`UPDATE ... SET Priority = ... WHERE MoviePathKey = ...`
のような動的更新が要る。これが poll 間隔（3 秒）と throttle（16〜33ms）の
どちらの周期で走るかで DB 負荷が大きく変わる。

→ 案として:
1. enqueue 時の priority を固定し、スクロール後は新規 enqueue の priority だけ変える
2. visible range 変更のたびに pending 全件の priority を更新する
3. priority は DB に持たず lease 時に visible range と突き合わせる

(1) は精度が低いが DB 負荷ゼロ、(3) はクエリが複雑になるが UPDATE 不要。
特に Phase 3 初手では (3) が低リスク本命に見える。
計画書にどの戦略を取るかの方針が欲しい。

**D-3 (中): QueueObj の ThumbPanelPos との関係**

計画書に「既存の ThumbPanelPos は流用せず」とある。
ThumbPanelPos は手動キューイング時のパネル位置を示し、0〜4 の小さな整数。
visible-first priority はスクロール位置に連動する動的な値。
用途が異なるため流用しないのは正しいが、
手動 enqueue と visible-first priority の優先順位関係を明文化しておくべき。

→ 手動 enqueue は常に最高優先？ それとも visible-first の方が上？
現在のデバウンスが `ThumbPanelPos != null` の場合スキップしている点からすると、
手動は特別扱いのまま残す想定だが、計画書に明示がない。

---

## E. Phase 4: 責務分割

### 良い点

**1. Common + タブ別 partial の構成**
`UpperTabs/Common` に共通基盤、`UpperTabs/Small` 等にタブ固有を置く。
Phase 1〜3 の汎用ロジックが `MainWindow.xaml.cs` に残るのを防ぐ。

### 指摘

**E-1 (中): Phase 4 は Phase 1〜3 の結果次第で scope が変わる**

Phase 1〜3 の実装が `MainWindow.xaml.cs` に直接入った場合、
Phase 4 はそのリファクタリングになる。
一方、Phase 1 の時点から `UpperTabs/Common` に新規ファイルを作る場合、
Phase 4 の分割作業は軽くなる。

計画書の Phase 1「新規候補: UpperTabs/Common/UpperTabActivationGate.cs」を見ると
最初から UpperTabs に置く想定だが、
Phase 4 の「MainWindow は shell に寄せる」がどこまでのリファクタを指すのか不明。

---

## F. 構造的な所見

**F-1 (高): `Refresh()` による全体再描画問題が構造論点として抜けている**

`FilteredMovieRecs` の全件差し替えに加えて、`Refresh()` が一覧全体の再描画を起こしている。
これは active tab 側の体感に直接効くため、構造論点として独立で持つべき。

→ `FilteredMovieRecs` 問題だけでなく、`Refresh()` 問題をどの Phase で扱うかも明記すべき。

**F-2 (高): FilteredMovieRecs 全件差し替え問題がどの Phase で解決されるか不明**

§8 リスクに「FilteredMovieRecs 全件差し替え構造が残るため」と認識しているが、
どの Phase で対処するのか書かれていない。
Phase 1 の非アクティブ停止で緩和はされるが、アクティブタブ自体の
全件差し替え（検索/ソート時）は残る。
Phase 5 相当のタスクか、この計画の scope 外かを明記すべき。

**F-3 (中): 下側タブとの整合**

下側タブには既に `BottomTabs/Common/MainWindow.BottomTabs.Common.cs` で
タブ切替と更新制御の仕組みがある。
上側タブの `UpperTabActivationGate` が下側タブの仕組みと設計パターンを揃えるのか、
独立設計にするのかの方針が欲しい。

**F-4 (中): ブランチ方針との整合**

workthree ブランチは「ユーザー体感テンポ最优先」。
この計画は体感テンポ改善そのものなので方向は合致しているが、
Phase 0〜4 の全工程を 1 ブランチで続けるのか、
Phase ごとにマージポイントを置くのかの運用方針がない。
FailureDb 系の計画書（Phase ごとにコミット + レビュー）の実績を踏まえると、
同じ Phase 刻みの運用が妥当。

---

## 計画書の記載品質

| 項目 | 評価 |
|---|---|
| 目的と非目標の明確さ | ◎ |
| Phase 分割の粒度 | ○ |
| 完了条件の具体性 | △（Phase 0, 1 が曖昧） |
| リスクの認識 | ○ |
| テスト観点 | ○ |
| 技術判断の記載 | △（B-1, B-3, C-1, D-2 が未定） |
| 対象ファイルの特定 | ○ |
| 実装順の根拠 | ◎ |

---

## 総合所見

Phase 1 着手前に固めるべき技術判断:

| 優先度 | 指摘 | 内容 |
|---|---|---|
| 高 | B-2 | 非アクティブタブ停止の実現手段（ItemsSource 差替 or 通知抑止 or 別の方式） |
| 高 | B-3 | `Refresh()` による全体再描画をどの Phase で扱うか |
| 高 | C-1 | VirtualizingWrapPanel での visible range 取得方式 |
| 高 | F-1 | `Refresh()` 全体再描画問題の対処 Phase |
| 高 | F-2 | FilteredMovieRecs 全件差し替え問題の対処 Phase |
| 中 | A-1 | 計測ポイントの具体定義 |
| 中 | B-1 | DecodePixelHeight + IsExists の同時渡し方式 |
| 中 | B-4 | LRU キャッシュサイズの拡張検討 |
| 中 | D-1 | lease-time resolver と DB priority 列追加のどちらを初手にするか |
| 中 | D-2 | priority の動的更新戦略 |
| 中 | D-3 | 手動 enqueue と visible-first priority の優先順位 |
| 中 | F-3 | 下側タブとの設計パターン整合 |

Phase 0 → 1 → 2 → 3 → 4 の順序と scope 設定は妥当。
B-2 / B-3 の設計判断が Phase 1 着手のゲートになるため、先に方式を決めてから実装に入るのが良い。
