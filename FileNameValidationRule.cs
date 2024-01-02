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
    /// パクリ元。https://araramistudio.jimdo.com/2016/09/16/wpf%E3%81%AE%E5%85%A5%E5%8A%9B%E8%A6%8F%E5%88%B6%E3%82%92%E3%82%AB%E3%82%B9%E3%82%BF%E3%83%9E%E3%82%A4%E3%82%BA/
    /// </summary>
    public class FileNameValidationRule : ValidationRule
    {
        public bool NotEmpty { get; set; }
        public string MessageHeader { get; set; }

        public FileNameValidationRule() {
            NotEmpty = false;
            MessageHeader = string.Empty;
        }

        public FileNameValidationRule(bool notEmpty, string messageHeader)
        {
            NotEmpty = notEmpty;
            MessageHeader = messageHeader;
        }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (null == value)
            {
                if (NotEmpty)
                {
                    var msg = "値を入力してください。";
                    if (!string.IsNullOrWhiteSpace(MessageHeader))
                    {
                        msg = MessageHeader + "に" + msg;
                        return new ValidationResult(false, msg);
                    }
                }
                else
                {
                    return ValidationResult.ValidResult;
                }
            }

            string str = value.ToString();
            if (string.IsNullOrEmpty(str))
            {
                if (NotEmpty)
                {
                    var msg = "値を入力してください。";
                    if (!string.IsNullOrWhiteSpace(MessageHeader))
                    {
                        msg = MessageHeader + "に" + msg;
                        return new ValidationResult(false, msg);
                    }
                }
                else
                {
                    return ValidationResult.ValidResult;
                }
            }
            return ValidationResult.ValidResult;
        }
    }
}
