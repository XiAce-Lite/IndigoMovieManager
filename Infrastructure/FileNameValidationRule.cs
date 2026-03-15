using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace IndigoMovieManager
{
    /// <summary>
    /// WPF画面のテキストボックス（例: 監視フォルダパスの入力欄など）に対して、
    /// 「空文字の入力を許可しない」などのバリデーション（入力値の検証）を行うためのカスタムルールクラス。
    /// （参考元: https://araramistudio.jimdo.com/2016/09/16/wpf%E3%81%AE%E5%85%A5%E5%8A%9B%E8%A6%8F%E5%88%B6%E3%82%92%E3%82%AB%E3%82%B9%E3%82%BF%E3%83%9E%E3%82%A4%E3%82%BA/）
    /// </summary>
    public class FileNameValidationRule : ValidationRule
    {
        // 必須入力（空入力をエラーとする）かどうか
        public bool NotEmpty { get; set; }

        // エラー発生時に表示するメッセージの接頭辞（例: 「フォルダパス」に「値を入力してください。」）
        public string MessageHeader { get; set; }

        public FileNameValidationRule()
        {
            NotEmpty = false;
            MessageHeader = string.Empty;
        }

        public FileNameValidationRule(bool notEmpty, string messageHeader)
        {
            NotEmpty = notEmpty;
            MessageHeader = messageHeader;
        }

        /// <summary>
        /// バインディングされた値が変更されるたびに呼び出され、入力内容の妥当性をチェックする。
        /// </summary>
        /// <param name="value">入力された値</param>
        /// <returns>検証結果（ValidResultなら正常、エラーを含むオブジェクトなら失敗）</returns>
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            // 処理フロー:
            // 1. まず入力されたオブジェクトが完全にnullかどうか、あるいは空文字列に等しいかを判定する。
            bool isEmpty = (value == null || string.IsNullOrEmpty(value.ToString()));

            // 2. 空またはnullの場合、必須チェック(NotEmpty)が有効ならエラーを生成して返す。
            if (isEmpty)
            {
                if (NotEmpty)
                {
                    var msg = "値を入力してください。";
                    if (!string.IsNullOrWhiteSpace(MessageHeader))
                    {
                        msg = $"{MessageHeader}に{msg}";
                        return new ValidationResult(false, msg);
                    }
                    else
                    {
                        return new ValidationResult(false, msg);
                    }
                }
                else
                {
                    // 空でも良い設定ならそのまま通過させる
                    return ValidationResult.ValidResult;
                }
            }

            // 3. 値がしっかり入っているので検証パスとする。
            return ValidationResult.ValidResult;
        }
    }
}
