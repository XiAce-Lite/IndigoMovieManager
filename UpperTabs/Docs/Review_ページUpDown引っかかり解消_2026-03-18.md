# Review: ページUpDown引っかかり解消 Implementation Plan (2026-03-18)

レビュー実施日: 2026-03-18
レビュー対象: `UpperTabs/Docs/Implementation Plan_ページUpDown引っかかり解消_2026-03-18.md`

## 1. 総合評価

方向性は正しく、workthree のテンポ最優先方針に合致している。
診断の正確性が高く、コードベースの実態と計画の記述がほぼ完全に一致する。
フェーズ順序も低リスク順に並んでおり、着手しやすい構成になっている。

## 2. 診断の検証結果

計画が挙げた 6 つの事実を、コード実態と突き合わせた結果を示す。

### 2.1 仮想化は入っている → ✅ 正確

`MainWindow.xaml` で `VirtualizingWrapPanel` + `IsVirtualizing=True` + `VirtualizationMode=Recycling` が全タブに適用済み。
計画の「まず仮想化を入れる、は今回の本筋ではない」という判断は妥当。

### 2.2 refresh 二重実行 → ✅ 正確、ただし補足あり

コードで確認した二重経路は以下の通り。

**即時経路** (`MainWindow.UpperTabs.PageScroll.cs` 39-44行):
```
TryScrollPage → RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "page-down")
  → タイマー停止 → ApplyUpperTabVisibleRangeRefresh を即実行
```

**遅延経路** (`MainWindow.UpperTabs.Viewport.cs` 158-164行):
```
ScrollToVerticalOffset → ScrollChanged イベント発火
  → RequestUpperTabVisibleRangeRefresh(reason: "scroll")  ← immediate 省略 = false
  → タイマー開始 → Tick 後に Apply 再実行
```

計画の記述通り、1 回の PageDown で Apply が 2 回走りうる。

**補足**: 遅延経路は `immediate: false` のためタイマー経由であり、即時経路と完全に同一 tick で重なるわけではない。
即時経路が先に走り、その後タイマー interval 後に遅延経路が走る。
つまり「同時に重なる」のではなく「近接して 2 回走る」が正確な表現。
改善時は、即時経路実行後にタイマーをリセットするだけで遅延経路を抑制できる可能性がある。

### 2.3 visible range 取得が重い → ✅ 正確、重要な構造的問題あり

`UpperTabViewportTracker.cs` の実態:

- `EnumerateRealizedContainers` (64-76行) が `EnumerateVisualChildren<FrameworkElement>` を呼ぶ
- `EnumerateVisualChildren` は `VisualTreeHelper.GetChildrenCount` で深さ優先再帰走査する
- 走査した全 `FrameworkElement` に対して `IndexFromContainer` を呼び、-1 でないものだけ抽出する
- 抽出した各コンテナに対して `TransformToAncestor` で座標変換し、ビューポート交差判定する

**計画が見落としている点**:

- `EnumerateRealizedContainers` は **全 VisualTree 子孫のうち FrameworkElement であるもの全て** を列挙した上で `IndexFromContainer` でフィルタしている。
  つまり「item container 候補」ではなく「全 FrameworkElement」を拾ってから捨てている。
  Big / Big10 では 1 件のテンプレート内に Grid, TextBlock, Image, ItemsControl, WrapPanel, TagControl 等が入るため、
  実際の item container 数の 10〜20 倍の FrameworkElement を走査している可能性がある。
- `FindScrollViewer` も同じ `EnumerateVisualChildren` を使っている。
  `ApplyUpperTabVisibleRangeRefresh` で毎回 `FindScrollViewer` を呼んでいるため、
  ScrollViewer 探索だけでも VisualTree 再帰が毎回走っている。

計画の Phase 2 で「ScrollViewer 探索結果を attach 時に保持する」は正しい方向だが、
`EnumerateRealizedContainers` の走査範囲を item container generator の直接列挙へ置き換える方が効果は大きい。

### 2.4 画像キャッシュは小さい → ✅ 正確

`NoLockImageConverter.cs` 23行: `MaxCacheEntries = 256`

