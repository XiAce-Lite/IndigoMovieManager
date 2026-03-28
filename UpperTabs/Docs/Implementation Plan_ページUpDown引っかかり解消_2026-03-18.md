# Implementation Plan: ページUpDown引っかかり解消 (2026-03-18)

最終更新日: 2026-03-18

変更概要:
- 起動段階ロード化の次に優先する「PageUp / PageDown 後の引っかかり解消」方針を整理する
- 上側タブの viewport refresh、画像キャッシュ、テンプレート重量の主因を分離して扱う
- `UpperTabs` 側の実装と `NoLockImageConverter` 側の実装を、低リスク順に着手できる形へ落とす
- 起動 partial 中の append と、起動後の一覧操作テンポを両立するための境界を明記する
- 追加レビューを反映し、`metadata` キャッシュ、`ItemContainerGenerator` 直接列挙、設定項目化を明記する
- Phase 1 / Phase 2 の先行実装として、follow-up scroll refresh suppress、startup append の timer 化、cache 設定項目化、metadata cache を反映した
- Phase 3 の first slice として、items host panel 優先列挙と `ScrollViewer` cache を反映した
- Phase 4 の first slice として、`Big / Big10 / List` の非選択タグ表示をサマリ化した
- Phase 0 の first slice として、`page scroll begin/end` と `viewport refresh elapsed_ms` の `ui-tempo` ログを追加した
- Phase 4 の second slice として、`Big / Big10` の上段詳細を非選択時 1 行サマリへ畳んだ
- Phase 1 の追加 slice として、ページ送り直後の `startup append` を短時間 suppress し、必要時だけ retry timer で再評価する形へ寄せた

## 実装進捗 (2026-03-18)

- [x] `PageUp` / `PageDown` 直後の `ScrollChanged` follow-up refresh を短時間 suppress する
- [x] `startup-append` 後の refresh を timer 経由へ寄せ、`Apply -> Append -> Apply` の詰まりを少し和らげる
- [x] 共通設定へ「一覧画像キャッシュ最大件数」を追加し、既定値を `1024` にした
- [x] `NoLockImageConverter` の image cache 上限を設定値から読むようにした
- [x] `NoLockImageConverter` に短命 metadata cache を追加した
- [x] `ui-tempo` へ `page scroll begin/end` と `viewport refresh elapsed_ms` を追加した
- [x] ページ送り直後は `startup append` を短時間 suppress し、必要時だけ retry timer で再評価する
- [x] `UpperTabViewportTracker` で items host panel 優先列挙と `ScrollViewer` cache を入れた
- [x] items host を掴めない時だけ `ItemContainerGenerator` fallback を使う形へ寄せた
- [x] `Big / Big10` のタグ領域を、非選択時サマリ / 選択時フル表示へ分けた
- [x] `List` のタグ領域を、非選択時サマリ / 選択時フル表示へ分けた
- [x] `Big / Big10` の上段詳細を、非選択時は `スコア / サイズ / 時間` の 1 行サマリへ寄せた
- [ ] タイトル・詳細テキスト側の更なる軽量化

## 1. 目的

目的は 1 つだけである。

- 起動後の処理が落ち着いた後、上側タブで `PageUp` / `PageDown` を押しても引っかかりにくい状態を作る

ここで言う「引っかかり」は、以下をまとめて指す。

- キー入力直後に一瞬止まる
- 1 画面送りのはずが、可視範囲計算や画像再読込で追従が遅れる
- 戻りスクロール時に、見たばかりの画像が再 decode されて詰まる
- startup partial の append と、一覧操作の仕事が重なってテンポが崩れる

## 2. 結論

先にやるべきことは、仮想化の追加ではない。

優先順位は以下で固定する。

1. `PageUp` / `PageDown` 後の viewport refresh 二重実行と startup append 再帰をまとめて止める
2. `NoLockImageConverter` の `metadata` キャッシュと image cache 上限設定を先に入れる
3. `UpperTabViewportTracker` の VisualTree 全走査を、`ItemContainerGenerator` 直接列挙ベースへ置き換える
4. `Big / Big10 / List` の 1 件テンプレートを軽くする

今の主犯は、

- `VirtualizingWrapPanel` 不足

ではなく、

- refresh の打ち方
- visible range 取得方法
- 画像キャッシュの小ささ
- キャッシュヒット時でも I/O を回避できていないこと
- 実体化された 1 件テンプレートの重さ

である。

