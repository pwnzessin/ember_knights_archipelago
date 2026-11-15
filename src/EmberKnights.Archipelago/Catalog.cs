using System.Reflection;
using System.Text.Json;

namespace EmberKnights.Archipelago;

public sealed class SkillDefinition
{
    public string Name { get; set; } = "";
    public int AbilityId { get; set; }
    public string LocationName { get; set; } = "";
    public string Region { get; set; } = "";
}

/// <summary>A skill every run has in the base game. It has no tablet and no location; it is only an item.</summary>
public sealed class StandardSkillDefinition
{
    public string Name { get; set; } = "";
    public int AbilityId { get; set; }
}

public sealed class WeaponDefinition
{
    public string Name { get; set; } = "";
    public int WeaponId { get; set; }
}

public sealed class EmberTreeDefinition
{
    public string Name { get; set; } = "";
    public int DefId { get; set; }
    public int Stage { get; set; }
    public int RequiredCoins { get; set; }
}

public sealed class WeaponModDefinition
{
    public string Name { get; set; } = "";
    public int DefId { get; set; }
    public int WeaponType { get; set; }
}

public sealed class RoomMilestoneDefinition
{
    public string World { get; set; } = "";
    public int AreaName { get; set; }
    public int Room { get; set; }
    public string LocationName => $"{World} Room {Room} Reached";
}

public sealed class RelicOrbDefinition
{
    /// <summary>1-based position in the game's relic unlock order; the n-th orb collected.</summary>
    public int Order { get; set; }
    public int DefId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The DLC orbs continue the base numbering, so the first DLC orb is shown as "Relic Orb 54".</summary>
    public int NumberOffset { get; set; }
    public string LocationName => $"Relic Orb {Order + NumberOffset:D2}";
}

public sealed class HeatModifierDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int[] Costs { get; set; } = Array.Empty<int>();
    public string? Unsafe { get; set; }
}

public sealed class HeatCatalogDefinition
{
    public int MaxHeat { get; set; }
    public List<HeatModifierDefinition> Modifiers { get; set; } = new();
}

public sealed class FillerDefinition
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Amount { get; set; }
}

public sealed class EncounterDefinition
{
    public string Name { get; set; } = "";
    public int[] EnemyIds { get; set; } = Array.Empty<int>();
    public string Region { get; set; } = "";
    public bool Variant { get; set; }
    public string? FinalGoal { get; set; }
}

/// <summary>The Wrath of the Architect content, in its own lists so nothing about the base game changes when it is off.</summary>
public sealed class DlcCatalogDefinition
{
    public List<string> Regions { get; set; } = new();
    public List<SkillDefinition> Skills { get; set; } = new();
    public List<WeaponDefinition> Weapons { get; set; } = new();
    public List<EncounterDefinition> Minibosses { get; set; } = new();
    public List<EncounterDefinition> Bosses { get; set; } = new();
    public List<WeaponModDefinition> WeaponMods { get; set; } = new();
    public List<RoomMilestoneDefinition> RoomMilestones { get; set; } = new();
    public List<RelicOrbDefinition> RelicOrbs { get; set; } = new();
}

public sealed class CatalogData
{
    public int IdBase { get; set; }
    public List<SkillDefinition> Skills { get; set; } = new();
    public List<StandardSkillDefinition> StandardSkills { get; set; } = new();
    public List<WeaponDefinition> Weapons { get; set; } = new();
    public List<FillerDefinition> Fillers { get; set; } = new();
    public List<EncounterDefinition> Minibosses { get; set; } = new();
    public List<EncounterDefinition> Bosses { get; set; } = new();
    public List<EmberTreeDefinition> EmberTree { get; set; } = new();
    public List<WeaponModDefinition> WeaponMods { get; set; } = new();
    public List<RoomMilestoneDefinition> RoomMilestones { get; set; } = new();
    public List<RelicOrbDefinition> RelicOrbs { get; set; } = new();
    public HeatCatalogDefinition Heat { get; set; } = new();
    public DlcCatalogDefinition Dlc { get; set; } = new();

