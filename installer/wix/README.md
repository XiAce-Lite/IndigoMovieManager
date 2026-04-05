# installer/wix

このフォルダは、`scripts/create_github_release_package.ps1` が作る verify 済み app package を入力にして、
`WiX v6` で installer を組み立てるための骨格を置く場所です。

方針:
- installer は app package の後段工程だけを担当する
- worker / engine package の sync や再 publish はここで行わない
- package 直下の `rescue-worker.lock.json` と `privateEnginePackages` を provenance 正本のまま継承する

v1 の範囲:
- `MSI + Burn bundle` の skeleton
- per-user install 前提の install / upgrade / uninstall
- 既存 ZIP と並ぶ bundle exe の生成入口
- `scripts/create_wix_installer_from_release_package.ps1` からの local proof

v1 の範囲外:
- self-update
- `UpdateApplyBridge`
- custom managed BA
- uninstall 時の保持項目 UI
- `.NET Desktop Runtime` prerequisite の本組み込み

補足:
- 2026-04-05 時点で、`scripts/create_wix_installer_from_release_package.ps1` から
  verify 済み package を入力に `MSI + bundle exe` 生成まで local 成功している
- per-user harvest は ICE38 / ICE64 と衝突するため、v1 では `SuppressValidation=true`
  を使って downstream proof を優先する
