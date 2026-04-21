using System.Runtime.InteropServices;

namespace DelayedClipboardApp;

/// <summary>
/// Main application form that demonstrates Windows delayed clipboard rendering.
///
/// === How Delayed Clipboard Rendering Works ===
///
/// Normal clipboard flow:
///   App copies → data is generated immediately → data stored in clipboard
///
/// Delayed rendering flow:
///   1. User clicks "Copy" → we call SetClipboardData(format, NULL) for each format
///      This "promises" the format to the clipboard without providing data.
///   2. Another app tries to paste → Windows sends WM_RENDERFORMAT to us
///      with the specific format ID the pasting app needs.
///   3. We generate the data on-the-fly and call SetClipboardData(format, data)
///      inside the WM_RENDERFORMAT handler to fulfill the promise.
///
/// This is useful when:
/// - Data generation is expensive and may never be needed
/// - You want to offer multiple formats but only generate what's actually requested
/// - The data might change between copy and paste
///
/// Special cases:
/// - WM_RENDERALLFORMATS: Sent when our window is being destroyed while we
///   still own the clipboard. We must render everything before we're gone.
/// - WM_DESTROYCLIPBOARD: Sent when another app takes clipboard ownership.
///   We no longer need to keep our promised data ready.
///
/// See: https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations
/// </summary>
public class MainForm : Form
{
    // ========================================================================
    // UI Controls
    // ========================================================================

    private NumericUpDown _rowsInput = null!;
    private NumericUpDown _columnsInput = null!;
    private NumericUpDown _delayInput = null!;
    private CheckBox _plainTextCheckbox = null!;
    private CheckBox _htmlCheckbox = null!;
    private CheckBox _customFormatCheckbox = null!;
    private Button _copyButton = null!;
    private Button _clearLogButton = null!;
    private TextBox _logTextBox = null!;

    // ========================================================================
    // Clipboard State
    // ========================================================================

    /// <summary>
    /// The registered clipboard format ID for "HTML Format".
    /// Unlike CF_UNICODETEXT which is a built-in constant (13), the HTML format
    /// must be registered at runtime using RegisterClipboardFormat.
    /// All Windows applications use the same registered name "HTML Format".
    /// </summary>
    private uint _cfHtml;

    /// <summary>
    /// The registered clipboard format ID for the Chromium Web Custom Format
    /// Map ("Web Custom Format Map"). This format holds a small JSON object
    /// that maps MIME-like identifiers (e.g., "web data/my-custom-format")
    /// to the registered format name that carries the actual payload bytes.
    /// Chromium-based browsers read this map first when resolving a custom
    /// format via the Async Clipboard API.
    /// </summary>
    private uint _cfWebCustomMap;

    /// <summary>
    /// The registered clipboard format ID for "Web Custom Format0" — the
    /// Chromium-reserved payload slot our JSON actually lives in. The map
    /// format above points "web data/my-custom-format" at this slot.
    /// </summary>
    private uint _cfWebCustomFormat0;

    /// <summary>
    /// The number of rows the user specified when they clicked "Copy".
    /// We save this at copy time because the user might change the UI values
    /// between clicking Copy and the actual paste request arriving.
    /// </summary>
    private int _promisedRows;

    /// <summary>
    /// The number of columns the user specified when they clicked "Copy".
    /// Saved at copy time for the same reason as _promisedRows.
    /// </summary>
    private int _promisedColumns;

    /// <summary>Whether we promised CF_UNICODETEXT (plain text) to the clipboard.</summary>
    private bool _promisedPlainText;

    /// <summary>Whether we promised "HTML Format" to the clipboard.</summary>
    private bool _promisedHtml;

    /// <summary>
    /// Whether we promised the Chromium web custom format to the clipboard.
    /// A single flag covers both the "Web Custom Format Map" and the
    /// "Web Custom Format0" payload slot — they always travel together.
    /// </summary>
    private bool _promisedCustom;

    /// <summary>
    /// The delay in seconds snapshotted at copy time.
    /// Saved alongside rows/columns so the delay used during rendering matches
    /// what the user configured when they clicked Copy.
    /// </summary>
    private int _promisedDelaySeconds;

    /// <summary>
    /// True while we are inside HandleRenderFormat (i.e., in the delay loop
    /// or generating content). Used by OnCopyButtonClick to detect that a
    /// rendering operation is in progress and must be cancelled.
    /// </summary>
    private bool _isRendering;

