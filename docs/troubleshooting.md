# Troubleshooting Guide

Common issues when working with Win32 delayed clipboard rendering and their solutions.

## "Failed to open clipboard"

**Symptom:** `OpenClipboard` returns `false`.

**Causes:**
1. **Another application has the clipboard open.** Clipboard viewers, password managers, and other tools may briefly hold the clipboard. Retry after a short delay, or inform the user.
2. **Your own code didn't call `CloseClipboard`.** Double-check that every `OpenClipboard` path has a matching `CloseClipboard` in a `finally` block.
3. **Calling `OpenClipboard` inside `WM_RENDERFORMAT`.** The clipboard is already open in this handler — do not open it again.

**Diagnosis:**
```csharp
if (!NativeMethods.OpenClipboard(Handle))
{
    int error = Marshal.GetLastWin32Error();
    Log($"OpenClipboard failed, Win32 error: {error}");
}
```

---

## WM_RENDERFORMAT Never Fires

**Symptom:** You click Copy, but pasting in another app doesn't trigger content generation.

**Causes:**
1. **Forgot to call `EmptyClipboard`.** Without this call, your window doesn't become the clipboard owner and won't receive render messages.
2. **Wrong window handle.** Ensure you pass `this.Handle` (the form's handle) to `OpenClipboard`.
3. **Pasting app doesn't request your format.** For example, Notepad requests `CF_UNICODETEXT` but not HTML. Check which format the target app expects.
4. **`SetClipboardData` with NULL failed silently.** Always check the return value.

**Diagnosis:**
Add logging to confirm each step:
```csharp
Log($"OpenClipboard: {NativeMethods.OpenClipboard(Handle)}");
Log($"EmptyClipboard: {NativeMethods.EmptyClipboard()}");
Log($"SetClipboardData: {NativeMethods.SetClipboardData(fmt, IntPtr.Zero)}");
```

---

## Garbled or Empty Content After Paste

**Symptom:** Pasting produces garbled text, empty content, or only partial data.

**Causes:**
1. **Wrong encoding.** `CF_UNICODETEXT` requires UTF-16 (`Encoding.Unicode`). HTML Format requires UTF-8 (`Encoding.UTF8`). Mixing them up produces garbled output.
2. **Missing null terminator.** Both formats require a null character at the end. Append `'\0'` before encoding.
3. **Incorrect HTML offset calculation.** If the byte offsets in the HTML header are wrong, the pasting app reads the wrong portion of the data.
4. **Memory too small.** Ensure `GlobalAlloc` allocates enough bytes for the full encoded content including the null terminator.

**Diagnosis for HTML offsets:**
```csharp
string htmlData = ContentGenerator.GenerateHtmlClipboardData(rows, cols);
Log(htmlData);  // Print the full string and manually verify offsets
```

---

## Application Crashes on Paste

**Symptom:** Access violation or unhandled exception when another app pastes.

**Causes:**
1. **Calling `GlobalFree` on clipboard-owned memory.** After `SetClipboardData(fmt, hGlobal)` succeeds, do NOT free `hGlobal`.
2. **Using `GMEM_FIXED` instead of `GMEM_MOVEABLE`.** The clipboard requires moveable memory.
3. **Buffer overrun.** The allocated memory is smaller than the data being copied. Ensure `GlobalAlloc` size matches `bytes.Length`.

---

## Clipboard Data Doesn't Persist After App Closes

**Symptom:** After closing the app, pasting produces nothing or an error.

**Cause:** `WM_RENDERALLFORMATS` is not handled correctly.

**Checklist:**
1. Is the `WM_RENDERALLFORMATS` case in `WndProc`?
2. Does the handler call `OpenClipboard`/`CloseClipboard` (required, unlike `WM_RENDERFORMAT`)?
3. Does it render ALL promised formats, not just one?
4. Is there any exception being swallowed?

---

## "Promise" Works but Second Copy Doesn't

**Symptom:** First Copy+Paste works fine. Clicking Copy again and pasting fails or produces stale data — no formats are promised on the second click.

**Root cause:** `EmptyClipboard()` sends `WM_DESTROYCLIPBOARD` **synchronously** to the current clipboard owner. If your app is the current owner (because of the first Copy), and you set `_promisedPlainText`/`_promisedHtml` **before** calling `EmptyClipboard()`, the `WM_DESTROYCLIPBOARD` handler wipes those flags before they're read.

**Fix:** Always snapshot promised state (rows, columns, format flags) **after** `EmptyClipboard()`, not before. See [Pattern 5 in Implementation Patterns](implementation-patterns.md#pattern-5-snapshotting-ui-state--after-emptyclipboard).

**Checklist:**
1. Are you setting promised-state variables AFTER `EmptyClipboard()`?
2. Does your `WM_DESTROYCLIPBOARD` handler clear promised-state variables? (It should — but the order matters.)
3. Ensure `_promisedRows` and `_promisedColumns` are also set after `EmptyClipboard()`.

---

## Web Custom Format Invisible to Chrome / Edge (but Visible in Native Clipboard Viewers)

**Symptom:** A native clipboard viewer (e.g., [Free Clipboard Viewer](https://freeclipboardviewer.com/))
shows all formats correctly — plain text, HTML, `Web Custom Format Map`,
and `Web Custom Format0` with the JSON payload. But when a web page in
Chrome or Edge calls `navigator.clipboard.read()`, the custom format
never appears in `item.types`. Plain text and HTML come through fine.

**Root cause:** The JSON key in the `Web Custom Format Map` includes the
`"web "` prefix (e.g., `{"web data/my-custom-format": "Web Custom Format0"}`).
Chromium's reader
(`ui/base/clipboard/clipboard.cc::OnCustomFormatDataRead`) validates
each key via `net::ParseMimeTypeWithoutParameter`. That function splits
the key on `/` and requires both parts to be valid HTTP tokens — and
HTTP tokens cannot contain spaces. So `"web data"` (the part before `/`)
fails, the entry is skipped, and the custom format disappears from the
consumer's view.

**Fix:** Use the **bare MIME type** as the key, not the `"web "`-prefixed
form. Chromium prepends `"web "` itself when surfacing the type to JS.

```json
// ❌ WRONG — Chromium silently drops this entry
{"web data/my-custom-format":"Web Custom Format0"}

// ✅ CORRECT — key is a valid MIME, Chromium adds "web " for JS
{"data/my-custom-format":"Web Custom Format0"}
```

**Diagnosis:** In the web page, log `item.types` immediately after
`navigator.clipboard.read()`:
- If the custom type is missing from `types`, the map key is malformed
  (or the top/subtype parts aren't valid HTTP tokens).
- If the custom type appears in `types` but `item.getType(...)` rejects
  or times out, the map is valid but the payload slot isn't being
  rendered in time — see the delay-timeout section below.

See [web-custom-format.md](web-custom-format.md) for the full map
convention.

---

## Web Custom Format Times Out in Chrome When Delay is High

**Symptom:** Chrome returns an empty array from `navigator.clipboard.read()`
on the first click; on a second click it returns plain text and HTML
but not the web custom format payload, even though the map itself
parses correctly.

**Root cause:** Chromium imposes an internal timeout on synchronous
`GetClipboardData` calls. If `WM_RENDERFORMAT` takes longer than
Chromium's budget (empirically a few seconds), Chromium abandons the
read for that format. On a follow-up click, any format whose
`WM_RENDERFORMAT` already completed on our side is now real (not
promised) and returns instantly — so plain text and HTML appear, but
the still-promised web custom payload hits the timeout again.

**Fix:** Lower the simulated delay for testing with Chrome/Edge.
Native apps like Free Clipboard Viewer block indefinitely on
`GetClipboardData` and are not subject to this timeout — they see all
formats regardless of delay.

---

## HTML Renders as Plain Text in Target App

**Symptom:** Pasting into Word or a rich-text editor shows the raw HTML tags instead of a formatted table.

**Causes:**
1. **Wrong clipboard format.** You may be placing HTML data as `CF_UNICODETEXT` instead of using the registered "HTML Format" ID.
2. **Missing HTML clipboard header.** The HTML data must include the `Version:0.9` / `StartHTML` / `EndHTML` / `StartFragment` / `EndFragment` header. Raw HTML without this header won't be recognized.
3. **Format registration failed.** Check that `RegisterClipboardFormat("HTML Format")` returned a non-zero value.

---

## Build Issues

### "The type or namespace 'DllImport' could not be found"

Add `using System.Runtime.InteropServices;` to the file.

### "The name 'Handle' does not exist in the current context"

Ensure the class inherits from `Form`. `Handle` is a property of `Control`/`Form` in WinForms.

### "CS8600: Converting null literal or possible null value to non-nullable type"

Use the null-forgiving operator (`null!`) for controls initialized in a separate method:
```csharp
private TextBox _logTextBox = null!;  // Initialized in InitializeComponents()
```

---

## Diagnostic Approach

When something isn't working:

1. **Check the Activity Log** — The app logs every step with timestamps. Look for ERROR messages.
2. **Verify the message flow** — You should see:
   - "Promised CF_UNICODETEXT" (or HTML) on Copy
   - "WM_RENDERFORMAT received" on Paste
   - "rendered and placed on clipboard" after generation
3. **Try a different target app** — Some apps only request specific formats. Try Notepad (text), Excel (text + HTML), and Word (HTML).
4. **Check clipboard contents with a viewer** — Use a clipboard viewer tool to see what formats are available on the clipboard.
5. **Add Win32 error logging** — Call `Marshal.GetLastWin32Error()` after any failed API call to get specific error codes.
