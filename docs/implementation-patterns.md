# Implementation Patterns & Pitfalls

Reusable code patterns and critical rules for working with Win32 delayed clipboard rendering in C# / WinForms.

## Pattern 1: Safe Clipboard Access (Try/Finally)

Always wrap clipboard operations in a try/finally block to ensure `CloseClipboard` is called even on errors:

```csharp
if (!NativeMethods.OpenClipboard(Handle))
{
    // Handle error: another app may have the clipboard locked
    return;
}

try
{
    NativeMethods.EmptyClipboard();
    // ... clipboard operations ...
}
finally
{
    NativeMethods.CloseClipboard();  // Always called, even on exceptions
}
```

**Why:** If `CloseClipboard` is not called, the clipboard remains locked and no other application can use it until the owning process exits.

---

## Pattern 2: Delayed Rendering Promise

Promise a format without providing data:

```csharp
// IntPtr.Zero = "I'll provide this data later"
IntPtr result = NativeMethods.SetClipboardData(CF_UNICODETEXT, IntPtr.Zero);
if (result == IntPtr.Zero)
{
    // Promise failed — check GetLastError
}
```

**Prerequisites:**
- Must have called `OpenClipboard(Handle)` first
- Must have called `EmptyClipboard()` to take ownership
- The window (Handle) must stay alive to receive `WM_RENDERFORMAT`

---

## Pattern 3: Memory Allocation for Clipboard Data

The full pattern for providing clipboard data:

```csharp
byte[] bytes = /* encoded data */;

// Step 1: Allocate moveable memory
IntPtr hGlobal = NativeMethods.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
if (hGlobal == IntPtr.Zero) return;  // Allocation failed

// Step 2: Lock to get a writable pointer
IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
if (ptr == IntPtr.Zero)
{
    NativeMethods.GlobalFree(hGlobal);  // Clean up on lock failure
    return;
}

// Step 3: Copy data into the memory block
Marshal.Copy(bytes, 0, ptr, bytes.Length);

// Step 4: Unlock (memory becomes moveable again)
NativeMethods.GlobalUnlock(hGlobal);

// Step 5: Hand to clipboard — clipboard now OWNS this memory
NativeMethods.SetClipboardData(format, hGlobal);
// DO NOT call GlobalFree(hGlobal) after this point
```

---

## Pattern 4: WndProc Override for Clipboard Messages

```csharp
protected override void WndProc(ref Message m)
{
    switch (m.Msg)
    {
        case NativeMethods.WM_RENDERFORMAT:
            // Cast wParam to get the format ID
            uint format = (uint)(nint)m.WParam;
            HandleRenderFormat(format);
            return;  // Do NOT pass to base — we handled it

        case NativeMethods.WM_RENDERALLFORMATS:
            HandleRenderAllFormats();
            return;

        case NativeMethods.WM_DESTROYCLIPBOARD:
            // Another app took ownership — clean up state
            return;
    }

    base.WndProc(ref m);  // Pass unhandled messages to base
}
```

**Key:** Use `return` (not `break`) after handling a message to prevent it from reaching the base `WndProc`.

---

## Pattern 5: Snapshotting UI State — AFTER EmptyClipboard

Save configuration values when the user clicks Copy, but **after** calling `EmptyClipboard()`, not before. This is critical because `EmptyClipboard()` sends `WM_DESTROYCLIPBOARD` synchronously to the current clipboard owner — which may be us if the user clicks Copy a second time. If you snapshot state before `EmptyClipboard`, the `WM_DESTROYCLIPBOARD` handler will wipe the values you just set.

```csharp
// ❌ WRONG — WM_DESTROYCLIPBOARD (from EmptyClipboard) clears these before they're used
_promisedPlainText = _plainTextCheckbox.Checked;  // Set to true
OpenClipboard(Handle);
EmptyClipboard();  // Sends WM_DESTROYCLIPBOARD → handler sets _promisedPlainText = false
if (_promisedPlainText) { /* never reached on second click! */ }

// ✅ CORRECT — snapshot AFTER EmptyClipboard
OpenClipboard(Handle);
EmptyClipboard();  // WM_DESTROYCLIPBOARD clears OLD state — that's fine
_promisedPlainText = _plainTextCheckbox.Checked;  // Set NEW state
if (_promisedPlainText) { /* works every time */ }
```

