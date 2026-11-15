using System.Runtime.InteropServices;

namespace Trinity.Batwing;

internal static class Native
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    private static int _frame = -1;
    private static bool _front;

    // Asked many times a frame, so the answer is kept for the frame.
    public static bool GameInFront
    {
        get
        {
            if (_frame == Gfx.Frame)
                return _front;

            _frame = Gfx.Frame;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            _front = pid == (uint)Environment.ProcessId;
            return _front;
        }
    }
}
