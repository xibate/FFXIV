using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace LucentUI.Style;

/// <summary>
/// Fonts and bar textures found in media folders: LucentUI's own Media folder plus any folders the user adds
/// (for example WoW's Interface\AddOns, so SharedMedia packs and EllesmereUI's media work as they are).
/// Media is known by name: the name an addon registers it under with LibSharedMedia, or else its file name.
/// </summary>
public sealed class MediaLibrary : IDisposable
{
    private static readonly string[] FontExtensions = { ".ttf", ".otf" };
    private static readonly string[] TextureExtensions = { ".tga", ".blp", ".png", ".jpg", ".jpeg" };
    private const int MaxFiles = 300_000;
    private const long MaxLuaSize = 1_500_000;

    // LSM:Register("statusbar", "Name", [[Interface\AddOns\...]]) and LSM.MediaType.STATUSBAR forms.
    private static readonly Regex Registration = new(
        @"Register\s*\(\s*(?:[""'](?<kind>statusbar|font|background|border)[""']|(?:[\w.]*\.)?MediaType\.(?<kind>STATUSBAR|FONT|BACKGROUND|BORDER))\s*,\s*[""'](?<name>[^""'\r\n]+)[""']\s*,\s*(?:\[=*\[(?<path>[^\]\r\n]+)\]=*\]|""(?<path>[^""\r\n]+)""|'(?<path>[^'\r\n]+)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object gate = new();
    private Dictionary<string, string> fonts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureSlot> loaded = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? scan;
    private readonly List<IDalamudTextureWrap> retired = new();

    private sealed class TextureSlot
    {
        public IDalamudTextureWrap? Wrap;
        public ISharedImmediateTexture? Shared;
        public bool Failed;
        public Task? Loading;
    }

    public MediaLibrary(string defaultFolder)
    {
        DefaultFolder = defaultFolder;
    }

    /// <summary>LucentUI's own media folder (always scanned).</summary>
    public string DefaultFolder { get; }

    public bool Scanning { get; private set; }

    public string Status { get; private set; } = "Not scanned yet.";

    /// <summary>Bumped after every scan, so fonts know to rebuild.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<string> FontNames { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> TextureNames { get; private set; } = Array.Empty<string>();

    public string? FontPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (gate)
        {
            return fonts.TryGetValue(name, out var path) ? path : null;
        }
    }

    public bool HasTexture(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        lock (gate)
        {
            return textures.ContainsKey(name);
        }
    }

    public bool HasFont(string? name) => FontPath(name) != null;

