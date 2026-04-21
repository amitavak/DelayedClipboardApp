# Win32 Clipboard APIs Reference

Complete P/Invoke reference for all Win32 functions used in this project.

## Clipboard Functions (user32.dll)

### OpenClipboard

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool OpenClipboard(IntPtr hWndNewOwner);
```

Opens the clipboard for examination and prevents other applications from modifying it.

| Parameter | Description |
|-----------|-------------|
| `hWndNewOwner` | Handle to the window to associate with the clipboard. Use `this.Handle` from a Form. |
| **Returns** | `true` if successful. Fails if another app has the clipboard open. |

**Usage rules:**
- Must be called before any clipboard modification
- Only one application can have the clipboard open at a time
- Always pair with `CloseClipboard()` in a try/finally block
- Do NOT call inside `WM_RENDERFORMAT` (clipboard is already open)
- DO call inside `WM_RENDERALLFORMATS` (must open explicitly)

---

### CloseClipboard

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool CloseClipboard();
```

Closes the clipboard after it was opened with `OpenClipboard`.

| **Returns** | `true` if successful. |

**Usage rules:**
- Must be called after every successful `OpenClipboard`
- Always place in a `finally` block to ensure cleanup on errors
- No parameters needed — closes whatever clipboard is currently open

---

### EmptyClipboard

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool EmptyClipboard();
```

Empties the clipboard and transfers **ownership** to the window that opened it.

| **Returns** | `true` if successful. |

**Why this matters for delayed rendering:**
- After `EmptyClipboard()`, our window becomes the clipboard owner
- As the owner, we receive `WM_RENDERFORMAT`, `WM_RENDERALLFORMATS`, and `WM_DESTROYCLIPBOARD`
- Without calling this, `SetClipboardData` with NULL will not work correctly

---

### SetClipboardData

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hData);
```

The core function. Has two modes of operation:

**Mode 1 — Delayed Rendering (promise):**
```csharp
// hData = IntPtr.Zero means "I'll provide this later"
SetClipboardData(CF_UNICODETEXT, IntPtr.Zero);
```

**Mode 2 — Immediate Rendering (provide):**
```csharp
// hData = valid HGLOBAL from GlobalAlloc
SetClipboardData(CF_UNICODETEXT, hGlobal);
```

| Parameter | Description |
|-----------|-------------|
| `uFormat` | Clipboard format ID (e.g., `CF_UNICODETEXT = 13` or a registered format ID) |
| `hData` | `IntPtr.Zero` for delayed rendering, or a valid HGLOBAL handle for immediate rendering |
| **Returns** | Handle to the data if successful, `IntPtr.Zero` on failure |

**Critical memory ownership rule:**
After a successful call with a valid HGLOBAL, **the clipboard owns the memory**. Do NOT call `GlobalFree()` on it. The clipboard will free it when it's no longer needed.

---

### RegisterClipboardFormat

```csharp
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern uint RegisterClipboardFormat(string lpszFormat);
```

Registers a named clipboard format and returns its unique ID.

| Parameter | Description |
|-----------|-------------|
| `lpszFormat` | Format name string (e.g., `"HTML Format"`) |
| **Returns** | Unique format ID (uint), or 0 on failure |

**Key behavior:**
- If the format is already registered by another app, returns the same ID
- Format IDs are in the range `0xC000` through `0xFFFF`
- The name `"HTML Format"` is the standard Windows convention for HTML clipboard data
- Built-in formats (like `CF_UNICODETEXT = 13`) do not need registration

---

## Global Memory Functions (kernel32.dll)

These functions manage the memory blocks that clipboard data lives in. The clipboard requires `GMEM_MOVEABLE` memory because Windows needs to be able to relocate the memory block.

