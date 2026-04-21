# Delayed Clipboard Rendering

## What Is It?

Delayed clipboard rendering is a Win32 feature where an application **promises** clipboard formats at copy time without providing actual data. The data is generated **on-demand** only when another application requests it by pasting.

This is in contrast to normal (immediate) clipboard rendering, where data is fully generated and stored in the clipboard the moment the user copies.

## When Is It Useful?

- **Expensive data generation**: If generating the data takes significant time or resources (database queries, network calls, complex computation), delayed rendering avoids wasting effort if the user never pastes.
- **Multiple formats**: When offering data in many formats (text, HTML, RTF, CSV, images), only the format actually requested needs to be generated.
- **Large data**: Avoids storing large data in the clipboard until it's actually needed.
- **Dynamic data**: The data can reflect the state at paste time rather than copy time (though this project snapshots values at copy time by design).

## How It Works

### Phase 1: Promise Formats (at Copy Time)

```
User clicks Copy
    │
    ├── OpenClipboard(hWnd)         ← Associate clipboard with our window
    ├── EmptyClipboard()            ← Take ownership (we are now the clipboard owner)
    ├── SetClipboardData(fmt, NULL) ← Promise format WITHOUT providing data
    ├── SetClipboardData(fmt, NULL) ← Promise another format
    └── CloseClipboard()            ← Done — clipboard knows we have data available
```

Passing `NULL` (IntPtr.Zero) as the data handle tells Windows: "I can provide this format, but I haven't generated the data yet."

### Phase 2: Render on Demand (at Paste Time)

```
User pastes in another app (e.g., Ctrl+V in Excel)
    │
    ├── Windows determines which format the pasting app wants
    ├── Windows sends WM_RENDERFORMAT to our window
    │   └── wParam = the format ID being requested
    │
    ├── Our WndProc receives the message
    ├── We generate the data for the requested format
    ├── GlobalAlloc + GlobalLock + Marshal.Copy + GlobalUnlock
    ├── SetClipboardData(fmt, hGlobal)  ← Provide actual data
    └── Pasting app receives the data
```

### Phase 3: Cleanup

Two cleanup scenarios exist:

**Scenario A: Another app takes clipboard** → We receive `WM_DESTROYCLIPBOARD`
- Another app called `EmptyClipboard()`, so our promises are void
- We just clear our tracking state

**Scenario B: Our window is closing** → We receive `WM_RENDERALLFORMATS`
- We must render ALL promised formats so the data survives our exit
- Unlike `WM_RENDERFORMAT`, we must call `OpenClipboard`/`CloseClipboard` ourselves

## Message Lifecycle Diagram

```
┌─────────────┐                    ┌─────────────┐
│  Our App     │                    │  Other App   │
│  (Owner)     │                    │  (Paster)    │
└──────┬───────┘                    └──────┬───────┘
       │                                   │
       │ SetClipboardData(fmt, NULL)       │
       │ ──── promise format ────────►     │
       │                                   │
       │        ... time passes ...        │
       │                                   │
       │    ◄──── WM_RENDERFORMAT ──────   │  (User pastes)
       │         (wParam = format)         │
       │                                   │
       │ [Generate data, 10s delay]        │
       │                                   │
       │ SetClipboardData(fmt, hGlobal)    │
       │ ──── provide actual data ────►    │
       │                                   │
       │                                   │  (Paste completes)
```

## Important Rules

1. **Do NOT call OpenClipboard/CloseClipboard inside WM_RENDERFORMAT** — the clipboard is already open on your behalf.
2. **DO call OpenClipboard/CloseClipboard inside WM_RENDERALLFORMATS** — you must open it yourself.
3. **After SetClipboardData(fmt, hGlobal) succeeds, the clipboard owns the memory** — do not free it.
4. **WM_RENDERFORMAT is synchronous** — the pasting app blocks until you provide data.
5. **EmptyClipboard() is required** before promising formats — it transfers clipboard ownership to your window.

## Multi-Format Paste: Map + Payload Round Trip

A single paste can trigger **multiple** `WM_RENDERFORMAT` messages when
a consumer needs more than one format. The Chromium Web Custom Format
Map convention used by this project is the clearest example:

```
Consumer pastes `web data/my-custom-format`
    │
    ├── Windows sends WM_RENDERFORMAT for "Web Custom Format Map"
    │      └── We generate the map JSON instantly (no delay)
    │          {"web data/my-custom-format":"Web Custom Format0"}
    │
    ├── Consumer parses the map, resolves MIME → "Web Custom Format0"
    │
    └── Windows sends WM_RENDERFORMAT for "Web Custom Format0"
           └── We run the cancellable delay loop, then generate payload
               [{"row":1,"col":1,"content":"Cell(1,1)"}, ...]
```

Design rule: render *payload* formats through the delay loop, render
*metadata* formats (like the map) instantly. This keeps paste latency
bounded by the single payload delay regardless of how many lookup
messages the consumer sends first. See
[web-custom-format.md](web-custom-format.md) for details.

## Comparison: Normal vs Delayed Rendering

| Aspect | Normal Rendering | Delayed Rendering |
|--------|-----------------|-------------------|
| Data generation | At copy time | At paste time |
| Memory usage | Immediate | On-demand |
| Multiple formats | All generated upfront | Only requested format generated |
| API call | `SetClipboardData(fmt, hGlobal)` | `SetClipboardData(fmt, NULL)` then later `SetClipboardData(fmt, hGlobal)` in WM_RENDERFORMAT |
| .NET support | `Clipboard.SetText()` works | Must use Win32 P/Invoke |
| Window requirement | Not required to stay open | Must stay open (or handle WM_RENDERALLFORMATS) |

## Why .NET's Clipboard Class Can't Do This

`System.Windows.Forms.Clipboard.SetText()` and `Clipboard.SetDataObject()` always write data immediately. There is no API in .NET to:
- Promise a format without data
- Register a callback for when data is requested
- Override `WndProc` through the Clipboard API

The only way to implement delayed rendering in .NET is to use Win32 P/Invoke directly and handle Windows messages in `WndProc`.

## References

- [Microsoft Docs: Delayed Rendering](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations#delayed-rendering)
- [Microsoft Docs: Using the Clipboard](https://learn.microsoft.com/en-us/windows/win32/dataxchg/using-the-clipboard)
