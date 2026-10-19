using System;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using LucentUI.Config;

namespace LucentUI.Style;

/// <summary>Owns the two game fonts the HUD uses, rebuilding them when the size setting changes.</summary>
public sealed class Fonts : IDisposable
{
    private IFontHandle? main;
    private IFontHandle? small;
    private int mainSize;
    private int smallSize;

    public void Ensure(StyleSettings style)
    {
        var wantMain = Math.Clamp(style.FontSize, 8, 40);
        var wantSmall = Math.Clamp(style.SmallFontSize, 8, 40);

        if (main == null || wantMain != mainSize)
        {
            main?.Dispose();
            main = Create(wantMain);
            mainSize = wantMain;
        }

        if (small == null || wantSmall != smallSize)
        {
            small?.Dispose();
            small = Create(wantSmall);
            smallSize = wantSmall;
        }
    }

    public IDisposable PushMain() => main?.Push() ?? Noop.Instance;

    public IDisposable PushSmall() => small?.Push() ?? Noop.Instance;

    public void Dispose()
    {
        main?.Dispose();
        small?.Dispose();
        main = null;
        small = null;
    }

    private static IFontHandle Create(int sizePx) =>
        Svc.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, sizePx));

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();

        public void Dispose()
        {
        }
    }
}