    public const string HeatReductionItemName = "Heat Reduction";

    private readonly Dictionary<long, string> locationNames = new();
    private readonly Dictionary<long, string> itemNames = new();
    private readonly Dictionary<int, int> emberTreeIndexByDefId = new();
    private readonly Dictionary<int, int> weaponModIndexByDefId = new();
    private readonly Dictionary<int, int> relicIndexByDefId = new();
    private readonly Dictionary<int, int> dlcRelicIndexByDefId = new();
    private readonly Dictionary<int, int> dlcWeaponModIndexByDefId = new();

    public long SkillItemId(int index) => IdBase + 1 + index;
    public long SkillLocationId(int index) => IdBase + 1000 + index;
    public long StandardSkillItemId(int index) => IdBase + 20 + index;
    public long WeaponItemId(int index) => IdBase + 100 + index;
    public long FillerItemId(int index) => IdBase + 200 + index;
    public long MinibossLocationId(int index) => IdBase + 2000 + index;
    public long BossLocationId(int index) => IdBase + 3000 + index;
    public long EmberTreeLocationId(int index) => IdBase + 4000 + index;
    public long WeaponModLocationId(int index) => IdBase + 5000 + index;
    public long RoomMilestoneLocationId(int index) => IdBase + 6000 + index;
    public long RelicOrbLocationId(int index) => IdBase + 7000 + index;
    public long RelicItemId(int index) => IdBase + 300 + index;
    public long HeatReductionItemId => IdBase + 400;

    public long DlcSkillItemId(int index) => IdBase + 500 + index;
    public long DlcWeaponItemId(int index) => IdBase + 520 + index;
    public long DlcRelicItemId(int index) => IdBase + 540 + index;
    public long DlcSkillLocationId(int index) => IdBase + 8000 + index;
    public long DlcMinibossLocationId(int index) => IdBase + 8100 + index;
    public long DlcBossLocationId(int index) => IdBase + 8200 + index;
    public long DlcWeaponModLocationId(int index) => IdBase + 8300 + index;
    public long DlcRoomMilestoneLocationId(int index) => IdBase + 8400 + index;
    public long DlcRelicOrbLocationId(int index) => IdBase + 8500 + index;

