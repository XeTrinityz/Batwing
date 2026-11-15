namespace Trinity.Batwing;

// How full the game's object table is. The game dies outright when it fills, and fast flight streams in
// districts quicker than the game collects the old ones, so loading anything checks this first.
internal static unsafe class ObjectTable
{
    public const int Capacity = 785000;

    // Below this the mod stops loading and asks for a collection.
    public const int Low = 90000;

    // Below this nothing more gets loaded at all.
    public const int Full = 40000;

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

            return used - MemoryExtensions.Count(new ReadOnlySpan<ulong>(slots, used), 0UL);
        }
        catch
        {
            return -1;
        }
    }
}
