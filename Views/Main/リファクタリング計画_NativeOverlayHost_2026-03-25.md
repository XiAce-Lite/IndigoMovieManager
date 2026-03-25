# NativeOverlayHost 根本リファクタリング 🔥

更新日: 2026-03-25（実装完了・native本線復活済み）

## 背景
要件定義と障害対応記録を読み込み、`NativeOverlayHost.cs`（旧1170行）を精読した結果、4つの構造的問題と1つの重大バグを発見・修正したよ！✨

## 🔍 発見した問題と解決策

| # | 問題 | 犯人 | 対策 | 結果 |
|---|------|------|------|------|
| 1 | デッドコード30行 | fallback移行後の到達不能な screenDc リトライパス | 削除 | ✅ |
| 2 | 二重座標クランプ | `RenderByFallbackWindow` で再クランプ | 除去 | ✅ |
| 3 | ログ二重出力 | fallback パスで `render failed` が2本出る | 1箇所に統合 | ✅ |
| 4 | 1170行が1ファイル | P/Invoke 260行がロジックと混在 | `NativeOverlayHost.NativeMethods.cs` に分離 | ✅ |
| 5 | **native 本線が動かない** | WPF `HwndSource` が `WS_EX_LAYERED` を剥ぎ取る + `SetLayeredWindowAttributes` と `UpdateLayeredWindow` の排他問題 | `CreateWindowEx` 直接生成 + 初期化時の `SetLayeredWindowAttributes` 除去 | ✅ |

## 📋 変更ファイル

### NativeOverlayHost.cs（MODIFY）
- `HwndSource` → `CreateWindowEx` によるWin32直接ウィンドウ生成に変更
- `RegisterClassExW` + `CreateWindowExW` で `WS_EX_LAYERED` を確実に保持
- `WndProcDelegate` をフィールド保持（GC保護）
- `SetLayeredWindowAttributes` の初期化呼び出しを除去（`UpdateLayeredWindow` と排他のため）
- デッドコード・二重クランプ・二重ログの全修正
- fallback Window の文字中央配置修正

### NativeOverlayHost.NativeMethods.cs（NEW）
- P/Invoke 宣言の全分離（enum/struct/DllImport）
- `CreateWindowEx` 関連の新規宣言追加（`RegisterClassExW`, `CreateWindowExW`, `DestroyWindow`, `DefWindowProcW`, `UnregisterClassW`, `GetModuleHandle`, `WndProcDelegate`, `WNDCLASSEX`）

## ✅ 検証結果
- ビルド: 0エラー・0警告 ✅
- 実行: `UpdateLayeredWindow ok` ログで native 本線の正常動作を確認 ✅
- fallback 経路に一度も落ちず、100% native 描画で動作 ✅