    public void Initialize()
    {
        for (var i = 0; i < Skills.Count; i++)
        {
            itemNames[SkillItemId(i)] = Skills[i].Name;
            locationNames[SkillLocationId(i)] = Skills[i].LocationName;
        }
        for (var i = 0; i < StandardSkills.Count; i++)
            itemNames[StandardSkillItemId(i)] = StandardSkills[i].Name;
        for (var i = 0; i < Weapons.Count; i++)
            itemNames[WeaponItemId(i)] = Weapons[i].Name;
        for (var i = 0; i < Fillers.Count; i++)
            itemNames[FillerItemId(i)] = Fillers[i].Name;
        for (var i = 0; i < Minibosses.Count; i++)
            locationNames[MinibossLocationId(i)] = $"{Minibosses[i].Name} Defeated";
        for (var i = 0; i < Bosses.Count; i++)
            locationNames[BossLocationId(i)] = $"{Bosses[i].Name} Defeated";
        for (var i = 0; i < EmberTree.Count; i++)
        {
            locationNames[EmberTreeLocationId(i)] = $"Ember Tree: {EmberTree[i].Name}";
            emberTreeIndexByDefId[EmberTree[i].DefId] = i;
        }
        for (var i = 0; i < WeaponMods.Count; i++)
        {
            locationNames[WeaponModLocationId(i)] = WeaponMods[i].Name;
            weaponModIndexByDefId[WeaponMods[i].DefId] = i;
        }
        for (var i = 0; i < RoomMilestones.Count; i++)
            locationNames[RoomMilestoneLocationId(i)] = RoomMilestones[i].LocationName;
        for (var i = 0; i < RelicOrbs.Count; i++)
        {
            itemNames[RelicItemId(i)] = RelicOrbs[i].Name;
            locationNames[RelicOrbLocationId(i)] = RelicOrbs[i].LocationName;
            relicIndexByDefId[RelicOrbs[i].DefId] = i;
        }
        itemNames[HeatReductionItemId] = HeatReductionItemName;

        // The names of every DLC entry are always known, whether or not a seed uses the DLC, so a received item or a
        // hint for one is never shown as "Item 113523500".
        for (var i = 0; i < Dlc.Skills.Count; i++)
        {
            itemNames[DlcSkillItemId(i)] = Dlc.Skills[i].Name;
            locationNames[DlcSkillLocationId(i)] = Dlc.Skills[i].LocationName;
        }
        for (var i = 0; i < Dlc.Weapons.Count; i++)
            itemNames[DlcWeaponItemId(i)] = Dlc.Weapons[i].Name;
        for (var i = 0; i < Dlc.Minibosses.Count; i++)
            locationNames[DlcMinibossLocationId(i)] = $"{Dlc.Minibosses[i].Name} Defeated";
        for (var i = 0; i < Dlc.Bosses.Count; i++)
            locationNames[DlcBossLocationId(i)] = $"{Dlc.Bosses[i].Name} Defeated";
        for (var i = 0; i < Dlc.WeaponMods.Count; i++)
        {
            locationNames[DlcWeaponModLocationId(i)] = Dlc.WeaponMods[i].Name;
            dlcWeaponModIndexByDefId[Dlc.WeaponMods[i].DefId] = i;
        }
        for (var i = 0; i < Dlc.RoomMilestones.Count; i++)
            locationNames[DlcRoomMilestoneLocationId(i)] = Dlc.RoomMilestones[i].LocationName;
        for (var i = 0; i < Dlc.RelicOrbs.Count; i++)
        {
            Dlc.RelicOrbs[i].NumberOffset = RelicOrbs.Count;
            itemNames[DlcRelicItemId(i)] = Dlc.RelicOrbs[i].Name;
            locationNames[DlcRelicOrbLocationId(i)] = Dlc.RelicOrbs[i].LocationName;
            dlcRelicIndexByDefId[Dlc.RelicOrbs[i].DefId] = i;
        }
    }

    public bool IsHeatReductionItem(long itemId) => itemId == HeatReductionItemId;

    public bool IsOrbRelic(int defId) => relicIndexByDefId.ContainsKey(defId);

    /// <summary>True for one of the 10 DLC orb relics (ids 211 to 220). Base orbs never match.</summary>
    public bool IsDlcOrbRelic(int defId) => dlcRelicIndexByDefId.ContainsKey(defId);

    public RelicOrbDefinition? DlcRelicForItem(long itemId)
    {
        var index = itemId - IdBase - 540;
        return index >= 0 && index < Dlc.RelicOrbs.Count ? Dlc.RelicOrbs[(int)index] : null;
    }

    /// <summary>The location for the n-th DLC orb (1-based), or null when n is outside 1..count.</summary>
    public long? LocationForDlcRelicOrb(int order) =>
        order >= 1 && order <= Dlc.RelicOrbs.Count ? DlcRelicOrbLocationId(order - 1) : null;

    public long? LocationForDlcWeaponMod(int defId) =>
        dlcWeaponModIndexByDefId.TryGetValue(defId, out var index) ? DlcWeaponModLocationId(index) : null;

    public SkillDefinition? DlcSkillForAbility(int abilityId) => Dlc.Skills.FirstOrDefault(skill => skill.AbilityId == abilityId);

