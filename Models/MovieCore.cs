namespace IndigoMovieManager
{
    /// <summary>
    /// MovieInfo と MovieRecords の共通項目をまとめるコアモデル。
    /// 段階移行の受け皿として使い、DB層とUI層の橋渡しを行う。
    /// </summary>
    public class MovieCore
    {
        public long MovieId { get; set; }
        public string MovieName { get; set; } = "";
        public string MoviePath { get; set; } = "";
        public long MovieLength { get; set; }
        public long MovieSize { get; set; }
        public DateTime LastDate { get; set; } = DateTime.Now;
        public DateTime FileDate { get; set; } = DateTime.Now;
        public DateTime RegistDate { get; set; } = DateTime.Now;
        public long Score { get; set; }
        public long ViewCount { get; set; }
        public string Hash { get; set; } = "";
        public string Container { get; set; } = "";
        public string Video { get; set; } = "";
        public string Audio { get; set; } = "";
        public string Extra { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Grouping { get; set; } = "";
        public string Writer { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Track { get; set; } = "";
        public string Camera { get; set; } = "";
        public string CreateTime { get; set; } = "";
        public string Kana { get; set; } = "";
        public string Roma { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Comment1 { get; set; } = "";
        public string Comment2 { get; set; } = "";
        public string Comment3 { get; set; } = "";
        public double FPS { get; set; } = 30;
        public double TotalFrames { get; set; }
    }
}
