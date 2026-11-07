using System.Text.RegularExpressions;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Pure static ANSI (SGR) formatter for runtime log lines. llama-server with
/// --log-colors on emits basic color codes (ESC [0 m, ESC [1 m, ESC [31..37 m)
/// even when stderr is redirected (piped), so raw lines carry escape sequences.
/// Format() renders one raw line into a FormattedString whose spans carry the
/// mapped color; Strip() removes the sequences for the clipboard copy text.
/// </summary>
public static class AnsiLogFormatter
{
    /// <summary>
    /// Basic SGR sequences "ESC [&lt;params&gt; m". llama.cpp emits single codes
    /// (0 = reset, 1 = bold, 31..37 = basic colors); compound codes are handled
    /// anyway by splitting on ';'.
    /// </summary>
    private static readonly Regex AnsiCode = new(@"\x1b\[(?<code>[\d;]*)m", RegexOptions.Compiled);

    // Log panel palette (Overview log: background #1E1E1E, default text #D4D4D4);
    // the ANSI colors are picked to stay readable on that dark background.
    public static readonly Color DefaultText = Color.FromRgb(212, 212, 212); // #D4D4D4
    private static readonly Color Red = Color.FromRgb(255, 107, 107);        // #FF6B6B
    private static readonly Color Green = Color.FromRgb(81, 207, 102);       // #51CF66
    private static readonly Color Yellow = Color.FromRgb(255, 212, 59);      // #FFD43B
    private static readonly Color Blue = Color.FromRgb(116, 192, 252);       // #74C0FC
    private static readonly Color Magenta = Color.FromRgb(218, 119, 242);    // #DA77F2
    private static readonly Color Cyan = Color.FromRgb(102, 217, 232);       // #66D9E8

    /// <summary>
    /// Render one raw log line (ANSI sequences included) into a FormattedString.
    /// Every span gets an explicit color (the default log text color when no ANSI
    /// color is active), so the rendering never depends on label color inheritance.
    /// A line without sequences yields a single plain span.
    /// </summary>
    public static FormattedString Format(string raw)
    {
        var result = new FormattedString();
        if (raw.Length == 0)
        {
            return result;
        }

        var color = DefaultText;
        var start = 0;
        foreach (Match match in AnsiCode.Matches(raw))
        {
            AppendSpan(result, raw, start, match.Index, color);
            color = ApplySgr(match.Groups["code"].Value, color);
            start = match.Index + match.Length;
        }

        AppendSpan(result, raw, start, raw.Length, color);
        return result;
    }

    /// <summary>Raw line text without the ANSI escape sequences (clipboard copy).</summary>
    public static string Strip(string raw) => AnsiCode.Replace(raw, string.Empty);

    private static void AppendSpan(FormattedString target, string raw, int from, int to, Color color)
    {
        if (to <= from)
        {
            return;
        }

        // MAUI 10: the span type of FormattedString is Microsoft.Maui.Controls.Span.
        target.Spans.Add(new Span
        {
            Text = raw.AsSpan(from, to - from).ToString(),
            TextColor = color
        });
    }

    /// <summary>
    /// Apply one SGR code group to the current color. 0/39 reset to the default
    /// text color, 31..37 map to the palette; bold (1) and any other codes are
    /// ignored (the text stays, only the color is tracked).
    /// </summary>
    private static Color ApplySgr(string code, Color current)
    {
        foreach (var part in code.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var sgr))
            {
                continue;
            }

            current = sgr switch
            {
                0 or 39 => DefaultText,
                31 => Red,
                32 => Green,
                33 => Yellow,
                34 => Blue,
                35 => Magenta,
                36 => Cyan,
                37 => DefaultText,
                _ => current
            };
        }

        return current;
    }
}
