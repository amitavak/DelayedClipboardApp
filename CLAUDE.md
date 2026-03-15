# CLAUDE.md — Delayed Clipboard App

## Project Overview

This is a Windows-only WinForms application (.NET 9) that demonstrates **delayed clipboard rendering** using Win32 P/Invoke. The app promises clipboard formats without providing data, then generates content on-demand when another application pastes.

## Architecture

- **NativeMethods.cs** — All Win32 P/Invoke declarations (user32.dll, kernel32.dll). Clipboard functions and global memory management.
- **ContentGenerator.cs** — Pure content generation. Produces plain text (tab-separated) and HTML (Windows HTML clipboard format with byte offset headers). No clipboard or UI dependencies.
- **MainForm.cs** — The main form. Builds UI programmatically (no designer files). Overrides `WndProc` to handle `WM_RENDERFORMAT`, `WM_RENDERALLFORMATS`, and `WM_DESTROYCLIPBOARD`.
- **Program.cs** — Entry point. `[STAThread]` is required for clipboard/COM operations.

## Key Technical Decisions

1. **WinForms over WPF**: WinForms provides direct `WndProc` override, which is essential for handling clipboard messages. WPF would require `HwndSource` hooks, adding complexity with no benefit.
2. **P/Invoke over .NET Clipboard**: `System.Windows.Forms.Clipboard` does not support delayed rendering. Win32 API is the only option.
3. **Programmatic UI**: No designer files — keeps the project to 4 source files and makes the code fully self-documenting.
4. **Thread.Sleep for delay**: `WM_RENDERFORMAT` is synchronous — the requesting app blocks until we provide data. Async is not applicable here.

## Clipboard Format Details

- **CF_UNICODETEXT (13)**: Built-in format. Data is UTF-16, null-terminated, in `GlobalAlloc(GMEM_MOVEABLE)` memory.
- **HTML Format**: Registered format (name: "HTML Format"). Data is UTF-8 with a specific header containing `StartHTML`, `EndHTML`, `StartFragment`, `EndFragment` byte offsets.

## Build & Run

```bash
dotnet build    # Compile
dotnet run      # Run
```

## Common Pitfalls

- Do NOT call `OpenClipboard`/`CloseClipboard` inside `WM_RENDERFORMAT` (clipboard is already open).
- DO call `OpenClipboard`/`CloseClipboard` inside `WM_RENDERALLFORMATS` (must open it yourself).
- After `SetClipboardData(fmt, hGlobal)`, the clipboard owns the memory — do not `GlobalFree` it.
- HTML clipboard format uses UTF-8 encoding, not UTF-16.

## Knowledge Base Maintenance

The `docs/` directory contains a knowledge base with technical documentation for this project. **Whenever you make changes to the application, you must also update the relevant knowledge base documents to keep them in sync.** This includes but is not limited to:

- **Adding/removing/modifying Win32 APIs** → Update `docs/win32-clipboard-apis.md`
- **Changing clipboard formats or encoding** → Update `docs/html-clipboard-format.md` and/or `docs/win32-clipboard-apis.md`
- **Changing the delayed rendering flow or message handling** → Update `docs/delayed-clipboard-rendering.md`
- **Adding new code patterns or discovering new pitfalls** → Update `docs/implementation-patterns.md`
- **Encountering and resolving new issues** → Update `docs/troubleshooting.md`
- **Adding new files or changing architecture** → Update `docs/README.md` (knowledge base index) and the Architecture section above

The knowledge base index is at `docs/README.md`. If you add a new document, add it to the index.