metadata 取得の順序も計画通り (68-78行):
```csharp
Path.Exists(filePath)     // I/O 1回目
FileInfo fileInfo = new(fullPath)  // I/O準備
fileInfo.LastWriteTimeUtc.Ticks    // I/O 2回目
fileInfo.Length                     // I/O 3回目
```

**計画が見落としている点**:

- キャッシュヒット時でも `Path.Exists` + `FileInfo` の metadata 取得が走る。
  `ConvertWithOptions` は「キャッシュを引く前に」必ず metadata を取得している (68-78行)。
  つまり **キャッシュヒットは I/O を回避しない。decode を回避するだけ** である。
  計画 7.3 の「file metadata を短時間キャッシュする」はこの問題に対処するものだが、
  この事実の重要度が計画本文で十分強調されていない。
  **キャッシュヒットしても毎回 3 回の file I/O が走る** のは、Phase 3 の優先度を Phase 1 並みに上げる根拠になりうる。

- `BuildCacheKey` で `fullPath|colorKey|H{decodePixelHeight}` をキーにしているため、
  同じ画像でも `decodePixelHeight` が異なるタブ間ではキャッシュ共有できない。
  タブ切り替え → 戻りの往復でキャッシュミスが起きやすい。

### 2.5 WrapPanel 系は Reset 方針 → ✅ 正確

`UpperTabCollectionUpdatePolicy.cs` 16-24行:
```csharp
if (tabIndex == 3) { return isSortOnly ? Move : Diff; }
return FilteredMovieRecsUpdateMode.Reset;
```

List (tabIndex=3) だけ Diff/Move、他は全て Reset。

### 2.6 下側詳細ペインは主犯度が低い → 検証保留

この点は直接のコード確認をしていないが、計画の判断（上側タブと画像 converter が主戦場）は、
上記 2.2〜2.4 の問題の深刻度から見て妥当。

## 3. 対策方針の評価

### 3.1 refresh 二重実行対策 (Phase 1) → ◎ 正しい、最安で効果が出やすい

方針: 即時 refresh をやめて scroll 後の 1 本へ寄せる

**評価**: 妥当。ただし以下の 2 案を比較検討する価値がある。

- **案 A**: PageDown では即時 refresh をやめ、ScrollChanged のタイマー経路だけ使う
  → 反応が遅延する（タイマー interval 分）
- **案 B**: PageDown の即時 refresh を残し、直後の ScrollChanged タイマーを抑制する
  → 反応は即時のまま、2 回目だけ潰す

計画は案 A 寄りだが、テンポ最優先なら案 B の方が適切な可能性がある。
「最後のスクロール変化から N ms 以内に Apply を呼んだ場合はタイマーをスキップ」のような guard で十分実現できる。

### 3.2 visible range 取得コスト対策 (Phase 2) → ○ 方向は正しいが、最大の改善点が一段弱い

計画が挙げる対策:
- ScrollViewer 探索結果をキャッシュ
- realized container だけ狭く拾う
- 変化なし時の snapshot 再構築を止める

**追加すべき最重要対策**:

- `EnumerateRealizedContainers` を VisualTree 全走査から **ItemContainerGenerator ベースの直接列挙** へ置き換える

現在の実装は「全 VisualTree 子孫を拾って IndexFromContainer で篩う」構造だが、
WPF の `ItemContainerGenerator` には `GeneratorPosition` や `ContainerFromIndex` があり、
`Items.Count` と組み合わせれば生成済みコンテナだけを直接取れる。
この置き換えは Phase 2 の核になるべきだが、計画では「resolver を分ける」という間接的な表現に留まっている。

### 3.3 画像キャッシュ不足対策 (Phase 3) → ○ 正しいが、metadata キャッシュの優先度が低く書かれすぎている

サイズ拡大 (256 → 1024〜1536) は妥当。

ただし **metadata 短時間キャッシュの効果は、LRU 拡大と同等かそれ以上** であることを強調すべき。

