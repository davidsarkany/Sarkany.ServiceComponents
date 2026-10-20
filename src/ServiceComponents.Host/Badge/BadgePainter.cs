using System.Net;

namespace ServiceComponents.Host.Badge
{
    /// <summary>
    /// Draws shields.io style ("plastic") SVG badges. Text widths are estimated from a Verdana 11px width table, so no
    /// System.Drawing (unsupported on Linux, vulnerable in the versions that ran there) is needed.
    /// </summary>
    internal static class BadgePainter
    {
        public const string Green = "#4c1";
        public const string Red = "#e05d44";
        public const string Blue = "#007ec6";
        public const string Grey = "#555";

        public static string Draw(string label, string value, string color)
        {
            label ??= string.Empty;
            value ??= string.Empty;
            var labelWidth = TextWidth(label) + 10;
            var valueWidth = TextWidth(value) + 10;
            var width = labelWidth + valueWidth;
            var labelText = WebUtility.HtmlEncode(label);
            var valueText = WebUtility.HtmlEncode(value);
            var fill = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(color) ? Grey : color);

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"18\" role=\"img\" aria-label=\"{labelText}: {valueText}\">" +
                   $"<title>{labelText}: {valueText}</title>" +
                   "<linearGradient id=\"s\" x2=\"0\" y2=\"100%\"><stop offset=\"0\" stop-color=\"#fff\" stop-opacity=\".7\"/><stop offset=\".1\" stop-color=\"#aaa\" stop-opacity=\".1\"/><stop offset=\".9\" stop-opacity=\".3\"/><stop offset=\"1\" stop-opacity=\".5\"/></linearGradient>" +
                   $"<clipPath id=\"r\"><rect width=\"{width}\" height=\"18\" rx=\"4\" fill=\"#fff\"/></clipPath>" +
                   $"<g clip-path=\"url(#r)\"><rect width=\"{labelWidth}\" height=\"18\" fill=\"#555\"/><rect x=\"{labelWidth}\" width=\"{valueWidth}\" height=\"18\" fill=\"{fill}\"/><rect width=\"{width}\" height=\"18\" fill=\"url(#s)\"/></g>" +
                   "<g fill=\"#fff\" text-anchor=\"middle\" font-family=\"Verdana,Geneva,DejaVu Sans,sans-serif\" font-size=\"11\">" +
                   $"<text x=\"{labelWidth / 2.0:0.0}\" y=\"13\">{labelText}</text>" +
                   $"<text x=\"{labelWidth + valueWidth / 2.0:0.0}\" y=\"13\">{valueText}</text>" +
                   "</g></svg>";
        }

        // Approximate advance widths of Verdana 11px, as used by shields.io.
        private static int TextWidth(string text)
        {
            var width = 0.0;
            foreach (var c in text) {
                width += c switch {
                    ' ' => 3.9,
                    'i' or 'l' or 'j' or '!' or '.' or ',' or ':' or ';' or '\'' or '|' => 3.1,
                    'f' or 't' or 'r' or 'I' or '(' or ')' or '[' or ']' or '-' => 4.6,
                    'm' or 'w' or 'M' or 'W' => 10.5,
                    >= 'A' and <= 'Z' => 7.5,
                    >= '0' and <= '9' => 7.0,
                    _ => 6.6
                };
            }

            return (int)System.Math.Ceiling(width);
        }
    }
}