    /// <summary>Scans the default folder and <paramref name="folders"/> in the background.</summary>
    public void Rescan(IEnumerable<string> folders)
    {
        scan?.Cancel();
        var cancel = scan = new CancellationTokenSource();
        var list = new List<string> { DefaultFolder };
        list.AddRange(folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().Trim('"')));
        Scanning = true;
        Status = "Scanning…";

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(DefaultFolder, "Fonts"));
                Directory.CreateDirectory(Path.Combine(DefaultFolder, "Textures"));
            }
            catch (Exception)
            {
                // The folder is only a convenience.
            }

            var (newFonts, newTextures, files, missing) = Scan(list, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            lock (gate)
            {
                fonts = newFonts;
                textures = newTextures;
                FontNames = newFonts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                TextureNames = newTextures.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                // The old textures may still be drawn this frame; free them at the start of the next one.
                foreach (var slot in loaded.Values)
                {
                    if (slot.Wrap != null)
                    {
                        retired.Add(slot.Wrap);
                    }
                }

                loaded.Clear();
            }

            Status = $"{newFonts.Count} fonts and {newTextures.Count} textures found in {files:N0} files." +
                     (missing.Count > 0 ? " Not found: " + string.Join(", ", missing) : string.Empty);
            Scanning = false;
            Version++;
        }, cancel.Token);
    }

    public static (Dictionary<string, string> Fonts, Dictionary<string, string> Textures, int Files, List<string> Missing) Scan(List<string> folders, CancellationToken cancel)
    {
        var fontFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var textureFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var registeredFonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var registeredTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var files = 0;

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                if (folder != folders[0])
                {
                    missing.Add(folder);
                }

                continue;
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System });
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in entries)
            {
                if (cancel.IsCancellationRequested || ++files > MaxFiles)
                {
                    break;
                }

                var ext = Path.GetExtension(file).ToLowerInvariant();
                var name = Path.GetFileNameWithoutExtension(file);
                if (FontExtensions.Contains(ext))
                {
                    fontFiles.TryAdd(name, file);
                }
                else if (TextureExtensions.Contains(ext))
                {
                    // Prefer formats we decode best when the same name exists twice.
                    if (!textureFiles.TryGetValue(name, out var existing) || Rank(ext) < Rank(Path.GetExtension(existing)))
                    {
                        textureFiles[name] = file;
                    }
                }
                else if (ext == ".lua")
                {
                    ReadRegistrations(file, registeredFonts, registeredTextures);
                }
            }
        }

        // Registered names win; file names fill in the rest.
        foreach (var (name, path) in fontFiles)
        {
            registeredFonts.TryAdd(name, path);
        }

        foreach (var (name, path) in textureFiles)
        {
            registeredTextures.TryAdd(name, path);
        }

        return (registeredFonts, registeredTextures, files, missing);
    }

    private static int Rank(string ext) => ext.ToLowerInvariant() switch
    {
        ".tga" => 0,
        ".png" => 1,
        ".blp" => 2,
        _ => 3,
    };

    private static void ReadRegistrations(string luaFile, Dictionary<string, string> fonts, Dictionary<string, string> textures)
    {
        try
        {
            var info = new FileInfo(luaFile);
            if (info.Length > MaxLuaSize)
            {
                return;
            }

            var text = File.ReadAllText(luaFile);
            if (!text.Contains("Register", StringComparison.Ordinal))
            {
                return;
            }

            foreach (Match m in Registration.Matches(text))
            {
                var kind = m.Groups["kind"].Value.ToLowerInvariant();
                var name = m.Groups["name"].Value.Trim();
                var path = ResolveAddonPath(luaFile, m.Groups["path"].Value, kind == "font");
                if (path == null || name.Length == 0)
                {
                    continue;
                }

                if (kind == "font")
                {
                    fonts.TryAdd(name, path);
                }
                else
                {
                    textures.TryAdd(name, path);
                }
            }
        }
        catch (Exception)
        {
            // Unreadable files are skipped.
        }
    }

    /// <summary>Turns a WoW media path (Interface\AddOns\Pack\file, extension optional) into a file on disk.</summary>
    private static string? ResolveAddonPath(string luaFile, string wowPath, bool font)
    {
        var path = wowPath.Replace("\\\\", "\\").Replace('\\', '/').Trim();
        var marker = path.IndexOf("interface/addons/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var relative = path[(marker + "interface/addons/".Length)..];

        // Find the AddOns folder above the file that registered it.
        var dir = Path.GetDirectoryName(luaFile);
        while (dir != null && !string.Equals(Path.GetFileName(dir), "AddOns", StringComparison.OrdinalIgnoreCase))
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (dir == null)
        {
            return null;
        }

        var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
        {
            return full;
        }

        // WoW lets texture paths leave out the extension.
        foreach (var ext in font ? FontExtensions : TextureExtensions)
        {
            if (File.Exists(full + ext))
            {
                return full + ext;
            }
        }

        return null;
    }

    /// <summary>Call at the start of a frame, before anything draws: frees textures replaced by a rescan.</summary>
    public void BeginFrame()
    {
        lock (gate)
        {
            foreach (var wrap in retired)
            {
                wrap.Dispose();
            }

            retired.Clear();
        }
    }

    /// <summary>The texture for a name, or null while it loads, if unknown, or if it cannot be read.</summary>
    public IDalamudTextureWrap? Texture(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (gate)
        {
            if (loaded.TryGetValue(name, out var slot))
            {
                if (slot.Shared != null)
                {
                    return slot.Shared.GetWrapOrDefault();
                }

                return slot.Wrap;
            }

            if (!textures.TryGetValue(name, out var path))
            {
                return null;
            }

            slot = new TextureSlot();
            loaded[name] = slot;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg")
            {
                slot.Shared = Svc.TextureProvider.GetFromFileAbsolute(path);
                return slot.Shared.GetWrapOrDefault();
            }

            slot.Loading = Task.Run(async () =>
            {
                try
                {
                    var image = ImageDecoders.Load(path) ?? throw new InvalidDataException("Unsupported image.");
                    var wrap = await Svc.TextureProvider.CreateFromRawAsync(RawImageSpecification.Rgba32(image.Width, image.Height), image.Pixels, "LucentUI " + name);
                    lock (gate)
                    {
                        if (loaded.TryGetValue(name, out var current) && current == slot)
                        {
                            slot.Wrap = wrap;
                        }
                        else
                        {
                            wrap.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    slot.Failed = true;
                    Svc.Log.Warning($"Could not load texture {path}: {ex.Message}");
                }
            });
            return null;
        }
    }

    public void Dispose()
    {
        scan?.Cancel();
        lock (gate)
        {
            foreach (var slot in loaded.Values)
            {
                slot.Wrap?.Dispose();
            }

            foreach (var wrap in retired)
            {
                wrap.Dispose();
            }

            retired.Clear();
            loaded.Clear();
        }
    }
}
