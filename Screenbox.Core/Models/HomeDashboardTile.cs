#nullable enable

namespace Screenbox.Core.Models;

public enum HomeDashboardTileKind
{
    VideoFolder,
    Favorites,
    Tag,
    Playlist
}

public sealed class HomeDashboardTile
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string Glyph { get; set; } = string.Empty;

    public HomeDashboardTileKind Kind { get; set; }

    public object? Target { get; set; }
}
