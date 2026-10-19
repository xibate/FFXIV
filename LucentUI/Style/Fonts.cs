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
    private bool created;
    private int mainSize = 16;
    private int smallSize = 13;

    public int MainSize => mainSize;

    public void Ensure(StyleSettings style)
    {
        if (!created || style.Font != family)
        {
            family = style.Font;
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
            entry = (Svc.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle((GameFontFamily)((int)family + 1), size)), 0);
        }

        handles[size] = (entry.Handle, ImGui.GetFrameCount());
        return entry.Handle.Push();
    }

    public void Dispose()
    {
        foreach (var (handle, _) in handles.Values)
        {
            handle.Dispose();
        }

        handles.Clear();
    }
}
