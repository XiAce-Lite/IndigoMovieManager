using System.IO;

namespace IndigoMovieManager
{
    public class TabInfo
    {
        private readonly int columns = 3;
        private readonly int rows = 1;
        private readonly int width = 120;
        private readonly int height = 90;
        private readonly int divCount = 0;
        private readonly string outPath = "";

        public int Columns => columns;
        public int Rows => rows;
        public int Width => width;
        public int Height => height;
        public int DivCount => divCount;
        public string OutPath => outPath;

        public TabInfo(int tabIndex, string dbName, string thumbFolder = "")
        {
            switch (tabIndex)
            {
                case 0:
                    width = 120;
                    height = 90;
                    columns = 3;
                    rows = 1;
                    break;
                case 1:
                    width = 200;
                    height = 150;
                    columns = 3;
                    rows = 1;
                    break;
                case 2:
                    width = 160;
                    height = 120;
                    columns = 1;
                    rows = 1;
                    break;
                case 3:
                    width = 56;
                    height = 42;
                    columns = 5;
                    rows = 1;
                    break;
                case 4:
                    width = 120;
                    height = 90;
                    columns = 5;
                    rows = 2;
                    break;
                default:
                    break;
            }
            divCount = columns * rows;
            if (thumbFolder == "")
            {
                outPath = Path.Combine(Directory.GetCurrentDirectory(), "Thumb", dbName, $"{width}x{height}x{columns}x{rows}");
            }
            else
            {
                outPath = Path.Combine(thumbFolder, $"{width}x{height}x{columns}x{rows}");
            }
        }
    }
}
