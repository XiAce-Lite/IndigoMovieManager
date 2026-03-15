using System.Collections.Generic;

namespace IndigoMovieManager.ModelViews
{
    /// <summary>
    /// ERROR マーカー付き動画を UI で扱いやすくまとめた表示用モデル。
    /// </summary>
    public class ThumbnailErrorRecordViewModel
    {
        public MovieRecords MovieRecord { get; init; }

        public long MovieId { get; init; }

        public string MovieName { get; init; } = "";

        public string MoviePath { get; init; } = "";

        public string FailedTabsText { get; init; } = "";

        public int MarkerCount { get; init; }

        public string LastMarkerWriteTimeText { get; init; } = "";

        public DateTime? LastMarkerWriteTime { get; init; }

        public string ProgressStatusText { get; init; } = "";

        public string ProgressPhaseText { get; init; } = "";

        public string ProgressEngineText { get; init; } = "";

        public string ProgressAttemptText { get; init; } = "";

        public string ProgressDetailText { get; init; } = "";

        public string ProgressUpdatedAtText { get; init; } = "";

        public DateTime? ProgressUpdatedAt { get; init; }

        public string ProgressSummaryKey { get; init; } = "";

        public IReadOnlyList<int> FailedTabIndices { get; init; } = [];
    }
}
