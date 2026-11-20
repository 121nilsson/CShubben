namespace Cshubben.Core.Features.CommandPalette;

/// <summary>Selection movement for the result list.</summary>
public static class PaletteNavigation
{
    /// <summary>Moves by delta and wraps around at both ends. 0 when the list is empty.</summary>
    public static int Move(int current, int delta, int count)
    {
        if (count <= 0) return 0;
        var next = (Clamp(current, count) + delta) % count;
        return next < 0 ? next + count : next;
    }

    /// <summary>Keeps an index inside the list (0 for an empty list).</summary>
    public static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Min(Math.Max(index, 0), count - 1);
}
