# Knowledge Base — Delayed Clipboard Rendering

Technical reference documentation for Win32 delayed clipboard rendering, the APIs involved, and the implementation patterns used in this project.

## Contents

| Document | Description |
|----------|-------------|
| [Delayed Clipboard Rendering](delayed-clipboard-rendering.md) | Core concept: how delayed rendering works, when to use it, and the message flow |
| [Win32 Clipboard APIs](win32-clipboard-apis.md) | P/Invoke reference for all clipboard and memory functions used |
| [HTML Clipboard Format](html-clipboard-format.md) | Windows HTML clipboard format specification with header/offset details |
| [Web Custom Format (Map Indirection)](web-custom-format.md) | Chromium Web Custom Format Map convention for exposing MIME-like custom formats |
| [Implementation Patterns](implementation-patterns.md) | Reusable code patterns, memory management rules, and critical pitfalls |
| [Troubleshooting](troubleshooting.md) | Common issues, debugging tips, and diagnostic approaches |

## Quick Reference

```
Normal clipboard:  Copy → generate data → store in clipboard → done
Delayed rendering: Copy → promise formats (NULL data) → [wait] → paste request → generate data → provide to clipboard
```

### Key Win32 Messages

| Message | Code | Meaning |
|---------|------|---------|
| `WM_RENDERFORMAT` | `0x0305` | "App X wants format Y — generate it now" |
| `WM_RENDERALLFORMATS` | `0x0306` | "You're closing — generate everything before you go" |
| `WM_DESTROYCLIPBOARD` | `0x0307` | "Another app took clipboard ownership — you're released" |

### Key APIs

| Function | Role |
|----------|------|
| `SetClipboardData(fmt, NULL)` | Promise a format (delayed rendering) |
| `SetClipboardData(fmt, hGlobal)` | Provide actual data for a format |
| `EmptyClipboard()` | Take clipboard ownership |
| `RegisterClipboardFormat("HTML Format")` | Get the format ID for HTML |
| `RegisterClipboardFormat("Web Custom Format Map")` | Chromium metadata map — JSON mapping MIME keys to slot format names |
| `RegisterClipboardFormat("Web Custom FormatN")` | Chromium payload slot (N = 0..15) pointed to by the map |
