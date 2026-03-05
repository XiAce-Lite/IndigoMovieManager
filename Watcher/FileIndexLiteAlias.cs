#if USE_EXTERNAL_EVERYTHINGLITE
// 外部EverythingLiteを使う場合は既存名前空間へ接続する。
global using Lite = EverythingLite;
#else
// 内包版は技術名ベースのUsnMft名前空間へ接続する。
global using Lite = IndigoMovieManager.FileIndex.UsnMft;
#endif
