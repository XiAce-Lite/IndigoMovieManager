using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// BindingやStaticResourceを使った動的パラメータ渡しを強引に実現する禁断の黒魔術（MarkupExtension）！🧙‍♂️✨
    /// WPF標準だと ConverterParameter にBindingが弾かれる(DependencyObjectじゃないから)というクソ仕様を、
    /// MultiBindingという器に無理やり押し込んで疑似的に回避する超絶ハックだぜ！
    ///
    /// 大いなる叡智の源: https://stackoverflow.com/questions/15309008/binding-converterparameter
    /// </summary>
    [ContentProperty(nameof(Binding))]
    public class ConverterBindableParameter : MarkupExtension
    {
        #region Public Properties

        /// <summary>主役となるバインディング（変換される元の値だ！）</summary>
        public Binding Binding { get; set; }

        /// <summary>バインディングモード（OneWay, TwoWayなど決戦の舞台！）</summary>
        public BindingMode Mode { get; set; }

        /// <summary>実際に値を叩き直す凄腕の職人（IValueConverter）</summary>
        public IValueConverter Converter { get; set; }

        /// <summary>動的にねじ込みたいパラメータ用のバインディング！ここが肝だ！</summary>
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
        /// XAMLパーサーがこのMarkupExtensionの謎を解き明かす時に呼ばれる召喚魔法！
        /// 内部で主役(Binding)と相棒(Parameter)を「MultiBinding」という一つの器に融合させて叩き返すぜ！💥
        /// </summary>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 1. 本来のBindingとパラメータ用Bindingを悪魔合体させるための器(MultiBinding)を作成！🏺
            var multiBinding = new MultiBinding();

            // 2. 主役となるバインディングをブチ込む！
            Binding.Mode = Mode;
            multiBinding.Bindings.Add(Binding);

            // 3. パラメータ用バインディングが指定されていれば相棒として追加！
            //    (パラメータ自体は表示用・読み取り専用にするためOneWayに固定だ！安全第一！👷)
            if (ConverterParameter != null)
            {
                ConverterParameter.Mode = BindingMode.OneWay;
                multiBinding.Bindings.Add(ConverterParameter);
            }

            // 4. MultiBindingの複数値を、元々指定された凄腕のConverterに繋ぎ直す専用アダプタを装着！🔌
            var adapter = new MultiValueConverterAdapter { Converter = Converter };
            multiBinding.Converter = adapter;

            // 5. 完璧に構築されたMultiBindingの完成品を、呼び出し元（XAML側のプロパティ）へドヤ顔で提供だ！😤
            return multiBinding.ProvideValue(serviceProvider);
        }

        #endregion

        /// <summary>
        /// MultiBindingから渡される配列(values)を瞬時に解析し、
        /// 面倒な「values[0]=値」「values[1]=パラメータ」の仕分け作業をやってのけ、
        /// 本来の IValueConverter 先生へ綺麗な形でパスを出す最強の中継ぎ（アダプタ）クラスだ！⚾💨
        /// </summary>
        [ContentProperty(nameof(Converter))]
        private class MultiValueConverterAdapter : IMultiValueConverter
        {
            public IValueConverter Converter { get; set; }

            // ConvertBack(逆変換)の時にもう一度必要になるパラメータを、こっそりポケットに隠し持っておくキャッシュ領域🤫
            private object lastParameter;

            public object Convert(
                object[] values,
                Type targetType,
                object parameter,
                CultureInfo culture
            )
            {
                // Visual StudioのXAMLデザイナー等でConverterが未設定だった場合のフェールセーフ（そのまま返すぜ！）
                if (Converter == null)
                    return values[0];

                // パラメータ(values[1])が存在する場合は引っこ抜いて、ConvertBack用にキャッシュしておく！📝
                if (values.Length > 1)
                    lastParameter = values[1];

                // 本来のConverter先生へ「第1要素=値」「第2要素=パラメータ」として仕事を丸投げだ！よろしく！🙏
                return Converter.Convert(values[0], targetType, lastParameter, culture);
            }

            public object[] ConvertBack(
                object value,
                Type[] targetTypes,
                object parameter,
                CultureInfo culture
            )
            {
                // Visual StudioのXAMLデザイナーフェールセーフ（エラーで落ちないための気遣い！）
                if (Converter == null)
                    return [value];

                // 順方向変換(Convert)時にこっそり記録しておいた lastParameter を使って元の魔法(逆変換)をかける！
                // MultiBindingへの返却値は規約で配列である必要があるから、サクッと配列で包んで返すぜ！🎁
                return [Converter.ConvertBack(value, targetTypes[0], lastParameter, culture)];
            }
        }
    }
}
