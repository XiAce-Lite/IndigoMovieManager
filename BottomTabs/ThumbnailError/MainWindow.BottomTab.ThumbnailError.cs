using System.Windows.Controls;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ThumbnailErrorBottomTabContentId = "ToolThumbnailError";

        private DataGrid GetThumbnailErrorDataGrid()
        {
            return ThumbnailErrorBottomTabViewHost?.ErrorListDataGridControl;
        }
    }
}
