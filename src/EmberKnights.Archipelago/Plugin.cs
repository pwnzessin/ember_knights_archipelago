using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace EmberKnights.Archipelago;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ArchipelagoPlugin : BasePlugin
{
    public const string PluginGuid = "com.emberknights.archipelago";
    public const string PluginName = "Ember Knights Archipelago";
    public const string PluginVersion = "1.5.0";

    public static ArchipelagoPlugin Instance { get; private set; } = null!;
    public static ManualLogSource LogSource => Instance.Log;
    public ConnectionService Service { get; private set; } = null!;

    public ConfigEntry<string> Host { get; private set; } = null!;
    public ConfigEntry<int> Port { get; private set; } = null!;
    public ConfigEntry<string> SlotName { get; private set; } = null!;
    public ConfigEntry<string> Password { get; private set; } = null!;
    public ConfigEntry<bool> AutoConnect { get; private set; } = null!;
    public ConfigEntry<bool> DeathLink { get; private set; } = null!;
    public ConfigEntry<int> StartingArchipelagoCoins { get; private set; } = null!;
    public ConfigEntry<int> ExtraStartingWeapons { get; private set; } = null!;
    public ConfigEntry<string> HeatMode { get; private set; } = null!;
    public ConfigEntry<string> HeatReroll { get; private set; } = null!;
    public ConfigEntry<bool> RelicDiagnosticsEnabled { get; private set; } = null!;
    public ConfigEntry<bool> PraxisDiagnosticsEnabled { get; private set; } = null!;
    public ConfigEntry<bool> CoinIconEnabled { get; private set; } = null!;
    public ConfigEntry<bool> HudLogWindowEnabled { get; private set; } = null!;
    public ConfigEntry<float> HudLogScale { get; private set; } = null!;
    public ConfigEntry<float> HudLogHoldSeconds { get; private set; } = null!;
    public ConfigEntry<float> HudLogIdleOpacity { get; private set; } = null!;

    private Harmony? harmony;

    public override void Load()
    {
        Instance = this;
        Host = Config.Bind("Connection", "host", "archipelago.gg", "Archipelago server hostname or IP address.");
        Port = Config.Bind("Connection", "port", 38281, "Archipelago server port.");
        SlotName = Config.Bind("Connection", "slot_name", "Player1", "Archipelago slot name from the generated YAML.");
        Password = Config.Bind("Connection", "password", "", "Archipelago room password, if required.");
        AutoConnect = Config.Bind("Connection", "auto_connect", false, "Connect automatically shortly after the game starts.");
        DeathLink = Config.Bind("Gameplay", "death_link", false, "Fallback DeathLink setting when slot data does not specify it.");
        StartingArchipelagoCoins = Config.Bind(
            "Gameplay",
            "starting_archipelago_coins",
            1,
            "Fallback starting Archipelago Coins (minimum 1) when slot data does not specify it.");
        ExtraStartingWeapons = Config.Bind(
            "Gameplay",
            "extra_starting_weapons",
            1,
            "Fallback number of random extra starting weapons (0..6) when slot data does not specify starting_weapon_ids.");
        HeatMode = Config.Bind(
            "Gameplay",
            "heat_mode",
            "random_assortment",
            "Fallback heat mode ('random_assortment', 'curated_heat_difficulty', or 'sequential') when slot data does not specify it.");
        HeatReroll = Config.Bind(
            "Gameplay",
            "heat_reroll",
            "every_run",
            "Fallback for when the random heat assortment is rolled ('every_run' or 'once') when slot data does not specify it.");
        RelicDiagnosticsEnabled = Config.Bind(
            "Diagnostics",
            "relic_diagnostics",
            true,
            "Log how relic orb pickups and Esper's identification turn-in change the save's relic lists ([RelicDiag] lines in LogOutput.log).");
        CoinIconEnabled = Config.Bind(
            "Visuals",
            "archipelago_coin_icon",
            true,
            "Replace the Ember icon with the Archipelago coin while connected. The original icon returns on disconnect.");
        HudLogWindowEnabled = Config.Bind(
            "Visuals",
            "hud_log_window",
            false,
            "Show the Archipelago log in a window drawn on the game's own HUD instead of the plain overlay box. " +
            "Experimental. It is drawn locally on each player's screen only and sends nothing over the game's network.");
        HudLogScale = Config.Bind(
            "Visuals",
            "hud_log_scale",
            0.7f,
            new ConfigDescription(
                "Size of the HUD log window. 1.0 is the original size, smaller values shrink it.",
                new AcceptableValueRange<float>(0.4f, 1.5f)));
        HudLogHoldSeconds = Config.Bind(
            "Visuals",
            "hud_log_hold_seconds",
            6f,
            new ConfigDescription(
                "How long the HUD log window stays in focus after a new message before it fades away.",
                new AcceptableValueRange<float>(1f, 60f)));
        HudLogIdleOpacity = Config.Bind(
            "Visuals",
            "hud_log_idle_opacity",
            0f,
            new ConfigDescription(
                "How visible the HUD log window stays when there is no new message. 0 hides it completely, " +
                "0.3 leaves it faintly visible.",
                new AcceptableValueRange<float>(0f, 1f)));
        PraxisDiagnosticsEnabled = Config.Bind(
            "Diagnostics",
            "praxis_diagnostics",
            true,
            "Log what the game does after a Praxis Arena Trap fires ([PraxisDiag] lines in LogOutput.log).");

        _ = GameCatalog.Data;
        Service = new ConnectionService(this);
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(ArchipelagoPlugin).Assembly);
        AddComponent<ArchipelagoBehaviour>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. Press F6 to connect.");
    }

    public override bool Unload()
    {
        Service?.Dispose();
        harmony?.UnpatchSelf();
        return true;
    }
}
