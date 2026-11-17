using TMPro;
using UnityEngine;

namespace EmberKnights.Archipelago;

/// <summary>
/// A small line of text on the game's title screen (the main menu) that says the Archipelago mod is loaded and where the
/// connection stands. It is drawn on the main menu's own canvas in the game's own font, so it looks like part of the game.
///
/// Everything here is local to this game process: it reads the connection state and draws on this player's screen, and
/// sends nothing over the game's network. What the canvas does with a new child can only be known by running the game, so
/// the label logs where it attached and measures itself, and any failure turns it off for the session instead of touching
/// the menu any further.
/// </summary>
internal sealed class TitleScreenLabel
{
    private const string RootName = "ArchipelagoTitleLabel";
    private const float FontSize = 22f;
    private const float Margin = 24f;
    private const float Width = 900f;
    private const float Height = 36f;

    private GameObject? root;
    private TextMeshProUGUI? text;
    private string rendered = "";
    private bool failed;
    private bool loggedTarget;
    private bool loggedShown;
    private float nextBuildAttemptAt;
    private int failedBuilds;

    /// <summary>True when the label gave up for this session; the menu is then left exactly as the game draws it.</summary>
    public bool HasFailed => failed;

    /// <summary>
    /// Called every frame. Shows the label while the main menu is open and hides it everywhere else (in a run, on the
    /// loading screen, while a save is being chosen is still part of the main menu and keeps it).
    /// </summary>
    public void Tick(string line, bool menuVisible)
    {
        if (failed)
            return;
        try
        {
            // Unity destroys canvas objects when the game rebuilds its UI; notice that and rebuild instead of touching them.
            if (root is not null && root == null)
                Release();

            if (!menuVisible)
            {
                if (root != null && root.activeSelf)
                    root.SetActive(false);
                return;
            }

            if (root is null)
            {
                if (Time.realtimeSinceStartup < nextBuildAttemptAt)
                    return;
                nextBuildAttemptAt = Time.realtimeSinceStartup + 1f;
                Build();
                if (root is null)
                    return;
            }

            if (!root!.activeSelf)
                root.SetActive(true);

            if (!string.Equals(line, rendered, StringComparison.Ordinal))
            {
                rendered = line;
                text!.text = line;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>Whether the game's main menu is currently up, so the label should show.</summary>
    public static bool IsMainMenuVisible()
    {
        try
        {
            var state = GameCode.UI?.GetElementState(eUIElement.MainMenu);
            return state is eUIElementState.Opened or eUIElementState.Opening;
        }
        catch (Exception)
        {
            // The UI manager does not exist yet while the game boots. That simply means "not visible".
            return false;
        }
    }

    private void Build()
    {
        var menu = GameCode.UI?.GetElementByType(eUIElement.MainMenu);
        var parent = menu?.RT;
        if (menu is null || parent is null)
            return; // The menu is not loaded yet. Try again shortly.

        var canvas = menu.Canvas ?? parent.GetComponentInParent<Canvas>();
        if (!loggedTarget)
        {
            loggedTarget = true;
            ArchipelagoPlugin.LogSource.LogInfo(
                $"[TitleLabel] Attaching to '{parent.name}' (rect={parent.rect}, lossyScale={parent.lossyScale}) under " +
                $"canvas '{canvas?.name ?? "none"}' (renderMode {(canvas is null ? "n/a" : canvas.renderMode.ToString())}, " +
                $"sortingOrder {(canvas is null ? 0 : canvas.sortingOrder)}), screen {Screen.width}x{Screen.height}.");
        }

        var font = FindFont();
        if (font is null)
        {
            // Retry a few times in case the menu's labels are not ready, then give up quietly.
            if (++failedBuilds >= 5)
                Fail(new InvalidOperationException("no TextMeshPro font asset was found"));
            return;
        }

        root = new GameObject(RootName);
        var rect = root.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        // Bottom left, clear of the Play/Options menu and the links in the bottom right corner.
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(Margin, Margin);
        rect.sizeDelta = new Vector2(Width, Height);

        text = root.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = FontSize;
        text.richText = true;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.raycastTarget = false;
        text.color = new Color(0.93f, 0.95f, 0.98f, 0.92f);
        // Match the game's own labels. The shared material can make the text look outlined in the game's style.
        var gameMaterial = FindLabelMaterial();
        if (gameMaterial is not null)
            text.fontSharedMaterial = gameMaterial;

        rendered = "";
        if (!loggedShown)
        {
            loggedShown = true;
            ArchipelagoPlugin.LogSource.LogInfo(
                $"[TitleLabel] Label created on the main menu (font '{font.name}', size {FontSize}).");
        }
    }

    /// <summary>The font the game's own main menu labels use, so the label matches the rest of the title screen.</summary>
    private static TMP_FontAsset? FindFont()
    {
        var refs = GameCode.UI?.GetElementByType(eUIElement.MainMenu)?.RefsValue?.TryCast<UIMainMenuRefs>();
        var fromMenu = refs?.LabelController?.font;
        if (fromMenu is not null)
            return fromMenu;

        var fromVersion = UIRoot2DRefs.Instance?.VersionLabel?.font;
        if (fromVersion is not null)
            return fromVersion;

        var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return all is { Length: > 0 } ? all[0] : null;
    }

    private static Material? FindLabelMaterial()
    {
        try
        {
            var refs = GameCode.UI?.GetElementByType(eUIElement.MainMenu)?.RefsValue?.TryCast<UIMainMenuRefs>();
            return refs?.LabelController?.TMPText?.fontSharedMaterial;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Release()
    {
        root = null;
        text = null;
        rendered = "";
    }

    private void Fail(Exception exception)
    {
        failed = true;
        ArchipelagoPlugin.LogSource.LogWarning(
            $"[TitleLabel] Could not draw the label on the title screen, leaving the menu as it is: {exception.Message}");
        try
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }
        catch (Exception)
        {
            // Nothing more to do; the label is abandoned either way.
        }
        Release();
    }
}
