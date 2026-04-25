using System.Globalization;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Converter;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブ専用の画像 converter。
    /// 非アクティブ中は再評価を止め、選択中だけ decode を走らせる。
    /// </summary>
    public sealed class UpperTabImageSourceConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Length < 3)
            {
                return DependencyProperty.UnsetValue;
            }

            object moviePathValue = values.Length > 3 ? values[3] : null;
            if (!UpperTabActivationGate.ShouldApplyImageUpdate(values[2], moviePathValue))
            {
                // Recycling されたコンテナへ前の画像が残らないよう、非対象時は明示的に空へ戻す。
                return DependencyProperty.UnsetValue;
            }

            bool isExists = values[1] is not bool exists || exists;
            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            return NoLockImageConverter.ConvertFilePath(values[0] as string, isExists, decodePixelHeight);
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
