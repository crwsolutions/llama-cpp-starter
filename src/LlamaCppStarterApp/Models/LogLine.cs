using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.Models;

/// <summary>
/// One runtime log line for the Overview log CollectionView: the text without
/// ANSI escape sequences (clipboard copy) plus the rendered FormattedString
/// (the ANSI colors from --log-colors on mapped to span colors).
/// </summary>
public sealed class LogLine
{
    public LogLine(string raw)
    {
        Raw = AnsiLogFormatter.Strip(raw);
        Text = AnsiLogFormatter.Format(raw);
    }

    /// <summary>Line text without the ANSI escape sequences (clipboard copy).</summary>
    public string Raw { get; }

    /// <summary>Rendered text; the spans carry the ANSI colors.</summary>
    public FormattedString Text { get; }
}