---

## Pattern 6: Cancellable Delay Loop with Application.DoEvents

When simulating a delay inside `WM_RENDERFORMAT`, avoid `Thread.Sleep(N)` which blocks the UI entirely. Instead, use a loop with `Application.DoEvents()` and small sleeps so the user can cancel the operation:

```csharp
private bool _isRendering;
private bool _cancelRendering;

private void HandleRenderFormat(uint format)
{
    _isRendering = true;
    _cancelRendering = false;

    // Cancellable delay loop — UI stays responsive
    const int sleepIntervalMs = 50;
    int elapsed = 0;
    while (elapsed < RenderDelaySeconds * 1000)
    {
        Application.DoEvents();   // Process button clicks, repaints, etc.
        if (_cancelRendering)
            break;
        Thread.Sleep(sleepIntervalMs);
        elapsed += sleepIntervalMs;
    }

    if (_cancelRendering)
    {
        _isRendering = false;
        // Re-trigger copy AFTER WM_RENDERFORMAT returns (clipboard is still open)
        BeginInvoke(PerformCopy);
        return;
    }

    // Normal rendering...
    _isRendering = false;
}

private void OnCopyButtonClick(object? sender, EventArgs e)
{
    if (_isRendering)
    {
        _cancelRendering = true;  // Signal the delay loop to abort
        return;                   // PerformCopy will be called via BeginInvoke
    }
    PerformCopy();
}
```

**Key points:**
- `Application.DoEvents()` pumps the message queue, allowing button clicks to fire during the delay
- `BeginInvoke(PerformCopy)` defers the new copy until after `WM_RENDERFORMAT` fully returns — we can't open the clipboard while it's already open
- Not calling `SetClipboardData` on cancellation is safe — Windows treats the format as empty

---

## Pattern 7: Force UI Update Before Blocking

When you need to update a UI element (e.g., a log) before a blocking operation:

```csharp
_logTextBox.AppendText("Generating content, please wait...\r\n");
_logTextBox.Update();  // Force immediate repaint
```

Without `.Update()`, UI changes wouldn't appear until the message loop is free, which may be after a long operation completes.

---

## Critical Pitfalls

### 1. Opening Clipboard in WM_RENDERFORMAT

```csharp
// ❌ WRONG — clipboard is already open in WM_RENDERFORMAT
private void HandleRenderFormat(uint format)
{
    NativeMethods.OpenClipboard(Handle);  // WILL FAIL
    // ...
    NativeMethods.CloseClipboard();
}

// ✅ CORRECT — just provide the data directly
private void HandleRenderFormat(uint format)
{
    // Generate data and call SetClipboardData — clipboard is already open
    NativeMethods.SetClipboardData(format, hGlobal);
}
```

### 2. NOT Opening Clipboard in WM_RENDERALLFORMATS

```csharp
// ❌ WRONG — clipboard is NOT open in WM_RENDERALLFORMATS
private void HandleRenderAllFormats()
{
    NativeMethods.SetClipboardData(format, hGlobal);  // WILL FAIL
}

// ✅ CORRECT — must open clipboard yourself
private void HandleRenderAllFormats()
{
    NativeMethods.OpenClipboard(Handle);
    try
    {
        NativeMethods.SetClipboardData(format, hGlobal);
    }
    finally
    {
        NativeMethods.CloseClipboard();
    }
}
```

### 3. Freeing Clipboard-Owned Memory