    /// <summary>
    /// Set to true by OnCopyButtonClick when the user clicks "Copy to Clipboard"
    /// while a rendering operation is in progress. The delay loop in
    /// HandleRenderFormat checks this flag and aborts early if set.
    /// After cancellation, HandleRenderFormat uses BeginInvoke to re-trigger
    /// PerformCopy once WM_RENDERFORMAT has fully returned.
    /// </summary>
    private bool _cancelRendering;

    // ========================================================================
    // Constructor
    // ========================================================================

    public MainForm()
    {
        InitializeComponents();

        // Register the Windows HTML clipboard format.
        // This returns a unique format ID that is consistent across all apps.
        // If another app has already registered "HTML Format", we get the same ID.
        _cfHtml = NativeMethods.RegisterClipboardFormat("HTML Format");

        // Register the two Chromium web-custom-format clipboard formats.
        // The map is metadata; the slot holds our JSON payload bytes. Both
        // IDs stay stable across apps because RegisterClipboardFormat hashes
        // the exact string to a canonical ID.
        _cfWebCustomMap = NativeMethods.RegisterClipboardFormat("Web Custom Format Map");
        _cfWebCustomFormat0 = NativeMethods.RegisterClipboardFormat(ContentGenerator.WebCustomFormatSlotName);

        Log($"Application started. Registered 'HTML Format' clipboard format (ID: {_cfHtml}).");
        Log($"Registered 'Web Custom Format Map' (ID: {_cfWebCustomMap}) and '{ContentGenerator.WebCustomFormatSlotName}' (ID: {_cfWebCustomFormat0}).");
        Log("Configure rows, columns, and formats, then click 'Copy to Clipboard'.");
    }

    // ========================================================================
    // UI Initialization
    // ========================================================================

