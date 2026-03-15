using System.Text;

namespace DelayedClipboardApp;

/// <summary>
/// Generates tabular content in plain text and HTML formats.
///
/// Each cell contains "Cell(row,col)" where row and col are 1-based indices.
/// The plain text format uses tab separators (suitable for pasting into Excel).
/// The HTML format uses the Windows "HTML Format" clipboard specification.
/// </summary>
internal static class ContentGenerator
{
    /// <summary>
    /// Generates a plain text table with tab-separated columns and newline-separated rows.
    ///
    /// Example output for 3 rows × 4 columns:
    ///   Cell(1,1)\tCell(1,2)\tCell(1,3)\tCell(1,4)
    ///   Cell(2,1)\tCell(2,2)\tCell(2,3)\tCell(2,4)
    ///   Cell(3,1)\tCell(3,2)\tCell(3,3)\tCell(3,4)
    /// </summary>
    /// <param name="rows">Number of rows in the table (1-based).</param>
    /// <param name="columns">Number of columns in the table (1-based).</param>
    /// <returns>Tab-delimited plain text representation of the table.</returns>
    public static string GeneratePlainText(int rows, int columns)
    {
        var sb = new StringBuilder();

        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= columns; c++)
            {
                // Add tab separator between columns (but not before the first column)
                if (c > 1)
                    sb.Append('\t');

                sb.Append($"Cell({r},{c})");
            }

            // Add newline between rows (but not after the last row)
            if (r < rows)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates HTML table content wrapped in the Windows HTML clipboard format.
    ///
    /// The Windows HTML clipboard format requires a specific header with byte offsets
    /// that point to different sections of the HTML content. The format is:
    ///
    ///   Version:0.9
    ///   StartHTML:0000000XXX      (byte offset to &lt;html&gt;)
    ///   EndHTML:0000000XXX        (byte offset to end of &lt;/html&gt;)
    ///   StartFragment:0000000XXX  (byte offset to the start of the actual content)
    ///   EndFragment:0000000XXX    (byte offset to the end of the actual content)
    ///   &lt;html&gt;
    ///   &lt;body&gt;
    ///   &lt;!--StartFragment--&gt;...table HTML...&lt;!--EndFragment--&gt;
    ///   &lt;/body&gt;
    ///   &lt;/html&gt;
    ///
    /// All byte offsets are calculated using UTF-8 encoding, as that is what
    /// the HTML clipboard format uses.
    ///
    /// See: https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format
    /// </summary>
    /// <param name="rows">Number of rows in the table (1-based).</param>
    /// <param name="columns">Number of columns in the table (1-based).</param>
    /// <returns>Complete HTML clipboard format string ready for the clipboard.</returns>
    public static string GenerateHtmlClipboardData(int rows, int columns)
    {
        // Step 1: Build the HTML table fragment
        var table = new StringBuilder();
        table.AppendLine("<table>");
        for (int r = 1; r <= rows; r++)
        {
            table.Append("  <tr>");
            for (int c = 1; c <= columns; c++)
            {
                table.Append($"<td>Cell({r},{c})</td>");
            }
            table.AppendLine("</tr>");
        }
        table.Append("</table>");
        string fragment = table.ToString();

        // Step 2: Define the HTML wrapper around the fragment
        string htmlPrefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
        string htmlSuffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

        // Step 3: Build the header template with 10-character placeholders.
        // Using exactly 10 X's ensures that when we replace them with
        // zero-padded 10-digit numbers, the header length stays constant.
        // This is critical because the header length affects all the offsets.
        string headerTemplate =
            "Version:0.9\r\n" +
            "StartHTML:XXXXXXXXXX\r\n" +
            "EndHTML:XXXXXXXXXX\r\n" +
            "StartFragment:XXXXXXXXXX\r\n" +
            "EndFragment:XXXXXXXXXX\r\n";

        // Step 4: Calculate byte offsets in UTF-8 encoding.
        // The offsets are measured from the very beginning of the string.
        int headerByteCount = Encoding.UTF8.GetByteCount(headerTemplate);
        int startHtml = headerByteCount;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlSuffix);

        // Step 5: Replace placeholders with actual zero-padded byte offsets.
        // D10 format specifier produces a 10-digit zero-padded number,
        // matching the 10 X's in the template exactly.
        string header = headerTemplate
            .Replace("StartHTML:XXXXXXXXXX", $"StartHTML:{startHtml:D10}")
            .Replace("EndHTML:XXXXXXXXXX", $"EndHTML:{endHtml:D10}")
            .Replace("StartFragment:XXXXXXXXXX", $"StartFragment:{startFragment:D10}")
            .Replace("EndFragment:XXXXXXXXXX", $"EndFragment:{endFragment:D10}");

        // Step 6: Assemble the complete HTML clipboard data
        return header + htmlPrefix + fragment + htmlSuffix;
    }
}
