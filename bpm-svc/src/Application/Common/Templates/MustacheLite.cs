using System.Text.RegularExpressions;

namespace Bpm.Application.Common.Templates;

/// Tiny {{ variable }} substitution. Supports dotted keys looked up in a flat
/// dictionary (e.g. "purchase.amount"). Missing keys render as empty string.
public static class MustacheLite
{
    private static readonly Regex Token = new(@"\{\{\s*([\w\.]+)\s*\}\}", RegexOptions.Compiled);

    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return Token.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return values.TryGetValue(key, out var v) ? v ?? "" : "";
        });
    }
}
