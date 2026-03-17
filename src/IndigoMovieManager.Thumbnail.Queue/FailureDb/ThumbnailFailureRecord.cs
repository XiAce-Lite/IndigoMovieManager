namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // 初版は粗い固定分類で十分とし、重い判定は救済exeへ寄せる。
    public enum ThumbnailFailureKind
    {
        None = 0,
        DrmProtected = 1,
        UnsupportedCodec = 2,
        IndexCorruption = 3,
        TransientDecodeFailure = 4,
        NoVideoStream = 5,
        FileLocked = 6,
        FileMissing = 7,
        ZeroByteFile = 8,
        HangSuspected = 9,
        Unknown = 10,
    }

    // FailureDbへappendする1試行分のレコード。
    public sealed class ThumbnailFailureRecord
    {
        public long FailureId { get; set; }
        public string MainDbFullPath { get; set; } = "";
        public string MainDbPathHash { get; set; } = "";
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public int TabIndex { get; set; }
        public string Lane { get; set; } = "";
        public string AttemptGroupId { get; set; } = "";
        public int AttemptNo { get; set; }
        public string Status { get; set; } = "";
        public string LeaseOwner { get; set; } = "";
        public string LeaseUntilUtc { get; set; } = "";
        public string Engine { get; set; } = "";
        public ThumbnailFailureKind FailureKind { get; set; } = ThumbnailFailureKind.Unknown;
        public string FailureReason { get; set; } = "";
        public long ElapsedMs { get; set; }
        public string SourcePath { get; set; } = "";
        public string OutputThumbPath { get; set; } = "";
        public bool RepairApplied { get; set; }
        public string ResultSignature { get; set; } = "";
        public string ExtraJson { get; set; } = "";
        public ThumbnailQueuePriority Priority { get; set; } = ThumbnailQueuePriority.Normal;
        public string PriorityUntilUtc { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