## 3. 調査で確定した事実

### 3.1 仮想化そのものは入っている

- `Small / Big / Grid / Big10` は `ListView + VirtualizingWrapPanel`
- `List` は `DataGrid` 仮想化

したがって、「まず仮想化を入れる」は今回の本筋ではない。

### 3.2 PageUp / PageDown で viewport refresh が二重に走る

現在は以下の 2 経路が連続する。

1. キー処理側で `ScrollToVerticalOffset(...)`
2. その直後に `RequestUpperTabVisibleRangeRefresh(immediate: true, ...)`
3. さらに `ScrollChanged` 側で `RequestUpperTabVisibleRangeRefresh(reason: "scroll")`

つまり、1 回の `PageDown` で同じ目的の refresh が重なりやすい。

### 3.3 visible range 取得が重い

`UpperTabViewportTracker` は毎回 VisualTree を再帰でたどり、
`FrameworkElement` を広く集めた後で item container を選別している。

この方式は、

- `Big / Big10` のようにテンプレートが深いタブ
- `ItemsControl + WrapPanel` を内包するタブ

ほどコストが膨らみやすい。

### 3.4 画像キャッシュは小さい

`NoLockImageConverter` は LRU 256 件固定である。

さらにキャッシュ照会前に毎回、

- `Path.Exists`
- `FileInfo`
- `LastWriteTimeUtc`
- `Length`

を読んでいる。

つまり、現行キャッシュは decode を避けても file I/O を避けない。

そのため、1 画面送りで見えた画像を戻りスクロールで再表示した時、
キャッシュから漏れる場合だけでなく、キャッシュヒット時でも metadata 取得負荷が残る。

さらに cache key は `decodePixelHeight` を含むため、
タブ間で decode 高さが違う画像は cache を共有しにくい。

### 3.5 WrapPanel 系タブは Reset 方針のまま

`Small / Big / Grid / Big10` は `FilteredMovieRecsUpdateMode.Reset` 固定である。

通常の `PageUp` / `PageDown` だけなら直撃しないが、

- startup append
- filter
- sort

が重なると、一覧全体が再構築寄りになりやすい。

### 3.6 タグ領域の仮想化属性は一部で実質効いていない

`BigDetailControl` や `List` タブのタグ領域は、
`WrapPanel` に対して `VirtualizingPanel.IsVirtualizing=True` を付けている箇所がある。

ただし `WrapPanel` 自体は仮想化パネルではないため、
タグ数が多い行では全タグが同時に実体化される。

これは主犯ではないが、Phase 4 で効く余地がある。

### 3.7 下側詳細ペインは主犯度が下がっている

詳細タブは前面時だけ更新する gate が入っている。
`Refresh()` も現在は詳細再表示中心であり、一覧全体再描画ではない。

したがって、今回の主戦場は上側タブと画像 converter である。

## 4. 今回の対象範囲

主対象:

- `UpperTabs/Common/MainWindow.UpperTabs.PageScroll.cs`
- `UpperTabs/Common/MainWindow.UpperTabs.Viewport.cs`
- `UpperTabs/Common/UpperTabViewportTracker.cs`
- `UpperTabs/Common/UpperTabCollectionUpdatePolicy.cs`
- `Infrastructure/Converter/NoLockImageConverter.cs`
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`
- `Views/Main/MainWindow.Startup.cs`
- `Views/Main/MainWindow.xaml`
- `Views/Settings/CommonSettingsWindow.xaml`
- `Views/Settings/CommonSettingsWindow.xaml.cs`
- `UserControls/BigDetailControl.xaml`

関連参照:

- `Views/Main/Docs/詳細設計_大DB起動段階ロード化_2026-03-17.md`
- `Docs/調査結果_UIボトルネック解消_2026-03-11.md`
- `UpperTabs/Docs/調査結果_VirtualizingWrapPanel適用効果検証_2026-03-17.md`

## 5. 非目標

今回やらないことを先に固定する。

- 上側タブを全面的に別 UI へ作り直すこと
- `ListView` / `DataGrid` を全廃すること
- `VirtualizingWrapPanel` を別ライブラリへ差し替えること
- `MovieRecords` 全体設計をこのテーマだけで大改造すること
- startup partial そのものを先に撤去すること

## 6. 成功条件

最低限の成功条件は以下で固定する。

1. startup heavy services 開始後でも、`PageUp` / `PageDown` の反応が目立って鈍らない
2. 同じ範囲を往復した時、画像再 decode の頻度が下がる
3. キャッシュヒット時の metadata I/O が短時間で再発しにくくなる
4. 最大キャッシュサイズを共通設定から変えられる
5. 初期値は現行 256 より十分大きい値で始める
6. `Big / Big10 / List` で `PageDown` 後の停止感が減る
7. startup partial 中でも、append 要求が過剰に連鎖しない
8. `ui-tempo` ログで before / after の比較が取れる

## 7. 主因ごとの対策方針

### 7.1 refresh 二重実行

問題:

- キー処理直後の即時 refresh と、`ScrollChanged` 起点の refresh が二重化している

方針:

- `PageUp` / `PageDown` の即時 refresh を残すか、scroll 側 1 本へ寄せるかは両案比較する
- 少なくとも「即時 refresh 実行後の遅延 refresh」は suppress する
- 同一 tick 近傍の重複を潰す request id か last-applied timestamp を入れる
- `startup-append` からの `immediate: true` も同じ場で整理し、再帰構造を減らす

狙い:

- 1 回のキー入力で 2 回以上 `ApplyUpperTabVisibleRangeRefresh(...)` が走るのを避ける
- `Apply -> Append -> Apply` の再帰連鎖も同時に減らす

### 7.2 visible range 取得コスト

問題:

- VisualTree 再帰走査が深いテンプレートほど重い

方針:

- `EnumerateRealizedContainers` の VisualTree 全走査をやめ、`ItemContainerGenerator` 直接列挙へ置き換える
- `ItemsControl` ごとに visible range resolver を分ける
- `DataGrid` は row container ベース
- `VirtualizingWrapPanel` タブは realized container だけをより狭く拾う
- `ScrollViewer` 探索結果は attach 時に保持し、refresh ごとに再探索しない
- visible range が変わらなかった時は、後段 snapshot 再構築も止める

狙い:

- `PageDown` 直後の CPU スパイクを下げる

### 7.3 画像キャッシュ不足

問題:

- 256 件では上側タブの往復スクロールに足りない
- キャッシュヒット時でも metadata 取得 I/O が残る
- cache key が decode 高さごとに分かれ、タブ間共有が効きにくい

方針:

- `NoLockImageConverter` の image cache 上限を拡大する
- path + decode 高さごとの image cache は残しつつ、file metadata を短時間キャッシュする
- 最大キャッシュサイズは共通設定へ追加し、ユーザーが調整できるようにする
- 初期値は現行 256 より大きい `1024` を既定候補とする
- 設定 UI は共通設定へ追加し、再起動不要で次回 decode から反映できる形を優先する
- active tab でよく触る decode profile を優先保持する
- まずは固定拡大で始め、必要なら後でメモリ上限方式へ進む

初期候補:

- 既定値 1024
- 設定可能範囲は 256 から 4096 を初期候補とする

狙い:

- `PageUp` / `PageDown` の往復で見た画像を再利用しやすくする
- cache hit 時の metadata I/O もまとめて減らす

### 7.4 1 件テンプレートの重さ

問題:

- `Big / Big10` はタイトル、画像、詳細、タグの 4 層が重い
- `List` もタグ `ItemsControl + WrapPanel` が重い
- `WrapPanel` に付けた仮想化属性は実質効いていない

方針:

- 非選択時は詳細情報量を少し減らす
- タグ領域は visible-first で段階表示するか、少なくとも全件同時再評価を避ける
- `BigDetailControl` の下段タグ領域は、まず見えている行だけ負荷が乗るよう調整する

狙い:

- viewport refresh で実体化されたアイテム 1 件あたりの仕事量を減らす

### 7.5 startup append 干渉

問題:

- startup partial 中に viewport 近傍判定で append が走る
- append 後にも即時 refresh が入り、ページ移動と仕事が重なる

方針:

- partial 中の append 判定を、ページ移動直後は少し寝かせる
- append 完了直後の即時 refresh も見直し、必要最小限にする
- `filterList = MainVM.FilteredMovieRecs.ToArray()` の全コピーを減らせるか確認する

狙い:

- 起動直後の骨格は維持しつつ、操作テンポ優先へ寄せる

## 8. フェーズ案

## Phase 0: 観測固定

目的:

- `PageUp` / `PageDown` に対して、どこで止まっているかをログで確定する

追加ログ候補:

- `ui-tempo page scroll key begin/end`
- `ui-tempo viewport refresh elapsed_ms`
- `ui-tempo viewport container_scan_count`
- `ui-tempo image cache hit/miss`
- `ui-tempo image metadata cache hit/miss`
- `ui-tempo startup append suppressed by page scroll`

完了条件:

- before / after を `ui-tempo` で比較できる

## Phase 1: refresh 一本化と startup append 接続整理

目的:

- 1 回のキー入力で viewport refresh が重ならないようにし、startup append の再帰も同時に整理する

実施内容:

- `PageUp` / `PageDown` 後の即時 refresh と scroll 側 refresh の役割を整理する
- 重複 refresh を捨てる guard を入れる
- startup append の起動条件とページ移動直後の関係を整理する
- `startup-append` 後の `immediate: true` を含め、再帰構造を減らす

対象:

- `UpperTabs/Common/MainWindow.UpperTabs.PageScroll.cs`
- `UpperTabs/Common/MainWindow.UpperTabs.Viewport.cs`
- `Views/Main/MainWindow.Startup.cs`

完了条件:

- 同じ 1 回のページ移動で viewport refresh が 1 系統に寄る
- startup append 由来の再帰 refresh も整理される

## Phase 2: 画像キャッシュ設定化と metadata キャッシュ

目的:

- 戻りスクロール時の再 decode と metadata I/O を早い段階で減らす

実施内容:

- `NoLockImageConverter` の image cache 上限を設定値から読む
- file metadata の短時間キャッシュを追加する
- 共通設定へ最大キャッシュサイズを追加する
- 既定値は 1024 で開始する
- 範囲外入力は安全側へ丸める

対象:

- `Infrastructure/Converter/NoLockImageConverter.cs`
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`
- `Views/Settings/CommonSettingsWindow.xaml`
- `Views/Settings/CommonSettingsWindow.xaml.cs`