    /// <summary>
    /// Creates and arranges all UI controls programmatically.
    /// We avoid using the WinForms designer to keep the project simple
    /// and the code self-contained in a single file.
    /// </summary>
    private void InitializeComponents()
    {
        // Form properties
        Text = "Delayed Clipboard App";
        Size = new Size(520, 580);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(420, 490);

        // Use a TableLayoutPanel for responsive layout that handles resizing
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15),
            ColumnCount = 2,
            RowCount = 9,
        };

        // Column 0: labels (auto-size to content)
        // Column 1: inputs (fill remaining space)
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- Row 0: Rows input ---
        var rowsLabel = new Label
        {
            Text = "Rows:",
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0)  // Vertical alignment with input
        };
        layout.Controls.Add(rowsLabel, 0, 0);

        _rowsInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 10000,
            Value = 3,           // Default: 3 rows (as in the requirements example)
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(_rowsInput, 1, 0);

        // --- Row 1: Columns input ---
        var colsLabel = new Label
        {
            Text = "Columns:",
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0)
        };
        layout.Controls.Add(colsLabel, 0, 1);

        _columnsInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 10000,
            Value = 4,           // Default: 4 columns (as in the requirements example)
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(_columnsInput, 1, 1);

        // --- Row 2: Delay input ---
        var delayLabel = new Label
        {
            Text = "Delay (seconds):",
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0)
        };
        layout.Controls.Add(delayLabel, 0, 2);

        _delayInput = new NumericUpDown
        {
            Minimum = 0,             // 0 = no delay (immediate rendering)
            Maximum = 120,
            Value = 8,               // Default: 8 seconds
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(_delayInput, 1, 2);

        // --- Row 3: Plain text checkbox ---
        _plainTextCheckbox = new CheckBox
        {
            Text = "Include Plain Text (text/plain)",
            Checked = true,
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0)
        };
        layout.Controls.Add(_plainTextCheckbox, 0, 3);
        layout.SetColumnSpan(_plainTextCheckbox, 2);

        // --- Row 4: HTML checkbox ---
        _htmlCheckbox = new CheckBox
        {
            Text = "Include HTML (text/html)",
            Checked = true,
            AutoSize = true,
        };
        layout.Controls.Add(_htmlCheckbox, 0, 4);
        layout.SetColumnSpan(_htmlCheckbox, 2);

        // --- Row 5: Custom format checkbox (Chromium web custom format) ---
        _customFormatCheckbox = new CheckBox
        {
            Text = $"Include Custom Format ({ContentGenerator.WebCustomFormatDisplayName})",
            Checked = false,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 5)
        };
        layout.Controls.Add(_customFormatCheckbox, 0, 5);
        layout.SetColumnSpan(_customFormatCheckbox, 2);

        // --- Row 6: Copy button ---
        _copyButton = new Button
        {
            Text = "Copy to Clipboard",
            Height = 40,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
        };
        _copyButton.Click += OnCopyButtonClick;
        layout.Controls.Add(_copyButton, 0, 6);
        layout.SetColumnSpan(_copyButton, 2);

        // --- Row 7: Log header (label on the left, Clear Logs button on the right) ---
        // Nested panel so the label and button share a single layout row without
        // interfering with the main grid's auto/fill column sizing.
        var logHeaderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4)
        };
        logHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeaderPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var logLabel = new Label
        {
            Text = "Activity Log:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 0, 0)
        };
        logHeaderPanel.Controls.Add(logLabel, 0, 0);

        _clearLogButton = new Button
        {
            Text = "Clear Logs",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(8, 2, 8, 2)
        };
        _clearLogButton.Click += OnClearLogButtonClick;
        logHeaderPanel.Controls.Add(_clearLogButton, 1, 0);

        layout.Controls.Add(logHeaderPanel, 0, 7);
        layout.SetColumnSpan(logHeaderPanel, 2);

        // --- Row 8: Log text box (fills remaining vertical space) ---
        _logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Font = new Font("Consolas", 9)  // Monospace for alignment
        };
        layout.Controls.Add(_logTextBox, 0, 8);
        layout.SetColumnSpan(_logTextBox, 2);

        // Row sizing: all rows auto-size except the last one (log) which fills remaining space
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 0: Rows
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 1: Columns
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 2: Delay
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 3: Plain text checkbox
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 4: HTML checkbox
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 5: Custom format checkbox
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 6: Copy button
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // row 7: Log label
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // row 8: Log (fills space)

        Controls.Add(layout);
    }

    // ========================================================================
    // Copy Button Handler — Promises Formats to Clipboard
    // ========================================================================

    /// <summary>
    /// Handles the "Copy to Clipboard" button click.
    ///
    /// If a rendering operation is currently in progress (the 10-second delay
    /// loop in HandleRenderFormat), this method cancels it and defers the new
    /// copy until after WM_RENDERFORMAT has fully returned.
    ///
    /// Otherwise, it delegates to PerformCopy to promise formats immediately.
    /// </summary>
    private void OnCopyButtonClick(object? sender, EventArgs e)
    {
        // Validate: at least one format must be selected
        if (!_plainTextCheckbox.Checked && !_htmlCheckbox.Checked && !_customFormatCheckbox.Checked)
        {
            MessageBox.Show(
                "Please select at least one format (Plain Text, HTML, or Custom Format).",
                "No Format Selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // If we are currently inside HandleRenderFormat (the delay loop),
        // we cannot open the clipboard because WM_RENDERFORMAT has it open.
        // Instead, signal the render loop to abort. HandleRenderFormat will
        // detect the flag, skip data generation, and use BeginInvoke to
        // call PerformCopy after WM_RENDERFORMAT has fully returned.
        if (_isRendering)
        {
            Log("────────────────────────────────────────────────────────────");
            Log("New copy requested — cancelling in-progress rendering...");
            _cancelRendering = true;
            return;
        }

        PerformCopy();
    }

    /// <summary>
    /// Opens the clipboard, takes ownership, and promises the selected formats
    /// using delayed rendering (SetClipboardData with NULL data handles).
    ///
    /// This method does NOT generate any content. The actual content will be
    /// generated later in HandleRenderFormat when another application pastes.
    ///
    /// Extracted from OnCopyButtonClick so it can also be called via BeginInvoke
    /// after a cancelled rendering operation.
    /// </summary>
    private void PerformCopy()
    {
        Log("────────────────────────────────────────────────────────────");

        // Step 1: Open the clipboard, associating it with our window handle.
        // This is required before any clipboard modification.
        if (!NativeMethods.OpenClipboard(Handle))
        {
            Log("ERROR: Failed to open clipboard. Another application may have it locked.");
            return;
        }

        try
        {
            // Step 2: Empty the clipboard to take ownership.
            // After EmptyClipboard, our window (Handle) becomes the clipboard owner.
            // This means we will receive WM_RENDERFORMAT messages.
            //
            // IMPORTANT: EmptyClipboard sends WM_DESTROYCLIPBOARD synchronously
            // to the current owner. If WE are the current owner (i.e., the user
            // clicked Copy a second time), our WM_DESTROYCLIPBOARD handler will
            // clear _promisedPlainText and _promisedHtml. Therefore, we must
            // snapshot the user's configuration AFTER this call, not before.
            if (!NativeMethods.EmptyClipboard())
            {
                Log("ERROR: Failed to empty clipboard.");
                return;
            }

            // Step 3: Snapshot the user's configuration AFTER EmptyClipboard.
            // This must happen after EmptyClipboard because EmptyClipboard sends
            // WM_DESTROYCLIPBOARD synchronously to the previous owner (which may
            // be us), and our handler clears the promised flags. Setting them
            // before EmptyClipboard would cause them to be wiped on re-copy.
            _promisedRows = (int)_rowsInput.Value;
            _promisedColumns = (int)_columnsInput.Value;
            _promisedDelaySeconds = (int)_delayInput.Value;
            _promisedPlainText = _plainTextCheckbox.Checked;
            _promisedHtml = _htmlCheckbox.Checked;
            _promisedCustom = _customFormatCheckbox.Checked;

            var formatList = new List<string>();
            if (_promisedPlainText) formatList.Add("PlainText");
            if (_promisedHtml) formatList.Add("HTML");
            if (_promisedCustom) formatList.Add("WebCustomFormat");

            Log($"Copy initiated: {_promisedRows} rows x {_promisedColumns} columns, delay: {_promisedDelaySeconds}s");
            Log($"Formats: {string.Join(" + ", formatList)}");

            // Step 4: Promise formats using delayed rendering.
            // By passing IntPtr.Zero as the data handle, we tell Windows:
            // "This format is available, but I'll provide the actual data later."
            // Windows will send us WM_RENDERFORMAT when the data is needed.

            if (_promisedPlainText)
            {
                IntPtr result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, IntPtr.Zero);
                if (result == IntPtr.Zero)
                    Log("ERROR: Failed to promise CF_UNICODETEXT format.");
                else
                    Log("Promised CF_UNICODETEXT (plain text) — data will be generated on paste.");
            }

            if (_promisedHtml)
            {
                IntPtr result = NativeMethods.SetClipboardData(_cfHtml, IntPtr.Zero);
                if (result == IntPtr.Zero)
                    Log($"ERROR: Failed to promise HTML Format (ID: {_cfHtml}).");
                else
                    Log($"Promised HTML Format (ID: {_cfHtml}) — data will be generated on paste.");
            }

            // The web custom format requires two promises: the map (metadata
            // pointing to the payload slot) and the slot itself (the JSON).
            // Both must be promised for Chromium-aware consumers to find
            // "web data/my-custom-format" on the clipboard.
            if (_promisedCustom)
            {
                IntPtr mapResult = NativeMethods.SetClipboardData(_cfWebCustomMap, IntPtr.Zero);
                if (mapResult == IntPtr.Zero)
                    Log($"ERROR: Failed to promise Web Custom Format Map (ID: {_cfWebCustomMap}).");
                else
                    Log($"Promised Web Custom Format Map (ID: {_cfWebCustomMap}) — map will be generated on paste.");

                IntPtr slotResult = NativeMethods.SetClipboardData(_cfWebCustomFormat0, IntPtr.Zero);
                if (slotResult == IntPtr.Zero)
                    Log($"ERROR: Failed to promise {ContentGenerator.WebCustomFormatSlotName} (ID: {_cfWebCustomFormat0}).");
                else
                    Log($"Promised {ContentGenerator.WebCustomFormatSlotName} (ID: {_cfWebCustomFormat0}) — payload will be generated on paste.");
            }

            Log("Clipboard is ready. Try pasting (Ctrl+V) in another application!");
        }
        finally
        {
            // Step 5: Always close the clipboard, even if errors occurred.
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>
    /// Handles the "Clear Logs" button click. Empties the activity log so the
    /// user can focus on the most recent messages. Safe to call at any time —
    /// clearing the UI text box has no effect on the clipboard state or any
    /// in-progress rendering.
    /// </summary>
    private void OnClearLogButtonClick(object? sender, EventArgs e)
    {
        _logTextBox.Clear();
        Log("Activity log cleared.");
    }

    // ========================================================================
    // Window Procedure Override — Clipboard Message Handling
    // ========================================================================

    /// <summary>
    /// Overrides the window procedure to intercept clipboard-related messages.
    ///
    /// This is the heart of delayed clipboard rendering. Windows sends these
    /// messages to the clipboard owner (us) when clipboard data is needed.
    ///
    /// The three clipboard messages we handle:
    /// - WM_RENDERFORMAT:     "An app wants format X — provide it now."
    /// - WM_RENDERALLFORMATS: "Your window is closing — provide ALL formats now."
    /// - WM_DESTROYCLIPBOARD: "Another app took clipboard ownership — you're done."
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_RENDERFORMAT:
                // Another application is requesting data for a specific format.
                // wParam contains the clipboard format ID being requested.
                HandleRenderFormat((uint)(nint)m.WParam);
                return; // Message handled — do not pass to base

            case NativeMethods.WM_RENDERALLFORMATS:
                // Our window is being destroyed while we still own the clipboard.
                // We must render all promised formats so the data survives our exit.
                HandleRenderAllFormats();
                return;

            case NativeMethods.WM_DESTROYCLIPBOARD:
                // Another application called EmptyClipboard(), taking ownership.
                // Our promises are void — clean up our tracking state.
                Log("Clipboard ownership lost — another application took the clipboard.");
                _promisedPlainText = false;
                _promisedHtml = false;
                _promisedCustom = false;
                return;
        }

        // Pass all other messages to the default WinForms handler
        base.WndProc(ref m);
    }

    // ========================================================================
    // Delayed Rendering Handlers
    // ========================================================================

    /// <summary>
    /// Handles WM_RENDERFORMAT: generates and provides data for one specific format.
    ///
    /// Called by Windows when another application requests clipboard data.
    /// The clipboard is already open on our behalf — do NOT call
    /// OpenClipboard/CloseClipboard inside this handler.
    ///
    /// Delay behavior:
    /// - Payload formats (plain text, HTML, web custom payload) wait the
    ///   user-configured delay to simulate expensive generation.
    /// - The Web Custom Format Map renders instantly — it is trivial
    ///   metadata with a fixed structure. This avoids doubling the
    ///   apparent paste latency when a Chromium consumer fetches both
    ///   the map and the payload for a single paste.
    ///
    /// Cancellation flow (payload formats only):
    /// 1. User clicks Copy during the delay → OnCopyButtonClick sets _cancelRendering
    /// 2. The delay loop detects the flag and exits early
    /// 3. We skip data generation (Windows treats the format as empty)
    /// 4. We use BeginInvoke to call PerformCopy after WM_RENDERFORMAT returns
    /// 5. PerformCopy re-promises the formats with the new configuration
    /// </summary>
    /// <param name="format">The clipboard format ID being requested.</param>
    private void HandleRenderFormat(uint format)
    {
        _isRendering = true;
        _cancelRendering = false;

        string formatName = GetFormatName(format);
        Log($">>> WM_RENDERFORMAT received — format requested: {formatName}");

        // Dispatch on format. The map renders without a delay; everything
        // else is treated as an "expensive" payload and uses the delay loop.
        if (format == _cfWebCustomMap && _promisedCustom)
        {
            Log("    Generating Web Custom Format Map (instant, no delay)...");
            _logTextBox.Update();
            RenderWebCustomMap();
        }
        else if (format == NativeMethods.CF_UNICODETEXT && _promisedPlainText)
        {
            if (RunCancellableDelay()) return;
            RenderPlainText();
        }
        else if (format == _cfHtml && _promisedHtml)
        {
            if (RunCancellableDelay()) return;
            RenderHtml();
        }
        else if (format == _cfWebCustomFormat0 && _promisedCustom)
        {
            if (RunCancellableDelay()) return;
            RenderWebCustomPayload();
        }
        else
        {
            Log($"    WARNING: Received request for unexpected format: {formatName} (ID: {format})");
        }

        _isRendering = false;
    }

    /// <summary>
    /// Runs the cancellable delay loop used before generating a payload
    /// format. Returns true if the user cancelled mid-delay, in which case
    /// the caller must return immediately without calling SetClipboardData —
    /// PerformCopy will be re-scheduled via BeginInvoke to re-promise fresh
    /// formats after WM_RENDERFORMAT has fully returned.
    ///
    /// Uses Application.DoEvents + small Thread.Sleep intervals instead of
    /// a single Thread.Sleep so the UI stays responsive and the user's
    /// second Copy click can fire OnCopyButtonClick to flip _cancelRendering.
    /// </summary>
    /// <returns>true if cancelled and caller should abort; false otherwise.</returns>
    private bool RunCancellableDelay()
    {
        Log($"    Generating {_promisedRows}x{_promisedColumns} table content...");
        Log($"    Simulating {_promisedDelaySeconds}-second delay (click 'Copy to Clipboard' to cancel)...");
        _logTextBox.Update();

        const int sleepIntervalMs = 50;
        int elapsed = 0;
        while (elapsed < _promisedDelaySeconds * 1000)
        {
            Application.DoEvents();
            if (_cancelRendering)
                break;
            Thread.Sleep(sleepIntervalMs);
            elapsed += sleepIntervalMs;
        }

        if (_cancelRendering)
        {
            Log("    Rendering cancelled. Will re-promise formats with new settings.");
            _isRendering = false;
            BeginInvoke(PerformCopy);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles WM_RENDERALLFORMATS: renders ALL promised formats.
    ///
    /// This is sent when the application is closing while still owning the clipboard.
    /// Unlike WM_RENDERFORMAT, we MUST call OpenClipboard/CloseClipboard ourselves
    /// in this handler.
    ///
    /// This ensures clipboard data is preserved even after our app exits.
    /// </summary>
    private void HandleRenderAllFormats()
    {
        Log(">>> WM_RENDERALLFORMATS received — rendering all promised formats before exit...");

        // For WM_RENDERALLFORMATS, we must open and close the clipboard ourselves
        // (unlike WM_RENDERFORMAT where the clipboard is already open).
        if (!NativeMethods.OpenClipboard(Handle))
        {
            Log("    ERROR: Failed to open clipboard for RenderAllFormats.");
            return;
        }

        try
        {
            if (_promisedPlainText)
            {
                Log("    Rendering plain text for clipboard persistence...");
                RenderPlainText();
            }

            if (_promisedHtml)
            {
                Log("    Rendering HTML for clipboard persistence...");
                RenderHtml();
            }

            if (_promisedCustom)
            {
                Log("    Rendering Web Custom Format Map for clipboard persistence...");
                RenderWebCustomMap();

                Log("    Rendering Web Custom Format payload for clipboard persistence...");
                RenderWebCustomPayload();
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }

        Log("    All formats rendered. Application can now safely exit.");
    }

    // ========================================================================
    // Data Rendering — Converting Content to Clipboard-Ready Memory Blocks
    // ========================================================================

    /// <summary>
    /// Generates plain text table content and places it on the clipboard.
    ///
    /// The clipboard requires data in a specific format:
    /// - Memory allocated with GlobalAlloc(GMEM_MOVEABLE)
    /// - CF_UNICODETEXT data must be UTF-16 encoded and null-terminated
    /// - After SetClipboardData succeeds, the clipboard OWNS the memory
    ///   (do NOT call GlobalFree on it)
    /// </summary>
    private void RenderPlainText()
    {
        // Generate the tab-separated table content
        string text = ContentGenerator.GeneratePlainText(_promisedRows, _promisedColumns);

        // Encode as UTF-16 with null terminator (required for CF_UNICODETEXT)
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');

        // Allocate a moveable global memory block for the clipboard
        IntPtr hGlobal = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE,
            (UIntPtr)bytes.Length);

        if (hGlobal == IntPtr.Zero)
        {
            Log("    ERROR: GlobalAlloc failed for plain text data.");
            return;
        }

        // Lock the memory to get a writable pointer, copy the data, then unlock
        IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            Log("    ERROR: GlobalLock failed for plain text data.");
            NativeMethods.GlobalFree(hGlobal);
            return;
        }

        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        NativeMethods.GlobalUnlock(hGlobal);

        // Hand the memory to the clipboard. After this call, the clipboard
        // owns the memory block — we must NOT free it ourselves.
        NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);

        Log($"    Plain text rendered and placed on clipboard ({bytes.Length} bytes, UTF-16).");
    }

    /// <summary>
    /// Generates HTML table content in Windows HTML clipboard format and places it
    /// on the clipboard.
    ///
    /// Key difference from plain text: the HTML clipboard format uses UTF-8 encoding
    /// (not UTF-16), and includes a header with byte offsets pointing to the
    /// HTML fragment within the data.
    /// </summary>
    private void RenderHtml()
    {
        // Generate the complete HTML clipboard format string (header + HTML)
        string htmlData = ContentGenerator.GenerateHtmlClipboardData(_promisedRows, _promisedColumns);

        // Encode as UTF-8 with null terminator (HTML clipboard format uses UTF-8)
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(htmlData + '\0');

        // Allocate moveable global memory
        IntPtr hGlobal = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE,
            (UIntPtr)bytes.Length);

        if (hGlobal == IntPtr.Zero)
        {
            Log("    ERROR: GlobalAlloc failed for HTML data.");
            return;
        }

        // Lock, copy, unlock
        IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            Log("    ERROR: GlobalLock failed for HTML data.");
            NativeMethods.GlobalFree(hGlobal);
            return;
        }

        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        NativeMethods.GlobalUnlock(hGlobal);

        // Hand the memory to the clipboard
        NativeMethods.SetClipboardData(_cfHtml, hGlobal);

        Log($"    HTML rendered and placed on clipboard ({bytes.Length} bytes, UTF-8).");
    }

    /// <summary>
    /// Generates the Chromium Web Custom Format Map JSON and places it on
    /// the clipboard under the "Web Custom Format Map" registered format.
    ///
    /// The map is a tiny JSON object — no delay is applied when rendering
    /// it. Unlike the text formats, no null terminator is appended: the
    /// clipboard-provided byte size is authoritative for binary/structured
    /// custom formats, and a trailing NUL would corrupt strict JSON parsers
    /// that read the full buffer.
    /// </summary>
    private void RenderWebCustomMap()
    {
        string json = ContentGenerator.GenerateWebCustomFormatMap();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        IntPtr hGlobal = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE,
            (UIntPtr)bytes.Length);

        if (hGlobal == IntPtr.Zero)
        {
            Log("    ERROR: GlobalAlloc failed for Web Custom Format Map.");
            return;
        }

        IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            Log("    ERROR: GlobalLock failed for Web Custom Format Map.");
            NativeMethods.GlobalFree(hGlobal);
            return;
        }

        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        NativeMethods.GlobalUnlock(hGlobal);

        NativeMethods.SetClipboardData(_cfWebCustomMap, hGlobal);

        Log($"    Web Custom Format Map rendered and placed on clipboard ({bytes.Length} bytes, UTF-8): {json}");
    }

    /// <summary>
    /// Generates the custom JSON payload and places it on the clipboard
    /// under the "Web Custom Format0" registered format (the slot the map
    /// points to for "web data/my-custom-format").
    ///
    /// UTF-8 encoded, no null terminator — consumers use GlobalSize / the
    /// clipboard-provided byte length as the authoritative size.
    /// </summary>
    private void RenderWebCustomPayload()
    {
        string json = ContentGenerator.GenerateCustomFormatJson(_promisedRows, _promisedColumns);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

        IntPtr hGlobal = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE,
            (UIntPtr)bytes.Length);

        if (hGlobal == IntPtr.Zero)
        {
            Log("    ERROR: GlobalAlloc failed for Web Custom Format payload.");
            return;
        }

        IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            Log("    ERROR: GlobalLock failed for Web Custom Format payload.");
            NativeMethods.GlobalFree(hGlobal);
            return;
        }

        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        NativeMethods.GlobalUnlock(hGlobal);

        NativeMethods.SetClipboardData(_cfWebCustomFormat0, hGlobal);

        Log($"    Web Custom Format payload rendered and placed on clipboard ({bytes.Length} bytes, UTF-8).");
    }

    // ========================================================================
    // Utility Methods
    // ========================================================================

    /// <summary>
    /// Returns a human-readable name for a clipboard format ID.
    /// </summary>
    private string GetFormatName(uint format)
    {
        if (format == NativeMethods.CF_UNICODETEXT) return "CF_UNICODETEXT (Plain Text)";
        if (format == _cfHtml) return "HTML Format";
        if (format == _cfWebCustomMap) return "Web Custom Format Map";
        if (format == _cfWebCustomFormat0) return $"{ContentGenerator.WebCustomFormatSlotName} ({ContentGenerator.WebCustomFormatDisplayName} payload)";
        return $"Unknown Format (ID: {format})";
    }

    /// <summary>
    /// Appends a timestamped message to the activity log text box.
    /// The log provides visibility into the delayed rendering process,
    /// showing exactly when formats are promised and when they are rendered.
    /// </summary>
    private void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _logTextBox.AppendText($"[{timestamp}] {message}\r\n");
    }
}
