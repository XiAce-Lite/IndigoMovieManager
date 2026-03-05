# Flowchart（動画情報取得・サムネイル作成・ハッシュ作成タイミング 2026-03-04）

## 1. 全体フロー（現行コード）
```mermaid
flowchart TD
    A[監視イベント or フォルダ再走査] --> B{DBに動画が既登録?}

    B -->|未登録| C[MovieInfo生成 noHash=false]
    C --> C1[動画情報取得 FPS/尺/Codec/サイズ]
    C --> C2[ハッシュ作成 CRC32 先頭128KB]
    C2 --> D[movieテーブルへ登録 hash含む]
    D --> E[キュー投入 QueueObjにhash設定]

    B -->|登録済み| F[DBのhashで想定サムネパス作成]
    F --> G{サムネイル存在?}
    G -->|なし| E
    G -->|あり| Z[スキップ]

    E --> H[QueueRequestをQueueDBへ永続化]
    H --> H1[QueueDBは現在hash未保持]
    H1 --> I[QueueProcessorがリース取得]
    I --> J[MainWindow.CreateThumbAsync]

    J --> J1[MovieId/Hash補完]
    J1 --> J2[MainVM MovieRecsを参照]
    J1 --> J3[DB movie_path一致で補完]
    J2 --> K[ThumbnailCreationService.CreateThumbAsync]
    J3 --> K

    K --> L[GetCachedMovieMeta moviePath hashHint]
    L --> M{メタキャッシュ有無}
    M -->|あり| M1[キャッシュhash/duration/DRM使用]
    M -->|なし| M2[初期化]
    M2 --> M21{hashHintあり?}
    M21 -->|あり| M22[hint hashを採用]
    M21 -->|なし| M23[GetHashCRC32実行]
    M2 --> M24{wmv or asf}
    M24 -->|yes| M25[DRM判定 TryDetectAsfDrmProtected]
    M24 -->|no| M26[DRM判定なし]

    M1 --> N[出力名決定 動画名 hash jpg]
    M22 --> N
    M23 --> N

    N --> O{DRM疑い?}
    O -->|yes| O1[DRMプレースホルダー作成して終了]
    O -->|no| P{duration取得済み?}

    P -->|未取得| Q[TryGetDurationSec]
    Q --> Q1[AppVideoMetadataProvider: MovieInfo noHash=true]
    Q1 --> Q2[必要時 Shell fallback]
    Q2 --> R[durationをキャッシュ]
    P -->|取得済み| S[エンジン選択]
    R --> S

    S --> T[サムネイル生成実行]
    T --> U{成功?}
    U -->|成功| V[保存・UI反映・ログ]
    U -->|失敗| W[再試行 or Failed更新]
```

## 2. タイミング要点
- ハッシュ作成タイミング1（DB未登録時）  
  `MovieInfo(noHash=false)` 生成時に作成。
- ハッシュ作成タイミング2（サムネイル生成時）  
  `ThumbnailCreationService.GetCachedMovieMeta` のキャッシュミス時。  
  ただし `hashHint`（Queue/UI/DB補完）取得済みなら再計算しない。
- WMV/ASFのDRM判定タイミング  
  `GetCachedMovieMeta` 初期化時（= ハッシュ決定と同じタイミング）で実行。
- 動画情報取得タイミング  
  - DB未登録時: `MovieInfo` でFPS/尺/Codec等を取得。  
  - サムネ生成時: duration不足時のみ `videoMetadataProvider.TryGetDurationSec`（`noHash=true` 経路）。
- サムネイル作成タイミング  
  QueueProcessor がQueueDBからジョブをリース後、`CreateThumbAsync` を実行。

## 3. 補足（命名ゆれ対策）
- 以前は `hash` が空のまま到達すると `動画名.#.jpg` になり得た。
- 現在は `CreateThumbAsync` 前段で `MovieRecs` またはDBから `hash` を補完し、命名ゆれを抑制。
- それでも「DB側hash空 かつ ハッシュ読取不可」の場合のみ、空hash名になる可能性は残る。

## 4. 参照コード
- `Watcher/MainWindow.Watcher.cs`
- `Models/MovieInfo.cs`
- `Thumbnail/Tools.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/ThumbnailCreationService.cs`
