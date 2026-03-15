# Windows HTML Clipboard Format

## Overview

Windows does not use raw HTML for clipboard data. Instead, it uses a specific format called **"HTML Format"** that wraps the HTML content in a header containing UTF-8 byte offsets. This allows clipboard viewers to locate the actual HTML fragment within the full document.

The format name `"HTML Format"` must be registered at runtime using `RegisterClipboardFormat("HTML Format")`. This returns a unique format ID that is consistent across all Windows applications.

## Format Structure

```
Version:0.9                        ← Format version
StartHTML:0000000105               ← Byte offset to <html>
EndHTML:0000000
StartFragment:0000000141           ← Byte offset to the content fragment
EndFragment:0000000
<html>                             ← StartHTML points here
<body>
<!--StartFragment-->               ← StartFragment points here (after this marker)
<table>                            ← The actual clipboard content
  <tr><td>Cell(1,1)</td></tr>
</table>
<!--EndFragment-->                 ← EndFragment points here (before this marker)
</body>
</html>                            ← EndHTML points here (after this)
```

## Header Fields

| Field | Description |
|-------|-------------|
| `Version` | Format version. Always `0.9` (the only version in common use). |
| `StartHTML` | Byte offset from the start of the string to the `<html>` tag. |
| `EndHTML` | Byte offset from the start of the string to the end of `</html>`. |
| `StartFragment` | Byte offset to the start of the useful content (after `<!--StartFragment-->`). |
| `EndFragment` | Byte offset to the end of the useful content (before `<!--EndFragment-->`). |

**All offsets are:**
- Measured in **bytes** (not characters) using **UTF-8** encoding
- Zero-padded to **10 digits** (e.g., `0000000105`)
- Counted from the **very first byte** of the entire string (including the header itself)

## Offset Calculation Algorithm

The tricky part is that the header length depends on the offset values, but the offset values depend on the header length. The solution is to use fixed-width 10-digit placeholders:

```csharp
// Step 1: Build the header template with 10-character placeholders
string headerTemplate =
    "Version:0.9\r\n" +
    "StartHTML:XXXXXXXXXX\r\n" +       // 10 X's = 10 digits
    "EndHTML:XXXXXXXXXX\r\n" +
    "StartFragment:XXXXXXXXXX\r\n" +
    "EndFragment:XXXXXXXXXX\r\n";

// Step 2: Build HTML wrapper and content
string htmlPrefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
string fragment = "<table>...</table>";  // Your actual content
string htmlSuffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

// Step 3: Calculate byte offsets (all in UTF-8)
int headerBytes = Encoding.UTF8.GetByteCount(headerTemplate);
int startHtml = headerBytes;
int startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
int endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlSuffix);

// Step 4: Replace placeholders with zero-padded numbers
// D10 produces exactly 10 digits, matching the 10 X's
string header = headerTemplate
    .Replace("StartHTML:XXXXXXXXXX", $"StartHTML:{startHtml:D10}")
    .Replace("EndHTML:XXXXXXXXXX", $"EndHTML:{endHtml:D10}")
    .Replace("StartFragment:XXXXXXXXXX", $"StartFragment:{startFragment:D10}")
    .Replace("EndFragment:XXXXXXXXXX", $"EndFragment:{endFragment:D10}");

// Step 5: Assemble
string result = header + htmlPrefix + fragment + htmlSuffix;
```

**Why this works:** Each `XXXXXXXXXX` is exactly 10 characters. Replacing with a `D10`-formatted number (e.g., `0000000105`) also produces exactly 10 characters. So the header length doesn't change after replacement, and all pre-calculated offsets remain correct.

## Encoding

The HTML clipboard format uses **UTF-8** encoding (not UTF-16). This is different from `CF_UNICODETEXT` which uses UTF-16.

When placing HTML data on the clipboard:
```csharp
// UTF-8 encoding with null terminator
byte[] bytes = Encoding.UTF8.GetBytes(htmlClipboardData + '\0');
```

When placing plain text on the clipboard:
```csharp
// UTF-16 encoding with null terminator
byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
```

## Complete Example

For a 2-row, 2-column table, the complete clipboard string looks like:

```
Version:0.9
StartHTML:0000000105
EndHTML:0000000
StartFragment:0000000141
EndFragment:0000000
<html>
<body>
<!--StartFragment--><table>
  <tr><td>Cell(1,1)</td><td>Cell(1,2)</td></tr>
  <tr><td>Cell(2,1)</td><td>Cell(2,2)</td></tr>
</table><!--EndFragment-->
</body>
</html>
```

(Note: actual offset values depend on the exact content length.)

## Optional Header Fields

The format also supports optional fields not used in this project:

| Field | Description |
|-------|-------------|
| `SourceURL` | URL of the document the HTML was copied from |
| `StartSelection` | Byte offset to the start of the user's selection |
| `EndSelection` | Byte offset to the end of the user's selection |

Example with optional fields:
```
Version:0.9
StartHTML:0000000200
EndHTML:0000000
StartFragment:0000000236
EndFragment:0000000
SourceURL:https://example.com/page
StartSelection:0000000236
EndSelection:0000000
```

## Common Mistakes

1. **Using UTF-16 encoding** — HTML format requires UTF-8. Using UTF-16 will corrupt the byte offsets and produce garbled content in pasting applications.

2. **Calculating offsets in characters instead of bytes** — For ASCII-only content this works by coincidence, but fails for any non-ASCII characters. Always use `Encoding.UTF8.GetByteCount()`.

3. **Variable-width offset numbers** — If you use non-padded numbers (e.g., `105` instead of `0000000105`), the header length changes, which invalidates the offset calculations. Always use 10-digit zero-padded numbers.

4. **Missing null terminator** — The clipboard data must be null-terminated. Append `'\0'` before encoding to bytes.

5. **Missing `<!--StartFragment-->` / `<!--EndFragment-->` markers** — These HTML comments are required. Some pasting applications use them to identify the content boundary.

## References

- [Microsoft Docs: HTML Clipboard Format](https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format)
