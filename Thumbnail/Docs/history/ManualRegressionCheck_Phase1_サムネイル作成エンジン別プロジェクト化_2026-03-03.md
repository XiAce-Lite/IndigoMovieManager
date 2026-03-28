# 手動回帰チェック手順（Phase1: サムネイル作成エンジン別プロジェクト化 2026-03-03）

## 1. 目的
- Phase1（アセンブリ分離）後に、サムネイル生成の基本導線が退行していないことを短時間で確認する。
- 特に `通常サムネ` と `手動サムネ` の確認ポイントを最小手順で固定する。

## 2. 短い手順（5ステップ）
1. 回帰テスト（分岐ロジック）を実行する。  
   `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~AutogenRegressionTests"`
2. 実動画1本で通常サムネ生成を実行する（autogen / ffmediatoolkit）。  
   `pwsh -File .\Thumbnail\Test\run_thumbnail_engine_bench.ps1 -InputMovie "<動画パス>" -Engines autogen,ffmediatoolkit -Iteration 1 -Warmup 0 -TabIndex 0 -Configuration Debug -SkipBuild`
3. アプリ起動スモーク（20秒）を実行する。  
   `bin\Debug\net8.0-windows\IndigoMovieManager_fork.exe` を起動し、20秒以上生存することを確認する。
4. 手動（UI）で通常サムネを1件生成する。  
   MainWindowで1動画を選択し、通常サムネ生成を実行する。
5. 手動（UI）で手動サムネを1件生成する。  
   再生位置を指定して手動サムネを実行する。

## 3. 合格条件
- `thumbnail-create-process.csv` に `status=success` の追記があること。
- 出力サムネイルの命名が `動画名.#hash.jpg` であること。
- アプリ起動が即時クラッシュしないこと。
- 手動サムネ（UI操作）で例外ダイアログが出ないこと。

## 4. 実施ログ（2026-03-03）
- [x] 手順1: 実施済み  
  結果: `成功 4 / 失敗 0 / スキップ 0`
- [x] 手順2: 実施済み  
  入力動画: `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\logs\manual-regression\20260303_033121\sample_4s.mp4`  
  結果: `autogen=success(1/1), ffmediatoolkit=success(1/1)`  
  ベンチCSV: `C:\Users\na6ce\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260303-033133.csv`  
  出力確認: 2件とも実ファイル存在、命名は `sample_4s.#aa65bfef.jpg`
- [x] 手順3: 実施済み  
  結果: 起動20秒後もプロセス生存（`AliveAfter20Sec=True`）
- [x] 手順4: 実施済み（ユーザー手動）
- [x] 手順5: 実施済み（ユーザー手動）

## 5. 手順4/5の最短実施メモ（ユーザー操作）
1. `pwsh -File .\Thumbnail\Test\run_autogen_e2e_manual.ps1` を実行する。
2. アプリで通常サムネ1件、手動サムネ1件を作成する。
3. Enterでスクリプトを戻し、`logs\e2e-manual\<timestamp>\after_*` を確認する。