理由:
- 現行では **キャッシュヒットしても毎回 `Path.Exists` + `FileInfo` が走る**
- 1 画面に 20 件見える場合、PageDown 1 回で 20 件分の file I/O が走る
- metadata を 5〜10 秒キャッシュするだけで、往復スクロール時の I/O がほぼゼロになる

初期候補の LRU サイズ「1024 ないし 1536」について:
- Big タブ (1 画面 ≒ 12〜20 件) で 5 画面分の往復なら 100 件で十分
- Small タブ (1 画面 ≒ 50〜100 件) で 5 画面分なら 500 件
- 1024 はバランスの取れた初期値。1536 は少し多い
- **メモリ影響**: BitmapSource のサイズは decode 解像度に依存する。`DecodePixelHeight` 付きなら 1 件 50KB〜200KB 程度。1024 件で 50MB〜200MB。上限側が気になるなら、メモリ上限方式への移行を Phase 3 の出口条件に入れておくのが安全

### 3.4 テンプレート軽量化 (Phase 4) → △ 効果は出にくい、優先度は計画通り最下位で正しい

`BigDetailControl.xaml` の実態を確認すると:
- Grid (5行2列) + TextBlock ×9 + ItemsControl (タグ) + WrapPanel + TagControl 群
- 構造としてはそこまで深くない（ネスト 3 段程度）
- タグの WrapPanel に仮想化属性は付いているが、パネル自体は `WrapPanel`（VirtualizingWrapPanel ではない）

**注意**: `VirtualizingPanel.IsVirtualizing=True` を `WrapPanel` に付けても、`WrapPanel` は仮想化パネルではないため実質無効。
タグが多いアイテムでは全タグが一度に実体化される。これは計画が「タグ領域は visible-first で段階表示」と書いている部分の裏付けになるが、
**`WrapPanel` に仮想化属性が付いているのに効いていない** という事実は計画に明記すべき。

### 3.5 startup append 干渉対策 (Phase 5) → ○ 方向は正しい

`MainWindow.Startup.cs` の連鎖構造を確認:

```
ApplyUpperTabVisibleRangeRefresh
  → TryScheduleStartupAppendForCurrentViewport  (毎回呼ばれる)
    → ShouldRequestStartupAppend (判定)
      → LoadStartupContinuationPageAsync (非同期)
        → ApplyStartupAppendPage
          → RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "startup-append")
            → ApplyUpperTabVisibleRangeRefresh  【再帰的に戻る】
```

この **Apply → Append → Apply の再帰構造** は、Phase 1 の refresh 一本化と合わせて整理しないと効果が薄い。
計画では Phase 5 として最後に置いているが、Phase 1 と同時に触る方が整合性は取りやすい。

## 4. フェーズ順序の評価

計画の着手順: Phase 0 → 1 → 3 → 2 → 4 → 5

**評価**: 概ね妥当。詳細コメント:

| 順序 | Phase | 評価 | コメント |
|------|-------|------|---------|
| 1st | Phase 0 (観測固定) | ◎ | 必須。before/after が取れないと改善を証明できない |
| 2nd | Phase 1 (refresh 一本化) | ◎ | 最安で効果が出やすい |
| 3rd | Phase 3 (画像キャッシュ) | ○ | 妥当。ただし metadata キャッシュを Phase 1 と同時に入れてもよい |
| 4th | Phase 2 (viewport tracker) | ○ | 実装は重いが効果も大きい |
| 5th | Phase 4 (テンプレート) | △ | 効果は実測次第。最下位で正しい |
| 6th | Phase 5 (startup append) | ▲ | Phase 1 との同時着手を推奨 |

**順序変更の提案**:

Phase 5 を Phase 1 と統合する方が安全。理由:
- Phase 1 で refresh 経路を触る時に、startup append からの `immediate: true` の扱いも同時に決める必要がある
- Phase 5 を後回しにすると、Phase 1 の改修が startup append 経路を考慮せずに進む危険がある
- 影響範囲が `MainWindow.UpperTabs.Viewport.cs` と `MainWindow.Startup.cs` で重複している

## 5. リスク評価

計画が挙げた 4 リスクに加え、以下を追加すべき。

