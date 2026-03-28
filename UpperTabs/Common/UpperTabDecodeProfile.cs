namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブごとのサムネ decode 目安をまとめる。
    /// 元サムネ生成サイズに合わせ、無駄な原寸 decode を避ける。
    /// </summary>
    public static class UpperTabDecodeProfile
    {
        public static int SmallDecodePixelHeight => 90;

        public static int BigDecodePixelHeight => 150;

        public static int GridDecodePixelHeight => 120;

        public static int ListDecodePixelHeight => 42;

        public static int Big10DecodePixelHeight => 180;
    }
}
