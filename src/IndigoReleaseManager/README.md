# IndigoReleaseManager

最終更新日: 2026-04-05

## 1. これは何か

`IndigoReleaseManager` は、開発者向けの配布オーケストレータである。

役割は次に絞る。

- Public / Private repo の current state を表示する
- `Private release`
- `Public preview`
- `Public release`

の 3 手順を、既存 script の正面入口としてまとめる

## 2. v1 の current state

2026-04-05 時点では、次まで入っている。

- `src/IndigoReleaseManager` の独立 WPF app
- Public / Private repo 情報の再取得
- `gh auth` / token 状態の表示
- `Private local build / publish / pack`
- `Public preview`
- `Public release`

補足:

- Private 側は v1 では local build / publish / package 実行までを正面入口にしている
- 実行後は Public repo の prepared dir へそのまま同期するので、続けて `Public release` へ進める
- Public 側は既存の `scripts/invoke_github_release_preview.ps1` と `scripts/invoke_release.ps1` をそのまま呼ぶ
- build / publish / release ロジックは app 側で再実装しない
- `ReleaseBranch` は v1 では branch 切替には使わず、現在 branch と一致しているかの安全確認に使う
- 起動直後の `private_engine_release_tag`、preview URL、release URL は空で始まり、実行後に埋まる

## 3. 実行

例:

```powershell
dotnet msbuild src/IndigoReleaseManager/IndigoReleaseManager.csproj /restore /p:Configuration=Debug /p:Platform=x64
```

出力:

- `src/IndigoReleaseManager/bin/x64/Debug/net8.0-windows10.0.19041.0/IndigoReleaseManager.exe`

## 4. 関連資料

- `scripts/Implementation Plan_IndigoReleaseManager導入_2026-04-05.md`
- `scripts/運用ガイド_開発者向け配布手順の時系列解説_2026-04-05.md`
- `scripts/運用メモ_WiX生成物とGitHubRelease反映_2026-04-05.md`
