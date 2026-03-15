# Delayed Clipboard App

A Windows desktop application that demonstrates **delayed clipboard rendering** — a Win32 feature where clipboard data is not generated at copy time but instead produced on-demand when another application requests it via paste.

**[Download DelayedClipboardApp.exe](https://github.com/amitavak/DelayedClipboardApp/releases/latest/download/DelayedClipboardApp.exe)** — self-contained, no .NET runtime required.

## What is Delayed Clipboard Rendering?

Normally when you copy something, the data is immediately written to the clipboard. With delayed rendering:

1. The app **promises** clipboard formats (e.g., text/plain, text/html) without providing data
2. When another app **pastes**, Windows sends `WM_RENDERFORMAT` back to the owning app
3. The owning app **generates the data on-the-fly** and provides it at that moment

This is useful for expensive data generation, large datasets, or offering multiple formats where only one will typically be used.

## Features

- Configure table dimensions (rows and columns)
- Select clipboard formats: Plain Text (tab-separated) and/or HTML table
- Delayed rendering via Win32 API (`SetClipboardData` with NULL data handle)
- Configurable simulated delay during content generation to demonstrate deferred behavior
- Activity log showing the full lifecycle: promise → request → render

## Prerequisites

- **Windows 11** (Windows 10 should also work)
- **.NET 9 SDK** — [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)

Verify the SDK is installed:

```
dotnet --version
```

## Build and Run

```bash
# Clone or navigate to the project directory
cd DelayedClipboardApp

# Build
dotnet build

# Run
dotnet run
```

Or build a self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The output will be in `bin/Release/net9.0-windows/win-x64/publish/`.

## How to Use

1. Launch the application
2. Set the number of **Rows** and **Columns** for the table
3. Check **Include Plain Text** and/or **Include HTML** as desired
4. Click **Copy to Clipboard** — this promises the formats (no data generated yet)
5. Switch to another application (e.g., Excel, Word, Notepad) and press **Ctrl+V**
6. Watch the Activity Log — you'll see the 10-second delay as content is generated on-demand
7. The generated table data appears in the target application

## Project Structure

```
DelayedClipboardApp/
├── DelayedClipboardApp.csproj   # .NET 9 WinForms project file
├── Program.cs                   # Application entry point
├── MainForm.cs                  # Main window: UI + WndProc clipboard handling
├── NativeMethods.cs             # Win32 P/Invoke declarations for clipboard APIs
├── ContentGenerator.cs          # Table content generation (plain text + HTML)
└── README.md                    # This file
```

## Technical Details

### Win32 APIs Used

| API | Purpose |
|-----|---------|
| `OpenClipboard` | Opens the clipboard for modification |
| `EmptyClipboard` | Clears clipboard and takes ownership |
| `SetClipboardData(fmt, NULL)` | Promises a format (delayed rendering) |
| `SetClipboardData(fmt, hData)` | Provides actual data when requested |
| `CloseClipboard` | Releases the clipboard |
| `RegisterClipboardFormat` | Registers "HTML Format" |
| `GlobalAlloc/Lock/Unlock` | Allocates clipboard-compatible memory |

### Windows Messages Handled

| Message | When | Action |
|---------|------|--------|
| `WM_RENDERFORMAT` | Another app pastes our data | Generate content for the requested format |
| `WM_RENDERALLFORMATS` | Our window is closing | Generate all promised formats |
| `WM_DESTROYCLIPBOARD` | Another app takes clipboard | Clear our tracking state |

### HTML Clipboard Format

Windows uses a specific format for HTML clipboard data that includes a UTF-8 header with byte offsets:

```
Version:0.9
StartHTML:0000000105
EndHTML:0000000
StartFragment:0000000141
EndFragment:0000000
<html>
<body>
<!--StartFragment--><table>...</table><!--EndFragment-->
</body>
</html>
```

### Why Not Use .NET's Clipboard Class?

`System.Windows.Forms.Clipboard` does not support delayed rendering. It always writes data immediately. To implement deferred/lazy clipboard data, we must use the Win32 API directly via P/Invoke and handle `WM_RENDERFORMAT` in the window procedure.

## Knowledge Base

The [`docs/`](docs/) directory contains detailed technical documentation:

- [**Delayed Clipboard Rendering**](docs/delayed-clipboard-rendering.md) — Core concept, message flow diagrams, and comparison with normal rendering
- [**Win32 Clipboard APIs**](docs/win32-clipboard-apis.md) — P/Invoke reference for all clipboard and memory functions
- [**HTML Clipboard Format**](docs/html-clipboard-format.md) — Windows HTML clipboard format specification with offset calculation
- [**Implementation Patterns**](docs/implementation-patterns.md) — Reusable code patterns, memory management rules, and critical pitfalls
- [**Troubleshooting**](docs/troubleshooting.md) — Common issues, diagnostic approaches, and solutions
