using MockRoom.Core.Rooms;

namespace MockRoom.ViewModels;

/// <summary>
/// Represents one wall side in the opening-editor wall picker, carrying whether a
/// standard door swing can fit there without colliding with existing floor items.
/// </summary>
public sealed class WallOption(WallSide side, bool canFit)
{
    public WallSide Side { get; } = side;
    public bool CanFit { get; } = canFit;

    /// <summary>Display text — appends "(no room)" for walls where no door swing can clear the furniture.</summary>
    public string Label => CanFit ? Side.ToString() : $"{Side} (no room)";
}
