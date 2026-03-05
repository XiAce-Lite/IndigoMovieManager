using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    internal static class AppStructuredLog
    {
        public static void Info(string eventId, string message, params (string Key, object Value)[] fields)
        {
            Trace.TraceInformation(Build("INFO", eventId, message, fields));
        }

        public static void Warn(string eventId, string message, params (string Key, object Value)[] fields)
        {
            Trace.TraceWarning(Build("WARN", eventId, message, fields));
        }

        public static void Error(string eventId, string message, params (string Key, object Value)[] fields)
        {
            Trace.TraceError(Build("ERROR", eventId, message, fields));
        }

        private static string Build(string level, string eventId, string message, params (string Key, object Value)[] fields)
        {
            var sb = new StringBuilder();
            sb.Append("level=").Append(level);
            sb.Append(" eventId=").Append(Normalize(eventId));
            sb.Append(" message=\"").Append(Normalize(message)).Append("\"");

            if (fields == null || fields.Length == 0)
            {
                return sb.ToString();
            }

            foreach (var field in fields.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                sb.Append(' ');
                sb.Append(field.Key.Trim());
                sb.Append('=');
                sb.Append(FormatValue(field.Value));
            }

            return sb.ToString();
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace('"', '\'').Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string FormatValue(object value)
        {
            var text = Normalize(value == null ? string.Empty : Convert.ToString(value));
            if (text.Length == 0)
            {
                return "\"\"";
            }

            if (text.Any(char.IsWhiteSpace) || text.Contains("="))
            {
                return "\"" + text + "\"";
            }

            return text;
        }
    }
}
