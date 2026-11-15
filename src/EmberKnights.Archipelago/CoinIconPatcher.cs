using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace EmberKnights.Archipelago;

/// <summary>
/// Replaces the Ember currency icon with the Archipelago coin while the multiworld is active.
///
/// The Ember Tree prices its nodes in text, and the icon inside that text is the TextMeshPro inline sprite
/// "Ember" from the shared sprite sheet. A TMP glyph is just a rectangle on that sheet plus layout metrics, so the
/// coin is written into an empty area of the sheet at runtime and the glyph is pointed at it. Nothing on disk changes.
///
/// Every step is verified before it changes anything and every failure is logged and leaves the game untouched.
/// </summary>
internal static class CoinIconPatcher
{
    private const string GlyphName = "Ember";
    private const string ResourceName = "EmberKnights.Archipelago.archipelago_coin_32.png";
    private const int CoinSize = 32;
    // Where the coin goes on the sheet, in texture pixels measured from the bottom left (Unity convention).
    private const int TargetX = 0;
    private const int TargetY = 0;

    private static TMP_SpriteAsset? patchedAsset;
    private static GlyphRect originalRect;
    private static GlyphMetrics originalMetrics;
    private static float originalScale;
    private static bool inspected;
    private static bool applied;

    public static bool IsApplied => applied;

    /// <summary>Looks for the sprite asset and, when everything checks out, swaps the icon. Safe to call repeatedly.</summary>
    public static void TryApply()
    {
        if (applied)
            return;
        try
        {
            if (!TryFindEmberGlyph(out var asset, out var character, out var glyph))
                return;
            var sheet = asset.spriteSheet?.TryCast<Texture2D>();
            LogInspection(asset, character, glyph, sheet);
            if (!CanPatch(sheet, glyph, out var reason))
            {
                if (!inspected)
                    ArchipelagoPlugin.LogSource.LogWarning($"Archipelago coin icon not applied: {reason}");
                inspected = true;
                return;
            }
            inspected = true;
            Apply(asset, glyph, sheet!);
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Archipelago coin icon failed and was left unchanged: {exception.Message}");
            inspected = true;
        }
    }

    /// <summary>Puts the original Ember icon back (disconnect), so vanilla play is never affected.</summary>
    public static void Restore()
    {
        if (!applied || patchedAsset is null)
            return;
        try
        {
            var glyph = FindGlyph(patchedAsset, out _);
            if (glyph is not null)
            {
                glyph.glyphRect = originalRect;
                glyph.metrics = originalMetrics;
                glyph.scale = originalScale;
                patchedAsset.UpdateLookupTables();
            }
            applied = false;
            ArchipelagoPlugin.LogSource.LogInfo("Original Ember icon restored.");
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not restore the Ember icon: {exception.Message}");
        }
    }

    private static bool TryFindEmberGlyph(out TMP_SpriteAsset asset, out TMP_SpriteCharacter character, out TMP_SpriteGlyph glyph)
    {
        asset = null!;
        character = null!;
        glyph = null!;
        var candidates = Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>();
        if (candidates is null)
            return false;
        foreach (var candidate in candidates)
        {
            if (candidate is null)
                continue;
            var found = FindGlyph(candidate, out var foundCharacter);
            if (found is null || foundCharacter is null)
                continue;
            asset = candidate;
            character = foundCharacter;
            glyph = found;
            return true;
        }
        return false;
    }

    private static TMP_SpriteGlyph? FindGlyph(TMP_SpriteAsset asset, out TMP_SpriteCharacter? character)
    {
        character = null;
        var characters = asset.spriteCharacterTable;
        var glyphs = asset.spriteGlyphTable;
        if (characters is null || glyphs is null)
            return null;
        for (var i = 0; i < characters.Count; i++)
        {
            var entry = characters[i];
            if (entry is null || !string.Equals(entry.name, GlyphName, StringComparison.Ordinal))
                continue;
            character = entry;
            var index = (int)entry.glyphIndex;
            for (var g = 0; g < glyphs.Count; g++)
            {
                var glyph = glyphs[g];
                if (glyph is not null && glyph.index == index)
                    return glyph;
            }
        }
        return null;
    }