完了条件:

- 同じ範囲の往復で cache hit が増える
- cache hit 時の metadata I/O も短時間で抑えられる
- 共通設定から上限を変更できる

## Phase 3: viewport tracker 軽量化

目的:

- visible range 取得そのものを軽くする

実施内容:

- `ScrollViewer` の再探索をやめる
- `ItemContainerGenerator` ベースで generated container を直接列挙する
- item container 列挙範囲を狭める
- `DataGrid` と `VirtualizingWrapPanel` で resolver を分ける
- 変化なし時の後段 snapshot 再構築を止める

対象:

- `UpperTabs/Common/UpperTabViewportTracker.cs`
- `UpperTabs/Common/MainWindow.UpperTabs.Viewport.cs`

完了条件:

- `PageDown` 直後の visible range 取得コストが下がる

## Phase 4: テンプレート軽量化

目的:

- 1 件あたりの描画仕事を減らす

実施内容:

- `Big / Big10 / List` の詳細量を見直す
- タグ表示の即時性を下げてもよい所を分ける
- 非選択・非近傍で常に要らない情報を減らす

対象:

- `Views/Main/MainWindow.xaml`
- `UserControls/BigDetailControl.xaml`
- 必要なら `UserControls/SmallDetailControl.xaml`

完了条件:

- 実体化アイテム数が同じでも、スクロール時の引っかかりが減る

## 9. 最初の着手順

最初にやる順番は以下で固定する。

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4

理由:

- まず重複 refresh を切るのが一番安い
- 次に metadata キャッシュと上限設定を入れると、往復スクロールの体感が早く改善しやすい
- viewport tracker 作り直しは効果が大きいが、少し実装が重い
- テンプレート軽量化は最後に実測付きで進める

## 10. リスク

- `VirtualizingWrapPanel` の container 取得を攻めすぎると visible range がずれる
- 画像キャッシュを増やしすぎるとメモリ圧迫に振れる
- metadata キャッシュ追加時にスレッドセーフティを崩すと逆に不安定になる
- startup append 抑制を強くしすぎると、段階ロードの見え方が悪くなる
- `Big / Big10` の情報量を削りすぎると既存 UX が変わる
- `WrapPanel` 側を軽くしようとして表示崩れを入れる危険がある

## 11. 成果物

この計画の成果物は以下。

- viewport refresh の一本化
- cache 上限の設定項目化と既定値 1024 の導入
- cache 拡大と metadata 再取得抑制
- before / after を比較できるログ
- 起動段階ロード計画へ接続された次段改善メモ

この順で進めれば、workthree の方針である「ユーザー体感テンポ最優先」と矛盾せず、
起動改善の次の一手として素直につながる。