### 計画記載のリスク → 全て妥当

| リスク | 評価 |
|--------|------|
| container 取得を攻めすぎると visible range がずれる | 妥当。Phase 2 で回帰テスト必須 |
| 画像キャッシュ増でメモリ圧迫 | 妥当。1024 件なら許容範囲だが監視は要る |
| startup append 抑制で段階ロードの見え方が悪化 | 妥当。閾値調整でバランスを取る |
| Big/Big10 の情報量削りすぎで UX 変化 | 妥当。Phase 4 は実測主導で |

### 追加リスク

| # | リスク | 重要度 | 対策案 |
|---|--------|--------|--------|
| R5 | BigDetailControl のタグ WrapPanel 仮想化属性が実質無効 | 🟡 中 | VirtualizingWrapPanel へ差し替えるか、タグ数上限で Truncate する |
| R6 | metadata キャッシュ導入時のスレッドセーフティ | 🟡 中 | `ConcurrentDictionary` or 既存 `CacheGate` lock の拡張 |
| R7 | Phase 1 で即時 refresh を消すと、startup-first-page の初回表示が遅れる | 🟡 中 | startup 経路からの `immediate: true` は例外的に残す判断が要る |
| R8 | `DecodePixelHeight` 違いでキャッシュが分散する | 🟢 低 | 主要 decode height を 1〜2 に絞れるか調査 |

## 6. 計画文書への具体的な修正提案

### 6.1 セクション 3.4 に追記すべき事実

> BigDetailControl のタグ領域は `WrapPanel` に `VirtualizingPanel.IsVirtualizing=True` を付けているが、
> `WrapPanel` は仮想化パネルではないため実質無効である。
> タグが多いアイテムでは全タグが同時に実体化される。

### 6.2 セクション 7.3 の強調度を上げるべき点

> **キャッシュヒット時でも毎回 `Path.Exists` + `FileInfo` の file I/O が走る。**
> キャッシュは decode を回避するだけであり、I/O を回避しない。
> metadata 短時間キャッシュの効果は LRU 拡大と同等かそれ以上である。

### 6.3 セクション 7.2 に追加すべき対策

> `EnumerateRealizedContainers` の VisualTree 全走査を
> `ItemContainerGenerator` ベースの直接列挙へ置き換える。
> 現在は全 FrameworkElement を拾って IndexFromContainer で篩う構造だが、
> 生成済みコンテナだけを直接取る方式に変えれば走査対象が 1/10〜1/20 に減る。

### 6.4 セクション 9 の着手順調整

Phase 5 を Phase 1 と統合するか、少なくとも Phase 1 の設計時に Phase 5 の接点を明示すべき。

理由: `ApplyUpperTabVisibleRangeRefresh` → `TryScheduleStartupAppendForCurrentViewport` → `ApplyStartupAppendPage` → `RequestUpperTabVisibleRangeRefresh(immediate: true)` の再帰構造が Phase 1 のスコープに入る。

### 6.5 Phase 0 のログキーに関する追記

現行のログ体系は `"ui-tempo"`, `"db"`, `"lifecycle"`, `"watch"` 等のキーを使用している。
Phase 0 で追加するログはこの命名規約に乗せる想定を明記すべき。

候補:
```
"ui-tempo" : page scroll key begin/end, viewport refresh elapsed_ms
"ui-tempo" : image cache hit/miss, metadata cache hit/miss
"ui-tempo" : startup append suppressed by page scroll
```

## 7. 結論

**計画の品質は高い。** 診断は全てコード実態で裏付けが取れた。

最も重要な修正は以下の 3 点:

1. **metadata キャッシュの優先度を上げる** — キャッシュヒットでも I/O が走る事実の影響は大きい
2. **Phase 5 を Phase 1 と統合する** — refresh 経路と startup append の再帰構造は同時に触る方が安全
3. **VisualTree 全走査の代替案を Phase 2 に明記する** — `ItemContainerGenerator` 直接列挙が核になるべき

これらを反映すれば、workthree のテンポ最優先方針に沿った実行可能な計画として着手できる。