### GlobalAlloc

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
```

Allocates a block of memory from the global heap.

| Parameter | Description |
|-----------|-------------|
| `uFlags` | Allocation flags. Use `GMEM_MOVEABLE = 0x0002` for clipboard data. |
| `dwBytes` | Number of bytes to allocate. |
| **Returns** | Handle to the allocated memory, or `IntPtr.Zero` on failure. |

**Why GMEM_MOVEABLE?**
The clipboard subsystem needs to move memory blocks between processes. Fixed memory (`GMEM_FIXED`) cannot be moved, so it doesn't work with the clipboard.

---

### GlobalLock

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalLock(IntPtr hMem);
```

Locks a moveable memory block and returns a direct pointer for reading/writing.

| Parameter | Description |
|-----------|-------------|
| `hMem` | Handle from `GlobalAlloc` |
| **Returns** | Pointer to the first byte of the memory block, or `IntPtr.Zero` on failure |

**Why locking is needed:**
Since the memory is moveable, you can't use the handle directly as a pointer. `GlobalLock` pins the memory in place temporarily and gives you a usable pointer.

---

### GlobalUnlock

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GlobalUnlock(IntPtr hMem);
```

Decrements the lock count. When the count reaches zero, the memory can be moved again.

| Parameter | Description |
|-----------|-------------|
| `hMem` | Handle from `GlobalAlloc` |
| **Returns** | `true` if still locked (lock count > 0), `false` if fully unlocked |

---

### GlobalFree

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalFree(IntPtr hMem);
```

Frees a global memory block.

| Parameter | Description |
|-----------|-------------|
| `hMem` | Handle from `GlobalAlloc` |
| **Returns** | `IntPtr.Zero` if successful, `hMem` on failure |

**When to call:**
- Only for memory you allocated but did NOT pass to `SetClipboardData`
- Typically in error-recovery paths (e.g., `GlobalLock` failed after `GlobalAlloc`)
- **NEVER** call on memory after a successful `SetClipboardData` — the clipboard owns it

---

## Complete Memory Lifecycle

```
GlobalAlloc(GMEM_MOVEABLE, size)  → hGlobal (handle)
    │
GlobalLock(hGlobal)               → ptr (direct pointer for writing)
    │
Marshal.Copy(bytes, 0, ptr, len)  → data copied into memory block
    │
GlobalUnlock(hGlobal)             → memory is moveable again
    │
SetClipboardData(fmt, hGlobal)    → clipboard takes ownership
    │
    ╳  DO NOT call GlobalFree     → clipboard will free when done
```

**Error path (if GlobalLock fails):**
```
GlobalAlloc(GMEM_MOVEABLE, size)  → hGlobal
    │
GlobalLock(hGlobal)               → IntPtr.Zero (FAILED)
    │
GlobalFree(hGlobal)               → clean up since clipboard never got it
```

## Clipboard Format Constants

| Constant | Value | Encoding | Description |
|----------|-------|----------|-------------|
| `CF_TEXT` | 1 | ANSI (8-bit) | Legacy text format |
| `CF_UNICODETEXT` | 13 | UTF-16 | Modern text format (used in this project) |
| `CF_BITMAP` | 2 | — | Bitmap image |
| `CF_DIB` | 8 | — | Device-independent bitmap |
| `CF_HDROP` | 15 | — | File list (drag-and-drop) |
| `"HTML Format"` | Registered | UTF-8 | HTML content (registered via `RegisterClipboardFormat`) |
| `"Web Custom Format Map"` | Registered | UTF-8 | Chromium-reserved JSON object mapping MIME keys (e.g., `web data/my-custom-format`) to payload slot names |
| `"Web Custom Format0"` .. `"Web Custom Format15"` | Registered | UTF-8 | Chromium-reserved payload slots referenced by the map — one MIME type per slot |

See [web-custom-format.md](web-custom-format.md) for the Chromium Web Custom Format Map conventions.

## References

- [Microsoft Docs: Clipboard Functions](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-functions)
- [Microsoft Docs: GlobalAlloc](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globalalloc)
