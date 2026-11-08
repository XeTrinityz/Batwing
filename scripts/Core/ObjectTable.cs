using System.Diagnostics;

namespace Trinity.Batwing;

internal static unsafe class ObjectTable
{
    public const int Capacity = 785000;

    private const nint SlotsUsed = 0x3A09F28;
    private const nint FreeCount = 0x3A09F70;

    private static nint _base;

    private static nint Base => _base != 0 ? _base : _base = Process.GetCurrentProcess().MainModule?.BaseAddress ?? 0;

    public static int Alive
    {
        get
        {
            try
            {
                if (Base == 0)
                    return -1;
                int used = *(int*)(Base + SlotsUsed);
                int free = *(int*)(Base + FreeCount);
                if (used <= 0 || used > Capacity || free < 0 || free > used)
                    return -1;
                return used - free;
            }
            catch
            {
                return -1;
            }
        }
    }

    public static int Headroom => Alive is var alive and >= 0 ? Capacity - alive : int.MaxValue;

    public static string Describe() => Alive is var alive and >= 0
        ? alive.ToString("N0") + " of " + Capacity.ToString("N0") + " objects"
        : "object count unknown";
}