    private static void LogInspection(TMP_SpriteAsset asset, TMP_SpriteCharacter character, TMP_SpriteGlyph glyph, Texture2D? sheet)
    {
        if (inspected)
            return;
        var rect = glyph.glyphRect;
        var metrics = glyph.metrics;
        ArchipelagoPlugin.LogSource.LogInfo(
            $"[CoinIcon] sprite asset '{asset.name}' glyph '{character.name}' index {glyph.index}: " +
            $"rect=({rect.x},{rect.y},{rect.width}x{rect.height}) " +
            $"metrics=(w {metrics.width}, h {metrics.height}, bearingX {metrics.horizontalBearingX}, " +
            $"bearingY {metrics.horizontalBearingY}, advance {metrics.horizontalAdvance}) scale={glyph.scale} atlas={glyph.atlasIndex}");
        if (sheet is null)
        {
            ArchipelagoPlugin.LogSource.LogInfo("[CoinIcon] the sprite sheet is not a Texture2D.");
            return;
        }
        ArchipelagoPlugin.LogSource.LogInfo(
            $"[CoinIcon] sprite sheet '{sheet.name}' {sheet.width}x{sheet.height} format={sheet.format} readable={sheet.isReadable}");
    }

    private static bool CanPatch(Texture2D? sheet, TMP_SpriteGlyph glyph, out string reason)
    {
        reason = "";
        if (sheet is null)
        {
            reason = "the sprite sheet is not a Texture2D";
            return false;
        }
        if (!sheet.isReadable)
        {
            reason = $"the sprite sheet is not readable (format {sheet.format})";
            return false;
        }
        if (sheet.format != TextureFormat.RGBA32 && sheet.format != TextureFormat.ARGB32 && sheet.format != TextureFormat.BGRA32)
        {
            reason = $"unsupported sprite sheet format {sheet.format}";
            return false;
        }
        if (TargetX + CoinSize > sheet.width || TargetY + CoinSize > sheet.height)
        {
            reason = $"the {CoinSize}x{CoinSize} target does not fit a {sheet.width}x{sheet.height} sheet";
            return false;
        }
        var area = sheet.GetPixels32();
        for (var y = TargetY; y < TargetY + CoinSize; y++)
        {
            for (var x = TargetX; x < TargetX + CoinSize; x++)
            {
                if (area[y * sheet.width + x].a != 0)
                {
                    reason = $"the target area is not empty at ({x},{y})";
                    return false;
                }
            }
        }
        return true;
    }

    private static void Apply(TMP_SpriteAsset asset, TMP_SpriteGlyph glyph, Texture2D sheet)
    {
        var pixels = LoadCoinPixels();
        if (pixels is null)
            return;

        originalRect = glyph.glyphRect;
        originalMetrics = glyph.metrics;
        originalScale = glyph.scale;

        sheet.SetPixels32(TargetX, TargetY, CoinSize, CoinSize, pixels);
        sheet.Apply(false);

        // Keep the on-screen height of the Ember icon and let the width follow the square coin, so the text beside it
        // moves only slightly.
        var height = originalMetrics.height;
        var width = height;
        glyph.glyphRect = new GlyphRect(TargetX, TargetY, CoinSize, CoinSize);
        glyph.metrics = new GlyphMetrics(
            width,
            height,
            originalMetrics.horizontalBearingX,
            originalMetrics.horizontalBearingY,
            width + (originalMetrics.horizontalAdvance - originalMetrics.width));
        asset.UpdateLookupTables();

        patchedAsset = asset;
        applied = true;
        ArchipelagoPlugin.LogSource.LogInfo(
            $"[CoinIcon] Ember icon replaced by the Archipelago coin (glyph {width}x{height}, sheet area {TargetX},{TargetY} {CoinSize}x{CoinSize}).");
    }

    private static Color32[]? LoadCoinPixels()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            ArchipelagoPlugin.LogSource.LogWarning("[CoinIcon] the embedded coin image is missing.");
            return null;
        }
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(image, (Il2CppStructArray<byte>)memory.ToArray()) ||
            image.width != CoinSize || image.height != CoinSize)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"[CoinIcon] the coin image is not {CoinSize}x{CoinSize}.");
            return null;
        }
        var source = image.GetPixels32();
        var result = new Color32[source.Length];
        for (var i = 0; i < source.Length; i++)
            result[i] = source[i];
        return result;
    }
}
