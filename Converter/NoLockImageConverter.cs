using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// Imageをロックしないコンバーター。パクリ元：https://zenn.dev/akid/articles/c05f4e2fed244f
    /// </summary>
    internal class NoLockImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var filePath = value as string;
            if (string.IsNullOrEmpty(filePath))
            {
                return Binding.DoNothing;
            }

            if (Path.Exists(filePath)) {
                using var fs = new FileStream(filePath, FileMode.Open,FileAccess.Read);
                // OnLoadにする
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var bmp = new WriteableBitmap(decoder.Frames[0]);
                bmp.Freeze();
                return bmp;
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
