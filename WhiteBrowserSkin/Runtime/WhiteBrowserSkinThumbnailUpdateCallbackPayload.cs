using System.Text.Json.Serialization;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// onUpdateThum callback へ渡す通知契約を 1 か所で固定する。
    /// JS 側は payload 参照でも引数参照でも受けられるよう、両方を持たせる。
    /// </summary>
    public sealed class WhiteBrowserSkinThumbnailUpdateCallbackPayload
    {
        [JsonPropertyName("dbIdentity")]
        public string DbIdentity { get; init; } = "";

        [JsonPropertyName("movieId")]
        public long MovieId { get; init; }

        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("recordKey")]
        public string RecordKey { get; init; } = "";

        [JsonPropertyName("moviePath")]
        public string MoviePath { get; init; } = "";

        [JsonPropertyName("thumbUrl")]
        public string ThumbUrl { get; init; } = "";

        [JsonPropertyName("thum")]
        public string Thum { get; init; } = "";

        [JsonPropertyName("thumbRevision")]
        public string ThumbRevision { get; init; } = "";

        [JsonPropertyName("thumbSourceKind")]
        public string ThumbSourceKind { get; init; } = "";

        [JsonPropertyName("thumbNaturalWidth")]
        public int ThumbNaturalWidth { get; init; }

        [JsonPropertyName("thumbNaturalHeight")]
        public int ThumbNaturalHeight { get; init; }

        [JsonPropertyName("thumbSheetColumns")]
        public int ThumbSheetColumns { get; init; } = 1;

        [JsonPropertyName("thumbSheetRows")]
        public int ThumbSheetRows { get; init; } = 1;

        [JsonPropertyName("sizeInfo")]
        public WhiteBrowserSkinThumbnailSizeInfoPayload SizeInfo { get; init; } = new();

        [JsonPropertyName("__immCallArgs")]
        public object[] CompatCallArgs { get; init; } = [];

        public static WhiteBrowserSkinThumbnailUpdateCallbackPayload Create(
            WhiteBrowserSkinThumbnailContractDto contract
        )
        {
            ArgumentNullException.ThrowIfNull(contract);

            WhiteBrowserSkinThumbnailSizeInfoPayload sizeInfo = new()
            {
                ThumbNaturalWidth = contract.ThumbNaturalWidth,
                ThumbNaturalHeight = contract.ThumbNaturalHeight,
                ThumbSheetColumns = contract.ThumbSheetColumns,
                ThumbSheetRows = contract.ThumbSheetRows,
            };

            return new WhiteBrowserSkinThumbnailUpdateCallbackPayload
            {
                DbIdentity = contract.DbIdentity ?? "",
                MovieId = contract.MovieId,
                Id = contract.MovieId,
                RecordKey = contract.RecordKey ?? "",
                MoviePath = contract.MoviePath ?? "",
                ThumbUrl = contract.ThumbUrl ?? "",
                Thum = contract.ThumbUrl ?? "",
                ThumbRevision = contract.ThumbRevision ?? "",
                ThumbSourceKind = contract.ThumbSourceKind ?? "",
                ThumbNaturalWidth = contract.ThumbNaturalWidth,
                ThumbNaturalHeight = contract.ThumbNaturalHeight,
                ThumbSheetColumns = contract.ThumbSheetColumns,
                ThumbSheetRows = contract.ThumbSheetRows,
                SizeInfo = sizeInfo,
                // 旧 WB callback は引数ベースでも使えるようにしておく。
                CompatCallArgs =
                [
                    contract.RecordKey ?? "",
                    contract.ThumbUrl ?? "",
                    contract.ThumbRevision ?? "",
                    contract.ThumbSourceKind ?? "",
                    sizeInfo,
                ],
            };
        }
    }

    public sealed class WhiteBrowserSkinThumbnailSizeInfoPayload
    {
        [JsonPropertyName("thumbNaturalWidth")]
        public int ThumbNaturalWidth { get; init; }

        [JsonPropertyName("thumbNaturalHeight")]
        public int ThumbNaturalHeight { get; init; }

        [JsonPropertyName("thumbSheetColumns")]
        public int ThumbSheetColumns { get; init; } = 1;

        [JsonPropertyName("thumbSheetRows")]
        public int ThumbSheetRows { get; init; } = 1;

        [JsonPropertyName("naturalWidth")]
        public int NaturalWidth => ThumbNaturalWidth;

        [JsonPropertyName("naturalHeight")]
        public int NaturalHeight => ThumbNaturalHeight;

        [JsonPropertyName("sheetColumns")]
        public int SheetColumns => ThumbSheetColumns;

        [JsonPropertyName("sheetRows")]
        public int SheetRows => ThumbSheetRows;
    }
}