```csharp
// ❌ WRONG — clipboard owns this memory after SetClipboardData
NativeMethods.SetClipboardData(format, hGlobal);
NativeMethods.GlobalFree(hGlobal);  // DOUBLE FREE → CRASH

// ✅ CORRECT — only free on error paths BEFORE SetClipboardData
IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
if (ptr == IntPtr.Zero)
{
    NativeMethods.GlobalFree(hGlobal);  // OK — clipboard never got it
    return;
}
```

### 4. Using Wrong Encoding

```csharp
// ❌ WRONG — HTML format uses UTF-8, not UTF-16
byte[] htmlBytes = Encoding.Unicode.GetBytes(htmlData);

// ✅ CORRECT
byte[] htmlBytes = Encoding.UTF8.GetBytes(htmlData + '\0');

// ❌ WRONG — CF_UNICODETEXT uses UTF-16, not UTF-8
byte[] textBytes = Encoding.UTF8.GetBytes(text);

// ✅ CORRECT
byte[] textBytes = Encoding.Unicode.GetBytes(text + '\0');
```

### 5. Forgetting the Null Terminator (Text Formats)

```csharp
// ❌ WRONG — missing null terminator
byte[] bytes = Encoding.Unicode.GetBytes(text);

// ✅ CORRECT — append null character before encoding
byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
```

**Exception — binary / structured custom formats:** Do *not* append a
null terminator for formats like the Chromium Web Custom Format Map or
Web Custom Format payload slots. Their size is conveyed by the
clipboard's own byte count, and a trailing NUL would corrupt strict
readers (e.g., JSON parsers reading the full buffer by size):

```csharp
// ✅ CORRECT — JSON custom format, no null terminator
byte[] bytes = Encoding.UTF8.GetBytes(json);
```

Rule of thumb: legacy text formats (CF_UNICODETEXT, HTML Format)
historically carry a null terminator for C-string compatibility; modern
binary/structured formats use size-authoritative framing and should not.

### 6. Missing [STAThread] Attribute

```csharp
// ❌ WRONG — clipboard operations require STA thread
static void Main()
{
    Application.Run(new MainForm());
}

// ✅ CORRECT
[STAThread]
static void Main()
{
    Application.Run(new MainForm());
}
```

The `[STAThread]` attribute is required for clipboard and COM operations. Without it, clipboard calls will fail silently or throw exceptions.

### 7. Using .NET Clipboard API for Delayed Rendering

```csharp
// ❌ IMPOSSIBLE — .NET Clipboard class doesn't support delayed rendering
Clipboard.SetText(null);  // This doesn't promise a format

// ✅ CORRECT — use Win32 P/Invoke
NativeMethods.SetClipboardData(CF_UNICODETEXT, IntPtr.Zero);
```

---

## Pattern 8: Metadata Formats Render Instantly (Skip the Delay)

When a single paste can trigger multiple `WM_RENDERFORMAT` messages —
for example, a Chromium consumer fetching both `Web Custom Format Map`
and `Web Custom Format0` — apply the simulated delay *only* to payload
formats. Metadata formats (maps, descriptors, fixed headers) should
render immediately.

```csharp
private void HandleRenderFormat(uint format)
{
    if (format == _cfWebCustomMap && _promisedCustom)
    {
        // Metadata — render instantly so it doesn't double the paste latency
        RenderWebCustomMap();
    }
    else if (format == _cfWebCustomFormat0 && _promisedCustom)
    {
        // Payload — apply the cancellable delay loop
        if (RunCancellableDelay()) return;
        RenderWebCustomPayload();
    }
    // ...
}
```

Without this split, a consumer that fetches metadata-then-payload would
experience `2 × delay` seconds per paste instead of `1 × delay`.

---

## Thread Safety Notes

- **WM_RENDERFORMAT is always delivered on the UI thread** (the thread that created the window), so no cross-thread concerns within the handler.
- **Thread.Sleep in WM_RENDERFORMAT blocks the UI thread**, but this is expected — the requesting app is also blocking, waiting for the data.
- **Do not use async/await in WndProc** — the message handler must be synchronous. The requesting app needs data immediately.
