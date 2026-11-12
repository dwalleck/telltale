using System.Buffers;
using System.Globalization;
using System.Text;
using Telltale.Core.Storage;

namespace Telltale.Reporting;

/// <summary>How text and rows from the store are written as markdown.</summary>
public static class Markdown
{
    private static readonly SearchValues<char> MeaningfulCharacters = SearchValues.Create("\\`*_[]<>|#~!&");

    /// <summary>
    /// Text from Jira (a sprint name, a status name) as markdown that shows it as it was typed: every
    /// character markdown or HTML gives a meaning is escaped, and a line break or other control character
    /// becomes one space, so the text can add no heading, link, image, tag or table cell and stays on its line.
    /// A report's own cells (figures, "suppressed") hold none of these characters and pass through unchanged.
    /// </summary>
    public static string Text(string text)
    {
        var escaped = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsControl(character))
            {
                // A run of them (a CR and an LF) is one space.
                if (escaped.Length == 0 || escaped[^1] != ' ')
                {
                    escaped.Append(' ');
                }

                continue;
            }

            // An underscore inside a word (a column name such as sprint_id) means nothing to markdown.
            var insideAWord = character == '_' && index > 0 && index < text.Length - 1 && char.IsLetterOrDigit(text[index - 1]) && char.IsLetterOrDigit(text[index + 1]);
            if (MeaningfulCharacters.Contains(character) && !insideAWord)
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    /// <summary>
    /// The rows of a query over the metric views as a markdown table: a head with the column names, then one
    /// line per row. A value the view withholds or does not have is written <c>NULL</c>; a number is written in
    /// full, in invariant form; text is written with <see cref="Text"/>.
    /// </summary>
    public static string Table(ViewRows rows)
    {
        var table = new StringBuilder();
        AppendRow(table, rows.Columns.Select(Text));
        AppendRow(table, rows.Columns.Select(_ => "---"));
        foreach (var row in rows.Rows)
        {
            AppendRow(table, row.Select(Cell));
        }

        return table.ToString();
    }

    private static void AppendRow(StringBuilder table, IEnumerable<string> cells)
    {
        foreach (var cell in cells)
        {
            table.Append("| ").Append(cell).Append(' ');
        }

        table.Append("|\n");
    }

    private static string Cell(object? value) => value switch
    {
        null => "NULL",
        string text => Text(text),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Text(value.ToString() ?? ""),
    };
}
