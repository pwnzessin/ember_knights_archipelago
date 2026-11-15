using System;
using UnityEngine;

namespace EmberKnights.Archipelago;

public sealed class ArchipelagoBehaviour : MonoBehaviour
{
    private float startedAt;
    private bool autoConnectAttempted;
    private HudLogWindow? hudWindow;
    private bool sinkAttached;

    public ArchipelagoBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Start()
    {
        startedAt = Time.realtimeSinceStartup;
    }

    private void Update()
    {
        var plugin = ArchipelagoPlugin.Instance;
        if (plugin?.Service is null)
            return;

        plugin.Service.PumpMainThread();
        TickHudWindow();
        if (Input.GetKeyDown(KeyCode.F6))
            plugin.Service.Connect();
        if (Input.GetKeyDown(KeyCode.F7))
            plugin.Service.Disconnect();

        if (plugin.AutoConnect.Value && !autoConnectAttempted && Time.realtimeSinceStartup - startedAt >= 5f)
        {
            autoConnectAttempted = true;
            plugin.Service.Connect();
        }
    }

    /// <summary>
    /// Keeps the HUD log window fed and current. The window is only created when the config asks for it, and is
    /// dropped for good if it ever fails, in which case OnGUI keeps drawing the original box.
    /// </summary>
    private void TickHudWindow()
    {
        var plugin = ArchipelagoPlugin.Instance;
        if (plugin?.Service is null)
            return;
        if (!plugin.HudLogWindowEnabled.Value)
        {
            if (hudWindow is not null && sinkAttached)
            {
                plugin.Service.LogSink = null;
                sinkAttached = false;
            }
            hudWindow = null;
            return;
        }

        if (hudWindow is null)
        {
            hudWindow = new HudLogWindow();
            // Start from what the overlay already holds so the window is not empty the first time it opens.
            foreach (var line in plugin.Service.RecentMessages)
                hudWindow.Add(line);
        }

        if (!sinkAttached)
        {
            plugin.Service.LogSink = hudWindow.Add;
            sinkAttached = true;
        }

        hudWindow.Configure(plugin.HudLogScale.Value, plugin.HudLogHoldSeconds.Value, plugin.HudLogIdleOpacity.Value);
        hudWindow.Tick(BuildStatusLine(), visible: true);
    }

    private static string BuildStatusLine()
    {
        var plugin = ArchipelagoPlugin.Instance;
        var service = plugin.Service;
        var statusHex = service.IsConnected ? "#3DD68C" : service.IsActive ? "#F2AB3B" : "#8A93A6";
        var statusWord = service.IsConnected ? "ONLINE" : service.IsActive ? "RECONNECT (F6)" : "OFFLINE (F6)";
        var goalLabel = service.Goal == "arch_praxis" ? "Arch Praxis" : "Praxis";
        var dlBadge = service.DeathLinkEnabled ? "  <color=#E06C75>DL</color>" : "";
        var queueBadge = service.PendingCheckCount > 0 ? $"  <color=#F2AB3B>Q:{service.PendingCheckCount}</color>" : "";
        return $"<b>AP</b> {plugin.SlotName.Value}  <color={statusHex}><b>{statusWord}</b></color>   " +
               $"Heat <b>{service.ActiveHeat}</b>/{service.StartHeat}  Coins <b>{service.AvailableArchipelagoCoins}</b>/{service.ArchipelagoCoinsReceived}  {goalLabel}{dlBadge}{queueBadge}";
    }

    private void OnGUI()
    {
        var plugin = ArchipelagoPlugin.Instance;
        if (plugin?.Service is null)
            return;
        // The HUD window replaces this box while it is up. If it fails or cannot be built, this box stays.
        if (hudWindow is { IsShowing: true })
            return;
        var service = plugin.Service;

        var messages = service.RecentMessages;
        var messageCount = Math.Max(1, messages.Count);
        const float width = 336f;
        var height = 54f + messageCount * 18f;
        const float margin = 14f;
        var x = Screen.width - width - margin;
        var y = margin;

        var previousColor = GUI.color;
        var previousBackground = GUI.backgroundColor;

        // Dark translucent card background using native GUI.Box with an empty title.
        GUI.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 0.88f);
        GUI.color = new Color(1f, 1f, 1f, 0.92f);
        GUI.Box(new Rect(x, y, width, height), "");

        var statusHex = service.IsConnected ? "#3DD68C" : service.IsActive ? "#F2AB3B" : "#8A93A6";
        var statusWord = service.IsConnected ? "● ONLINE" : service.IsActive ? "● RECONNECT (F6)" : "○ OFFLINE (F6)";
        var slot = plugin.SlotName.Value;
        var goalLabel = service.Goal == "arch_praxis" ? "Arch Praxis" : "Praxis";
        var dlBadge = service.DeathLinkEnabled ? "  ·  <color=#E06C75>DL</color>" : "";
        var queueBadge = service.PendingCheckCount > 0 ? $"  ·  <color=#F2AB3B>Q:{service.PendingCheckCount}</color>" : "";

        GUI.color = new Color(0.93f, 0.95f, 0.98f, 0.96f);
        GUI.Label(
            new Rect(x + 10f, y + 6f, width - 20f, 20f),
            $"<b>AP</b> · {slot}   <color={statusHex}><b>{statusWord}</b></color>   <color=#6E7687>[F6/F7]</color>");

        GUI.Label(
            new Rect(x + 10f, y + 26f, width - 20f, 20f),
            $"Heat <b>{service.ActiveHeat}</b>/{service.StartHeat}  ·  Coins <b>{service.AvailableArchipelagoCoins}</b>/{service.ArchipelagoCoinsReceived}  ·  {goalLabel}{dlBadge}{queueBadge}");

        for (var i = 0; i < messages.Count; i++)
        {
            var isNewest = i == messages.Count - 1;
            var line = messages[i] ?? "";
            if (line.Length > 52)
                line = line[..49] + "...";
            GUI.color = isNewest
                ? new Color(0.88f, 0.91f, 0.96f, 0.96f)
                : new Color(0.62f, 0.67f, 0.76f, 0.82f);
            GUI.Label(new Rect(x + 10f, y + 48f + i * 18f, width - 20f, 18f), line);
        }

        GUI.backgroundColor = previousBackground;
        GUI.color = previousColor;
    }
}