    public SkillDefinition? DlcSkillForItem(long itemId)
    {
        var index = itemId - IdBase - 500;
        return index >= 0 && index < Dlc.Skills.Count ? Dlc.Skills[(int)index] : null;
    }

    public WeaponDefinition? DlcWeaponForItem(long itemId)
    {
        var index = itemId - IdBase - 520;
        return index >= 0 && index < Dlc.Weapons.Count ? Dlc.Weapons[(int)index] : null;
    }

    public bool IsDlcWeapon(int weaponId) => Dlc.Weapons.Any(weapon => weapon.WeaponId == weaponId);

    public long LocationForDlcSkill(SkillDefinition skill) => DlcSkillLocationId(Dlc.Skills.IndexOf(skill));
    public long LocationForDlcMiniboss(EncounterDefinition encounter) => DlcMinibossLocationId(Dlc.Minibosses.IndexOf(encounter));
    public long LocationForDlcBoss(EncounterDefinition encounter) => DlcBossLocationId(Dlc.Bosses.IndexOf(encounter));

    public EncounterDefinition? DlcMinibossForEnemy(int enemyId) =>
        Dlc.Minibosses.FirstOrDefault(encounter => encounter.EnemyIds.Contains(enemyId));

    public EncounterDefinition? DlcBossForEnemy(int enemyId) =>
        Dlc.Bosses.FirstOrDefault(encounter => encounter.EnemyIds.Contains(enemyId));

    public IEnumerable<RoomMilestoneDefinition> DlcMilestonesFor(int areaName, int roomsEntered)
    {
        foreach (var milestone in Dlc.RoomMilestones)
        {
            if (milestone.AreaName == areaName && milestone.Room <= roomsEntered)
                yield return milestone;
        }
    }

    public long LocationForDlcRoomMilestone(RoomMilestoneDefinition milestone) =>
        DlcRoomMilestoneLocationId(Dlc.RoomMilestones.IndexOf(milestone));

    public RelicOrbDefinition? RelicForItem(long itemId)
    {
        var index = itemId - IdBase - 300;
        return index >= 0 && index < RelicOrbs.Count ? RelicOrbs[(int)index] : null;
    }

    /// <summary>The location for the n-th relic orb (1-based), or null when n is outside 1..count.</summary>
    public long? LocationForRelicOrb(int order) =>
        order >= 1 && order <= RelicOrbs.Count ? RelicOrbLocationId(order - 1) : null;

    public long? LocationForEmberTreeNode(int defId) =>
        emberTreeIndexByDefId.TryGetValue(defId, out var index) ? EmberTreeLocationId(index) : null;

    public long? LocationForWeaponMod(int defId) =>
        weaponModIndexByDefId.TryGetValue(defId, out var index) ? WeaponModLocationId(index) : null;

    public IEnumerable<RoomMilestoneDefinition> MilestonesFor(int areaName, int roomsEntered)
    {
        foreach (var milestone in RoomMilestones)
        {
            if (milestone.AreaName == areaName && milestone.Room <= roomsEntered)
                yield return milestone;
        }
    }

    public long LocationForRoomMilestone(RoomMilestoneDefinition milestone) =>
        RoomMilestoneLocationId(RoomMilestones.IndexOf(milestone));

    public string GetLocationName(long id) => locationNames.TryGetValue(id, out var name) ? name : $"Location {id}";
    public string GetItemName(long id) => itemNames.TryGetValue(id, out var name) ? name : $"Item {id}";

    public SkillDefinition? SkillForAbility(int abilityId) => Skills.FirstOrDefault(skill => skill.AbilityId == abilityId);
    public SkillDefinition? SkillForItem(long itemId) => SkillAtIndex(itemId, 1, Skills.Count);
    public SkillDefinition? SkillForLocation(long locationId) => SkillAtIndex(locationId, 1000, Skills.Count);

