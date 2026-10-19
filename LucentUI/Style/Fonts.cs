using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using LucentUI.Config;

namespace LucentUI.Style;

/// <summary>Game fonts at any size, created on first use and dropped when unused for a while.</summary>
public sealed class Fonts : IDisposable
{
    private const int MinSize = 8;
    private const int MaxSize = 48;
    private const int UnusedFrames = 600;

    private readonly Dictionary<int, (IFontHandle Handle, int LastUsed)> handles = new();
    private FontFamily family;
    private string? customPath;
    private int mediaVersion = -1;
    private bool created;
    private int mainSize = 16;
    private int smallSize = 13;

    public int MainSize => mainSize;

    public void Ensure(StyleSettings style)
    {
        // A font from the media library replaces the game font; rebuild when either changes.
        var media = Plugin.Media;
        var path = media?.FontPath(style.FontName);
        var version = media?.Version ?? 0;
        if (!created || style.Font != family || path != customPath || (path != null && version != mediaVersion))
        {
            family = style.Font;
            customPath = path;
            mediaVersion = version;
            Dispose();
            created = true;
        }

        mainSize = Math.Clamp(style.FontSize, MinSize, MaxSize);
        smallSize = Math.Clamp(style.SmallFontSize, MinSize, MaxSize);

        // Drop sizes nobody has drawn with for a while (e.g. left over from dragging a size slider).
        var frame = ImGui.GetFrameCount();
        if (frame % 300 == 0)
        {
            var stale = new List<int>();
            foreach (var (size, entry) in handles)
            {
                if (size != mainSize && size != smallSize && frame - entry.LastUsed > UnusedFrames)
                {
                    stale.Add(size);
                }
            }

            foreach (var size in stale)
            {
                handles[size].Handle.Dispose();
                handles.Remove(size);
            }
        }
    }

    public IDisposable PushMain() => Push(mainSize);

    public IDisposable PushSmall() => Push(smallSize);

    /// <summary>Pushes the font at <paramref name="size"/> pixels; 0 or less means the main size.</summary>
    public IDisposable Push(int size)
    {
        size = size <= 0 ? mainSize : Math.Clamp(size, MinSize, MaxSize);
        if (!handles.TryGetValue(size, out var entry))
        {
            entry = (customPath != null ? NewFileFont(customPath, size) : Svc.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle((GameFontFamily)((int)family + 1), size)), 0);
        }

        handles[size] = (entry.Handle, ImGui.GetFrameCount());
        return entry.Handle.Push();
    }

    /// <summary>A .ttf/.otf font at <paramref name="size"/> pixels, with the game's glyphs (icons, Japanese) merged in.</summary>
    private static IFontHandle NewFileFont(string path, int size)
    {
        return Svc.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var config = new SafeFontConfig { SizePx = size };
            ImFontPtr font;
            try
            {
                font = tk.AddFontFromFile(path, config);
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"Could not load font {path}: {ex.Message}");
                tk.AddDalamudDefaultFont(size, null);
                return;
            }

            try
            {
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, size), GameGlyphRanges, font);
            }
            catch (Exception)
            {
                // The custom font still works without the extra glyphs.
            }
        }));
    }

    // Private-use (game icons), Japanese kana, CJK ideographs and full-width forms.
    private static readonly ushort[] GameGlyphRanges = { 0x3000, 0x30FF, 0x4E00, 0x9FAF, 0xE000, 0xF8FF, 0xFF00, 0xFFEF, 0 };

    public void Dispose()
    {
        foreach (var (handle, _) in handles.Values)
        {
            handle.Dispose();
        }

        handles.Clear();
    }
}
