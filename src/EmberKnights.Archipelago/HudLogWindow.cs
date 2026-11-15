using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberKnights.Archipelago;

/// <summary>
/// A scrolling Archipelago log drawn on the game's own HUD canvas instead of with Unity's immediate-mode GUI.
/// Everything here is local to this game process: it reads the connection's log and draws on this player's screen,
/// and sends nothing over the game's network, so it cannot affect co-op.
///
/// The HUD canvas is only knowable at runtime, so the window builds itself lazily once the game's HUD exists and
/// logs what it found. Any failure disables it and leaves the plugin's original overlay in charge.
/// </summary>
internal sealed class HudLogWindow
{
    private const string RootName = "ArchipelagoLogWindow";
    private const int Capacity = 60;
    private const float Width = 460f;
    private const float RowHeight = 17f;
    private const int Rows = 9;
    /// <summary>Where message text starts, in em, so wrapped rows hang under the text instead of under the time.</summary>
    private const float TextStartEm = 3.4f;
    private const float LogFontSize = 12f;
    /// <summary>Average character width as a fraction of the font size. The game's HUD font is proportional.</summary>
    private const float AverageCharEm = 0.52f;
    private const float Margin = 12f;
    private const float FadeInSeconds = 0.25f;
    private const float FadeOutSeconds = 1.2f;

    private readonly LogBuffer buffer = new(Capacity);
    private FocusFade fade = new(6f, FadeInSeconds, FadeOutSeconds);
    private float appliedHold = 6f;
    private float appliedIdle;
    private float appliedScale = -1f;
    private CanvasGroup? group;
    private GameObject? root;
    private TextMeshProUGUI? statusText;
    private TextMeshProUGUI? logText;
    private int renderedVersion = -1;
    private string renderedStatus = "";
    private bool failed;
    private bool loggedTarget;
    private bool onScreenConfirmed;
    private int geometryReports;
    private float nextGeometryReportAt;
    private float nextBuildAttemptAt;

    /// <summary>
    /// True only once the window exists AND its rectangle was measured to lie on screen. Existing is not enough: a
    /// window parented under a scaled or moved HUD object can exist and still be off screen, and the original overlay
    /// must keep drawing until the new one is known to be visible.
    /// </summary>
    public bool IsShowing => root is not null && !failed && onScreenConfirmed;

    /// <summary>True when the window gave up; the original overlay should be used instead.</summary>
    public bool HasFailed => failed;

    public void Add(string? line)
    {
        if (buffer.Add(line))
            fade.Poke(Time.realtimeSinceStartup);
    }

