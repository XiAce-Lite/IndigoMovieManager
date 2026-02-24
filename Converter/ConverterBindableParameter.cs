using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// BindingやStaticResourceを用いた動的なConverterParameter渡しを実現するためのMarkupExtension。
    /// WPF標準では ConverterParameter にBindingを設定できない(DependencyObjectでない)問題を、
    /// MultiBindingにラップして疑似的にパラメータを渡すことで回避するハック実装。
    ///
    /// 参考: https://stackoverflow.com/questions/15309008/binding-converterparameter
    /// </summary>
    [ContentProperty(nameof(Binding))]
    public class ConverterBindableParameter : MarkupExtension
    {
        #region Public Properties

        /// <summary>主となるバインディング（変換される元の値）</summary>
        public Binding Binding { get; set; }

        /// <summary>バインディングモード（OneWay, TwoWayなど）</summary>
        public BindingMode Mode { get; set; }

        /// <summary>実際に値を変換するIValueConverter</summary>
        public IValueConverter Converter { get; set; }

        /// <summary>動的に渡したいパラメータとなるバインディング</summary>
        public Binding ConverterParameter { get; set; }

        #endregion

        public ConverterBindableParameter() { }

        public ConverterBindableParameter(string path)
        {
            Binding = new Binding(path);
        }

        public ConverterBindableParameter(Binding binding)
        {
            Binding = binding;
        }

        #region Overridden Methods

        /// <summary>
        /// XAMLパーサーがこのMarkupExtensionの値を解決する際に呼ばれる。
        /// 内部で主バインディングとパラメータ用バインディングを MultiBinding に詰め直して返す。
        /// </summary>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 1. 本来のBindingとパラメータ用Bindingをまとめる器(MultiBinding)を作成する
            var multiBinding = new MultiBinding();

            // 2. 主となるバインディングを登録する
            Binding.Mode = Mode;
            multiBinding.Bindings.Add(Binding);

            // 3. パラメータ用バインディングが指定されていれば追加する
            //    (パラメータ自体は表示用読み取り専用とするためにOneWay固定)
            if (ConverterParameter != null)
            {
                ConverterParameter.Mode = BindingMode.OneWay;
                multiBinding.Bindings.Add(ConverterParameter);
            }

            // 4. MultiBindingの複数値を、元々指定されたIValueConverterに繋ぎ直すアダプタを設定する
            var adapter = new MultiValueConverterAdapter { Converter = Converter };
            multiBinding.Converter = adapter;

            // 5. 構築したMultiBindingの解決結果を呼び出し元（XAML側のプロパティ）へ提供する
            return multiBinding.ProvideValue(serviceProvider);
        }

        #endregion

        /// <summary>
        /// MultiBindingから渡される配列(values)を解析し、
        /// values[0]を「変換対象の値」、values[1]を「パラメータ」とみなして、
        /// 本来の IValueConverter に中継するアダプタクラス。
        /// </summary>
        [ContentProperty(nameof(Converter))]
        private class MultiValueConverterAdapter : IMultiValueConverter
        {
            public IValueConverter Converter { get; set; }

            // ConvertBack(逆変換)時に最後のパラメータを再利用するために保持するキャッシュ
            private object lastParameter;

            public object Convert(
                object[] values,
                Type targetType,
                object parameter,
                CultureInfo culture
            )
            {
                // Visual StudioのXAMLデザイナー等でConverterが未設定の場合のフェールセーフ
                if (Converter == null)
                    return values[0];

                // パラメータ(values[1])が存在する場合は取得し、ConvertBack用にキャッシュしておく
                if (values.Length > 1)
                    lastParameter = values[1];

                // 本来のConverterへ、第1要素を値、第2要素をパラメータとして処理を委譲する
                return Converter.Convert(values[0], targetType, lastParameter, culture);
            }

            public object[] ConvertBack(
                object value,
                Type[] targetTypes,
                object parameter,
                CultureInfo culture
            )
            {
                // Visual StudioのXAMLデザイナーフェールセーフ
                if (Converter == null)
                    return [value];

                // 順方向変換(Convert)時に記録しておいた lastParameter を用いて元のIValueConverterの逆変換を呼ぶ
                // MultiBindingへの返却値は配列である必要があるため、配列でラップして返す
                return [Converter.ConvertBack(value, targetTypes[0], lastParameter, culture)];
            }
        }
    }
}
