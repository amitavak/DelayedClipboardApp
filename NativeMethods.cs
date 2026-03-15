using System.Runtime.InteropServices;

namespace DelayedClipboardApp;

/// <summary>
/// Contains Win32 P/Invoke declarations for clipboard operations.
///
/// .NET's built-in System.Windows.Forms.Clipboard class does NOT support delayed
/// clipboard rendering. We must use the Win32 API directly to:
/// 1. Promise clipboard formats without providing data (SetClipboardData with NULL)
/// 2. Respond to WM_RENDERFORMAT when another app requests the data
///
/// See: https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations#delayed-rendering
/// </summary>
internal static class NativeMethods
{
    // ========================================================================
    // Clipboard format constants
    // ========================================================================

    /// <summary>
    /// Unicode text format. Data is a null-terminated UTF-16 string.
    /// This is the standard format for text on modern Windows.
    /// </summary>
    public const uint CF_UNICODETEXT = 13;

    // ========================================================================
    // Memory allocation flags
    // ========================================================================

    /// <summary>
    /// Allocates moveable memory. Required for clipboard data because
    /// Windows needs to be able to move the memory block.
    /// </summary>
    public const uint GMEM_MOVEABLE = 0x0002;

    // ========================================================================
    // Windows messages related to clipboard operations
    // ========================================================================

    /// <summary>
    /// Sent to the clipboard owner when a specific format needs to be rendered.
    /// wParam contains the clipboard format ID that is being requested.
    /// This is the core message for delayed rendering.
    /// </summary>
    public const int WM_RENDERFORMAT = 0x0305;

    /// <summary>
    /// Sent to the clipboard owner when the owner window is being destroyed
    /// and there are still delayed-rendered formats that haven't been rendered.
    /// The owner must render ALL promised formats before the window is destroyed.
    /// </summary>
    public const int WM_RENDERALLFORMATS = 0x0306;

    /// <summary>
    /// Sent to the clipboard owner when the clipboard is emptied by another
    /// application calling EmptyClipboard(). This means we've lost ownership.
    /// </summary>
    public const int WM_DESTROYCLIPBOARD = 0x0307;

    // ========================================================================
    // Clipboard functions (user32.dll)
    // ========================================================================

    /// <summary>
    /// Opens the clipboard for examination and prevents other applications
    /// from modifying the clipboard content. The specified window becomes
    /// associated with the open clipboard.
    /// </summary>
    /// <param name="hWndNewOwner">Handle to the window to be associated with the clipboard.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    /// <summary>
    /// Closes the clipboard after it has been opened with OpenClipboard.
    /// Must be called after every successful OpenClipboard.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    /// <summary>
    /// Empties the clipboard and frees handles to data in the clipboard.
    /// The window specified in OpenClipboard becomes the clipboard owner.
    /// Must be called before placing data on the clipboard.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    /// <summary>
    /// Places data on the clipboard in a specified format.
    ///
    /// For delayed rendering: pass IntPtr.Zero as hData to promise the format
    /// without providing data yet. The clipboard owner will receive
    /// WM_RENDERFORMAT when the data is actually needed.
    ///
    /// For immediate rendering: pass a valid HGLOBAL handle allocated with
    /// GlobalAlloc(GMEM_MOVEABLE). After this call, the clipboard owns the memory.
    /// </summary>
    /// <param name="uFormat">The clipboard format ID (e.g., CF_UNICODETEXT or a registered format).</param>
    /// <param name="hData">Handle to the data, or IntPtr.Zero for delayed rendering.</param>
    /// <returns>Handle to the data if successful, IntPtr.Zero on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hData);

    /// <summary>
    /// Registers a new clipboard format by name. If the format already exists,
    /// returns the existing format ID. Used to register "HTML Format".
    /// </summary>
    /// <param name="lpszFormat">The name of the new format (e.g., "HTML Format").</param>
    /// <returns>The registered format ID, or 0 on failure.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);

    // ========================================================================
    // Global memory functions (kernel32.dll)
    // ========================================================================

    /// <summary>
    /// Allocates memory from the global heap. Used with GMEM_MOVEABLE to
    /// create memory blocks suitable for clipboard data.
    /// </summary>
    /// <param name="uFlags">Memory allocation attributes (use GMEM_MOVEABLE).</param>
    /// <param name="dwBytes">Number of bytes to allocate.</param>
    /// <returns>Handle to the allocated memory, or IntPtr.Zero on failure.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    /// <summary>
    /// Locks a global memory object and returns a pointer to the first byte.
    /// The memory remains locked until GlobalUnlock is called.
    /// </summary>
    /// <param name="hMem">Handle to the global memory object.</param>
    /// <returns>Pointer to the first byte, or IntPtr.Zero on failure.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    /// <summary>
    /// Decrements the lock count for a global memory object.
    /// When the count reaches zero, the memory can be moved or discarded.
    /// </summary>
    /// <param name="hMem">Handle to the global memory object.</param>
    /// <returns>True if the memory is still locked, false if fully unlocked.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    /// <summary>
    /// Frees the specified global memory object. Only use this for memory
    /// that you allocated but did NOT pass to SetClipboardData (since the
    /// clipboard takes ownership of that memory).
    /// </summary>
    /// <param name="hMem">Handle to the global memory object to free.</param>
    /// <returns>IntPtr.Zero if successful, hMem on failure.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);
}
