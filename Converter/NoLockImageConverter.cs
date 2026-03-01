using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// 画像を開いても元ファイルを絶対にロックしない、超絶紳士な最強コンバーター！🎩✨
    /// 「UIでサムネ表示中だからファイルが消せませ〜ん！😭」なんて悲劇は二度と起こさせないぜ！
    /// 偉大なるパクリ元：https://zenn.dev/akid/articles/c05f4e2fed244f
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
            try
            {
                if (Path.Exists(filePath))
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    // OnLoadマジック！ここでサクッとメモリに抱え込んで即ファイル解放だ！さらばロック！👋
                    var decoder = BitmapDecoder.Create(
                        fs,
                        BitmapCreateOptions.None,
                        BitmapCacheOption.OnLoad
                    );
                    var bmp = new WriteableBitmap(decoder.Frames[0]);
                    bmp.Freeze();

                    if (parameter != null)
                    {
                        if ((bool)parameter == false)
                        {
                            // パクリ元へのリスペクトを忘れない！白黒の世界へ染め上げる激シブ変換ロジック！😎
                            // https://maywork.net/computer/wpf-bgr32togray/
                            int w = bmp.PixelWidth;
                            int h = bmp.PixelHeight;
                            int stride = (w * bmp.Format.BitsPerPixel + 7) / 8;
                            int ch = bmp.Format.BitsPerPixel / 8;
                            int bufSize = stride * h;
                            byte[] inputBuf = new byte[bufSize];
                            byte[] outputBuf = new byte[w * h];

                            bmp.CopyPixels(inputBuf, stride, 0);

                            for (int y = 0; y < h; y++)
                            {
                                for (int x = 0; x < w; x++)
                                {
                                    int i = (stride * y) + (x * ch);
                                    int j = w * y + x;

                                    int avg = 0;
                                    for (int k = 0; k < ch; k++)
                                    {
                                        avg += inputBuf[i + k];
                                    }
                                    avg /= ch;

                                    outputBuf[j] = (byte)avg;
                                }
                            }

                            BitmapSource result = BitmapSource.Create(
                                w,
                                h,
                                bmp.DpiX,
                                bmp.DpiY,
                                PixelFormats.Gray8,
                                BitmapPalettes.Gray256,
                                outputBuf,
                                w
                            );
                            return result;
                        }
                    }
                    return bmp;
                }
            }
            catch (Exception)
            {
                return Binding.DoNothing;
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
