using BmSDK.Framework;

namespace Trinity.Batwing;

internal static unsafe class ObjectTable
{
    public const int Capacity = 785000;

    private static int _alive = -1;
    private static Every _count;

    // The SDK knows where the object list lives on Steam and on Epic. It holds Capacity slots inline
    // with the used count right after them, and a freed slot is left empty.
    public static int Alive
    {
        get
        {
            if (_count.Elapsed(1.0))
                _alive = Count();
            return _alive;
        }
    }

    public static int Headroom => Alive is var alive and >= 0 ? Capacity - alive : int.MaxValue;

    public static string Describe() => Alive is var alive and >= 0
        ? alive.ToString("N0") + " of " + Capacity.ToString("N0") + " objects"
        : "object count unknown";

    private static int Count()
    {
        try
        {
            ulong* slots = (ulong*)MemUtil.GetIntPointer(GameDefine.Current.GObjObjects);
            int used = *(int*)((byte*)slots + Capacity * 8L + 4);
            if (used <= 0 || used > Capacity)
                return -1;

            int alive = 0;
            for (int i = 0; i < used; i++)
            {
                if (slots[i] != 0)
                    alive++;
            }

            return alive;
        }
        catch
        {
            return -1;
        }
    }
}