    /// <summary>The standard skill this item grants, or null when the item is not one.</summary>
    public StandardSkillDefinition? StandardSkillForItem(long itemId)
    {
        var index = itemId - IdBase - 20;
        return index >= 0 && index < StandardSkills.Count ? StandardSkills[(int)index] : null;
    }

    public StandardSkillDefinition? StandardSkillForAbility(int abilityId) =>
        StandardSkills.FirstOrDefault(skill => skill.AbilityId == abilityId);
    public WeaponDefinition? WeaponForItem(long itemId)
    {
        var index = itemId - IdBase - 100;
        return index >= 0 && index < Weapons.Count ? Weapons[(int)index] : null;
    }

    public bool IsRandomizedWeapon(int weaponId) => Weapons.Any(weapon => weapon.WeaponId == weaponId);
    public FillerDefinition? FillerForItem(long itemId) => FillerAtIndex(itemId);

    public long LocationForSkill(SkillDefinition skill) => SkillLocationId(Skills.IndexOf(skill));
    public long LocationForMiniboss(EncounterDefinition encounter) => MinibossLocationId(Minibosses.IndexOf(encounter));
    public long LocationForBoss(EncounterDefinition encounter) => BossLocationId(Bosses.IndexOf(encounter));

    public EncounterDefinition? MinibossForEnemy(int enemyId) =>
        Minibosses.FirstOrDefault(encounter => encounter.EnemyIds.Contains(enemyId));

    public EncounterDefinition? BossForEnemy(int enemyId) =>
        Bosses.FirstOrDefault(encounter => encounter.FinalGoal is null && encounter.EnemyIds.Contains(enemyId));

    public EncounterDefinition? FinalBoss(string goal) =>
        Bosses.FirstOrDefault(encounter => encounter.FinalGoal == goal);

    public bool OwnsLocation(IReadOnlyCollection<long> sessionLocations, long locationId) =>
        sessionLocations.Count == 0 || sessionLocations.Contains(locationId);

    private SkillDefinition? SkillAtIndex(long id, int offset, int count)
    {
        var index = id - IdBase - offset;
        return index >= 0 && index < count ? Skills[(int)index] : null;
    }

    private FillerDefinition? FillerAtIndex(long id)
    {
        var index = id - IdBase - 200;
        return index >= 0 && index < Fillers.Count ? Fillers[(int)index] : null;
    }
}

public static class GameCatalog
{
    public static CatalogData Data { get; } = Load();

    private static CatalogData Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("EmberKnights.Archipelago.catalog.json")
            ?? throw new InvalidOperationException("Embedded Archipelago catalog is missing.");
        var catalog = JsonSerializer.Deserialize<CatalogData>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Embedded Archipelago catalog is invalid.");
        catalog.Initialize();
        if (catalog.Skills.Count != 10 || catalog.StandardSkills.Count != 7 || catalog.Weapons.Count != 6 ||
            catalog.Minibosses.Count != 16 || catalog.Bosses.Count != 14 ||
            catalog.EmberTree.Count != 30 || catalog.WeaponMods.Count != 87 || catalog.RoomMilestones.Count != 18 ||
            catalog.RelicOrbs.Count != 53 || catalog.Heat.MaxHeat != 55 || catalog.Heat.Modifiers.Count != 17 ||
            catalog.Fillers.Count != 8)
            throw new InvalidOperationException("Archipelago catalog counts do not match the supported game version.");
        if (catalog.Dlc.Skills.Count != 10 || catalog.Dlc.Weapons.Count != 2 || catalog.Dlc.Minibosses.Count != 4 ||
            catalog.Dlc.Bosses.Count != 6 || catalog.Dlc.WeaponMods.Count != 19 ||
            catalog.Dlc.RoomMilestones.Count != 18 || catalog.Dlc.RelicOrbs.Count != 10 || catalog.Dlc.Regions.Count != 5)
            throw new InvalidOperationException("Archipelago DLC catalog counts do not match the supported game version.");
        return catalog;
    }
}