    /// <summary>Applies the user's config. Cheap to call every frame; it only acts when a value changed.</summary>
    public void Configure(float scale, float holdSeconds, float idleOpacity)
    {
        if (!Mathf.Approximately(holdSeconds, appliedHold) || !Mathf.Approximately(idleOpacity, appliedIdle))
        {
            appliedHold = holdSeconds;
            appliedIdle = idleOpacity;
            // A new fade object forgets the previous timing, so start it where the old one was.
            var previous = fade.Alpha;
            fade = new FocusFade(holdSeconds, FadeInSeconds, FadeOutSeconds, idleOpacity);
            if (previous > idleOpacity)
                fade.Poke(Time.realtimeSinceStartup);
        }
        if (!Mathf.Approximately(scale, appliedScale))
        {
            appliedScale = scale;
            if (root != null)
                root.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <summary>Called every frame. Builds the window on first use and keeps its text current.</summary>
    public void Tick(string status, bool visible)
    {
        if (failed)
            return;
        try
        {
            if (root is null)
            {
                if (!visible || Time.realtimeSinceStartup < nextBuildAttemptAt)
                    return;
                nextBuildAttemptAt = Time.realtimeSinceStartup + 2f;
                Build();
                if (root is null)
                    return;
            }

            // Unity destroys the canvas objects on scene changes; notice that and rebuild instead of touching them.
            if (root == null)
            {
                Release();
                return;
            }

            root.SetActive(visible);
            if (!visible)
                return;
            var alpha = fade.Tick(Time.realtimeSinceStartup);
            if (group != null)
                group.alpha = alpha;
            ReportGeometry();

            if (!ReferenceEquals(status, renderedStatus) && !string.Equals(status, renderedStatus, StringComparison.Ordinal))
            {
                renderedStatus = status;
                statusText!.text = status;
            }

            if (renderedVersion != buffer.Version)
            {
                renderedVersion = buffer.Version;
                // Characters that fit beside the time column, from the font size rather than a fixed pixel guess.
                var textWidthPx = Width - 16f - TextStartEm * LogFontSize;
                var chars = Math.Max(12, (int)(textWidthPx / (LogFontSize * AverageCharEm)));
                var rows = buffer.VisibleRows(Rows, chars, gapBetweenMessages: true);
                logText!.text = LogFormatter.Build(rows, TextStartEm, gapBetweenMessages: true);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Build()
    {
        var hud = GameCode.Game?.g_UI?.Hud;
        var refs = hud?.Refs;
        // The HUD's own root rectangle, not 'PlayerCanvas': that child holds the per-player HUDs and can be moved or
        // scaled by the game, which would put this window somewhere unexpected or off screen.
        var parent = refs?.RT ?? refs?.PlayerCanvasRT;
        if (parent is null)
            return; // The HUD is not up yet (title screen, loading). Try again shortly.

        var canvas = refs?.Canvas ?? parent.GetComponentInParent<Canvas>();
        if (!loggedTarget)
        {
            loggedTarget = true;
            ArchipelagoPlugin.LogSource.LogInfo(
                $"[HudLog] Attaching to '{parent.name}' (refs.RT={(refs?.RT is null ? "none" : "yes")}, " +
                $"rect={parent.rect}, lossyScale={parent.lossyScale}) under canvas '{canvas?.name ?? "none"}' " +
                $"(renderMode {(canvas is null ? "n/a" : canvas.renderMode.ToString())}, " +
                $"scaleFactor {(canvas is null ? 0f : canvas.scaleFactor)}).");
        }

        var font = FindFont();
        if (font is null)
        {
            Fail(new InvalidOperationException("no TextMeshPro font asset was found"));
            return;
        }

        root = new GameObject(RootName);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = new Vector2(-Margin, -Margin);
        rootRect.sizeDelta = new Vector2(Width, 24f + Rows * RowHeight + 10f);

        var background = root.AddComponent<Image>();
        background.color = new Color(0.05f, 0.06f, 0.09f, 0.72f);
        background.raycastTarget = false;

        statusText = MakeText("Status", rootRect, font, 14f, new Vector2(8f, -4f), new Vector2(Width - 16f, 20f));
        logText = MakeText("Log", rootRect, font, LogFontSize, new Vector2(8f, -26f), new Vector2(Width - 16f, Rows * RowHeight));
        logText.alignment = TextAlignmentOptions.BottomLeft;
        logText.color = Color.white;

        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        var scale = appliedScale > 0f ? appliedScale : 1f;
        rootRect.localScale = new Vector3(scale, scale, 1f);
        fade.Poke(Time.realtimeSinceStartup);

        renderedVersion = -1;
        renderedStatus = "";
        ArchipelagoPlugin.LogSource.LogInfo("[HudLog] Window created on the game HUD.");
    }

    private static TextMeshProUGUI MakeText(
        string name, RectTransform parent, TMP_FontAsset font, float size, Vector2 offset, Vector2 area)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = area;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.richText = true;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        text.color = new Color(0.93f, 0.95f, 0.98f, 0.96f);
        return text;
    }

    private static TMP_FontAsset? FindFont()
    {
        // Prefer the font the game's own HUD labels use, so the window matches the rest of the HUD.
        var label = GameCode.Game?.g_UI?.Hud?.Refs?.EmberLabel;
        var fromHud = label?.font;
        if (fromHud is not null)
            return fromHud;

        var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return all is { Length: > 0 } ? all[0] : null;
    }

    /// <summary>
    /// Logs where the window really ended up, a few times over the first seconds. Where the HUD canvas puts a new
    /// child cannot be read without running the game, so this is how an invisible window gets diagnosed.
    /// </summary>
    private void ReportGeometry()
    {
        if (Time.realtimeSinceStartup < nextGeometryReportAt || root == null)
            return;
        // Measure every few seconds for as long as the window lives, so a window that later moves off screen hands
        // the job back to the original overlay. Only the first few measurements are written to the log.
        nextGeometryReportAt = Time.realtimeSinceStartup + 3f;
        var wasOnScreen = onScreenConfirmed;
        var logThis = geometryReports < 6 || wasOnScreen != onScreenConfirmed;
        geometryReports++;

        var rect = root.GetComponent<RectTransform>();
        var parent = rect.parent?.TryCast<RectTransform>();
        var corners = new Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(corners);
        var canvas = root.GetComponentInParent<Canvas>();
        var group = root.GetComponentInParent<CanvasGroup>();
        onScreenConfirmed = IsInsideScreen(rect, canvas, corners);
        logThis |= wasOnScreen != onScreenConfirmed;
        if (!logThis)
            return;
        ArchipelagoPlugin.LogSource.LogInfo(
            $"[HudLog] geometry #{geometryReports}: activeInHierarchy={root.activeInHierarchy} " +
            $"layer={root.layer} lossyScale={rect.lossyScale} localScale={rect.localScale} " +
            $"parentRect={(parent is null ? "none" : parent.rect.ToString())} parentScale={(parent is null ? "n/a" : parent.lossyScale.ToString())} " +
            $"corners=[{corners[0]} {corners[1]} {corners[2]} {corners[3]}] " +
            $"canvasEnabled={(canvas is null ? "n/a" : canvas.enabled.ToString())} sortingOrder={(canvas is null ? 0 : canvas.sortingOrder)} " +
            $"canvasAlpha={(group is null ? "none" : group.alpha.ToString())} screen={Screen.width}x{Screen.height} onScreen={onScreenConfirmed} " +
            $"statusText='{statusText?.text}' enabled={(statusText is null ? "n/a" : statusText.enabled.ToString())} " +
            $"logRows={logText?.text?.Split('\n').Length ?? 0}.");
    }

    /// <summary>
    /// True when the window's four world corners project into the visible screen. Uses the canvas camera for a
    /// screen-space-camera canvas and the raw positions for an overlay canvas.
    /// </summary>
    private static bool IsInsideScreen(RectTransform rect, Canvas? canvas, Il2CppStructArray<Vector3> corners)
    {
        var camera = canvas is { renderMode: RenderMode.ScreenSpaceOverlay } ? null : canvas?.worldCamera;
        var visibleCorners = 0;
        for (var i = 0; i < 4; i++)
        {
            var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            if (screenPoint.x >= 0f && screenPoint.x <= Screen.width && screenPoint.y >= 0f && screenPoint.y <= Screen.height)
                visibleCorners++;
        }
        // Require the whole window on screen, otherwise part of the log could be cut off without anyone noticing.
        return visibleCorners == 4 && rect.rect.width > 1f && rect.rect.height > 1f;
    }

    private void Release()
    {
        onScreenConfirmed = false;
        geometryReports = 0;
        root = null;
        statusText = null;
        logText = null;
        group = null;
        renderedVersion = -1;
        renderedStatus = "";
    }

    private void Fail(Exception exception)
    {
        failed = true;
        ArchipelagoPlugin.LogSource.LogWarning(
            $"[HudLog] Could not draw the log window on the game HUD, falling back to the original overlay: {exception.Message}");
        try
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }
        catch (Exception)
        {
            // Nothing more to do; the window is abandoned either way.
        }
        Release();
    }
}
