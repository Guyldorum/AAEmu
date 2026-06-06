using AAEmu.Commons.Network;
// ReSharper disable ClassNeverInstantiated.Global

namespace AAEmu.Game.Models.Game;

public class Configurations : PacketMarshaler
{
    public string Key { get; set; }
    public string Value { get; set; }
}

public class WorldConfig
{
    public enum WindModelType
    {
        /// <summary>Retail-like: wind only along N-S axis. 15 angle bonus for wind in the direction of the ship.</summary>
        Official,
        /// <summary>More realistic: wind direction rotates smoothly over the day.</summary>
        Realistic
    }

    /// <summary>
    /// Message of the Day that gets displayed in player's chat upon login
    /// </summary>
    public string MOTD { get; set; } = "";

    /// <summary>
    /// Message shown to the player when they exit the game
    /// </summary>
    public string LogoutMessage { get; set; } = "";

    /// <summary>
    /// Time in minutes between user data Save events
    /// </summary>
    public double AutoSaveInterval { get; set; } = 5.0;

    /// <summary>
    /// Server-side Exp multiplier (on top of buffs)
    /// </summary>
    public double ExpRate { get; set; } = 1.0;

    /// <summary>
    /// Server-side Honor Points multiplier (on top of buffs)
    /// </summary>
    public double HonorRate { get; set; } = 1.0;

    /// <summary>
    /// Server-side Vocation Badge multiplier (on top of buffs)
    /// </summary>
    public double VocationRate { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for the loot dice (some loot types are not affected by this)
    /// </summary>
    public double LootRate { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for gold that is obtained through loot drops
    /// </summary>
    public double GoldLootMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for growth rate of doodads, note that this only affects steps marked as growth and not those with a simple timer.
    /// </summary>
    public double GrowthRate { get; set; } = 1.0;

    /// <summary>
    /// Number of days 1 week worth of tax pays for, set this to 3640 would make 1 tax payment last for about 10 years.
    /// </summary>
    public uint DaysForTaxPayment { get; set; } = 7u;

    /// <summary>
    /// Set a minimum access-level that a character must have to ignore falling damage (for devs)
    /// </summary>
    public int IgnoreFallDamageAccessLevel { get; set; } = 100;

    /// <summary>
    /// When enabled, players take no damage at all
    /// </summary>
    public bool GodMode { get; set; }

    /// <summary>
    /// Enables the loading of NavMesh data for dungeons
    /// </summary>
    public bool GeoDataMode { get; set; }

    /// <summary>
    /// When false, heightmaps get loaded on-demand only. Should increase boot times and lower memory use
    /// </summary>
    // TODO: Also apply this to missionX.bai files
    public bool PreLoadTerrain { get; set; }

    /// <summary>
    /// Enable the loading of level model geometry to have more accurate collision for AI and Skills
    /// </summary>
    public bool LoadBrushModels { get; set; }

    /// <summary>
    /// If not zero, will only load brush models that result in a hitbox size larger than or equal to this size (diagonal)
    /// </summary>
    public float LoadBrushMinimumSize { get; set; } = 0f;

    /// <summary>
    /// Maximum number of instances that can be created (includes system instances)
    /// </summary>
    public uint MaxInstances { get; set; } = 32;

    /// <summary>
    /// Target Ticks per Second to use for Physics threads
    /// </summary>
    public float TargetPhysicsTps { get; set; } = 25f;

    /// <summary>
    /// Server-side Actability Points multiplier (on top of buffs)
    /// </summary>
    /// <summary>
    /// Wind model used by ship physics. Default: <c>Official</c>.
    /// </summary>
    public WindModelType WindModel { get; set; } = WindModelType.Official;

    public double ActabilityRate { get; set; } = 1.0;

    /// <summary>
    /// NPC spawn-Z resolution policy. Controls how the server reconciles the Z value declared in
    /// <c>npc_spawns.json</c> with the heightmap and .bai navmesh data when spawning a non-flying NPC.
    /// See <see cref="SpawnHeightConfig"/> for details.
    /// </summary>
    public SpawnHeightConfig SpawnHeight { get; set; } = new();
}

/// <summary>
/// Tunable thresholds that control the spawn-Z resolution algorithm in
/// <c>NpcSpawnerNpc.SpawnNpc</c>. Defaults are conservative on purpose: they fix the common
/// "NPC levitates above the ground" / "NPC under the floor" cases without re-snapping NPCs that
/// were placed deliberately on structures (interior floors, bridges, dungeon platforms, etc.).
/// </summary>
public class SpawnHeightConfig
{
    /// <summary>
    /// Master switch. When false the new resolution logic is bypassed and the original
    /// <c>abs(spawnZ - geoZ) &lt; 1f</c> behaviour is used. Useful for A/B testing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum 2D (XY) distance, in metres, between a spawn point and a .bai node for that
    /// node's Z to be considered representative of the local floor. Beyond this radius the
    /// .bai value is treated as untrusted and the heightmap is preferred. Default: 6m.
    /// </summary>
    public float MaxBaiDistance2D { get; set; } = 6.0f;

    /// <summary>
    /// Maximum vertical separation, in metres, between a candidate .bai Z and the local
    /// heightmap Z for the .bai value to be considered consistent with the open-air ground.
    /// If exceeded the .bai node is assumed to belong to a different vertical surface
    /// (interior floor, bridge, balcony) and the heightmap is preferred. Default: 3m.
    /// </summary>
    public float MaxBaiVsHeightmapDelta { get; set; } = 3.0f;

    /// <summary>
    /// Maximum vertical correction, in metres, that the resolver is allowed to apply to the
    /// JSON-declared spawn Z. Default: 5m.
    /// </summary>
    public float MaxSnapDistance { get; set; } = 5.0f;

    /// <summary>
    /// Threshold above which a spawn Z that exceeds hmap and bai (when they agree) is treated
    /// as unmeshed structure (pier, dock, low balcony). Default: 1.5m.
    /// </summary>
    public float UnmeshedStructureThreshold { get; set; } = 1.5f;

    /// <summary>
    /// Maximum DOWNWARD snap distance. Asymmetric guard: upward corrections still allowed up
    /// to MaxSnapDistance, but downward gaps > 0.5m are treated as NPC standing on unmeshed
    /// sub-structure - JSON spawn Z kept verbatim. Default: 0.5m.
    /// </summary>
    public float MaxDownwardSnapDistance { get; set; } = 0.5f;

    /// <summary>
    /// (V5) XY radius around spawn within which GetReferenceHeight trusts spawner Z instead
    /// of running fresh terrain lookup. Default: 3.0m. Set to 0 to disable.
    /// </summary>
    public float SpawnHomeXyRadius { get; set; } = 3.0f;

    /// <summary>
    /// (V6) Width of smooth transition band just outside SpawnHomeXyRadius. Eliminates Z
    /// oscillation when XY jitters across radius boundary. Default: 4.0m. 0 = V5 hard cut.
    /// </summary>
    public float SpawnHomeBlendBand { get; set; } = 4.0f;

    /// <summary>
    /// (V6) When true (default), ground Z prefers interpolated heightmap over .bai navmesh
    /// when they agree (within MaxBaiVsHeightmapDelta). Demotes .bai to a structure guard:
    /// trusted only where it DISAGREES with heightmap (bridges, cave floors, dungeon platforms).
    /// </summary>
    public bool PreferHeightmapForGroundZ { get; set; } = true;

    /// <summary>
    /// When true, every non-flying NPC spawn logs (at Debug) the three candidate Z values
    /// and decision. Disable on production - main_world has tens of thousands of spawns.
    /// </summary>
    public bool LogResolution { get; set; } = false;

    /// <summary>
    /// A/B test toggle. When true, restores original 3D-nearest .bai lookup. Diagnostics only.
    /// </summary>
    public bool UseLegacyBai3DLookup { get; set; } = false;

    /// <summary>
    /// Optional, OFF by default. Trusts .bai near JSON spawn Z even if diverging from heightmap.
    /// For spawns deliberately on bridges/interior floors/elevated structures.
    /// </summary>
    public bool TrustBaiNearSpawnZ { get; set; } = false;
}


public class DungeonLoadConfig
{
    public string Name { get; set; } = string.Empty;
    public uint Channel { get; set; } = 0;
    public uint Id { get; set; } = 0;
}

public class DungeonsConfig
{
    /// <summary>
    /// If people are kicked from a dungeon and there are no people left,
    /// should the system automatically remove the dungeon instance (default=yes, retail=no) 
    /// </summary>
    public bool AutoCleanupAfterKick { get; set; } = true;

    /// <summary>
    /// Time in seconds after being removed from a party in a dungeon before you get kicked out
    /// </summary>
    public int AutoTeamDisbandKickTime { get; set; } = 30;

    /// <summary>
    /// List of dungeon instances that should be created by default
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<DungeonLoadConfig> AutoCreate { get; set; } = [];
}

public class AccountDeleteDelayTiming
{
    /// <summary>
    /// Minimum Level this timing applies to
    /// </summary>
    public int Level { get; set; }
    /// <summary>
    /// Delay in minutes that needs to be used if this character is at least this level
    /// </summary>
    public int Delay { get; set; }
}

public class AccountConfig
{
    /// <summary>
    /// Allowed Regex for account names
    /// </summary>
    public string NameRegex { get; set; } = "^[a-zA-Z0-9]{1,18}$";
    /// <summary>
    /// Marks if a deleted character's name can be re-used for a new character
    /// </summary>
    public bool DeleteReleaseName { get; set; } = false;
    // ReSharper disable once CollectionNeverUpdated.Global
    // Populated by JSON reader
    /// <summary>
    /// Delete character settings
    /// </summary>
    public List<AccountDeleteDelayTiming> DeleteTimings { get; set; } = [];
    /// <summary>
    /// Default access-level for new accounts
    /// </summary>
    public int AccessLevelDefault { get; set; } = 0;
    /// <summary>
    /// Access-Level that should be used for the first created account on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstAccount { get; set; } = 100;
    /// <summary>
    /// Access-Level that should be used for the first created character on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstCharacter { get; set; } = 100;
}

public class CurrencyValuesConfig
{
    public int Default { get; set; } = 0;
    public int DailyLogin { get; set; } = 0;
    public int TickMinutes { get; set; } = 5;
    public int TickAmount { get; set; } = 0;
    public int TickAmountPremium { get; set; } = 0;

    public int GetTickAmount(bool isPremium)
    {
        return isPremium ? TickAmountPremium : TickAmount;
    }
}

public class SpecialtyConfig
{
    /// <summary>
    /// Maximum rate for speciality packs
    /// </summary>
    public int MaxSpecialtyRatio { get; set; } = 130;
    /// <summary>
    /// Minimum rate for speciality packs
    /// </summary>
    public int MinSpecialtyRatio { get; set; } = 70;
    /// <summary>
    /// Amount the trade in rate lowers for each traded pack
    /// </summary>
    public double RatioDecreasePerPack { get; set; } = 0.5f;
    /// <summary>
    /// Number of % a trade recovers every X time
    /// </summary>
    public double RatioIncreasePerTick { get; set; } = 5.0;
    /// <summary>
    /// Number of minutes between trade rate updates when selling packs
    /// </summary>
    public double RatioDecreaseTickMinutes { get; set; } = 1f;
    /// <summary>
    /// Time in minutes before a traded pack is no longer counted towards the trade rate calculation
    /// </summary>
    public double RatioRegenTickMinutes { get; set; } = 60f;

    /// <summary>
    /// Time in minutes to delay trade pack reward mail delivery. Default is 8 hours.
    /// </summary>
    /// <remarks>
    /// The default value is 8 hours. This setting controls how long after delivery 
    /// a player must wait before receiving their trade pack reward via mail.
    /// </remarks>
    public double TradePackMailDelayInMinutes { get; set; } = 480f;
}

public class ScriptsConfig
{
    public LoadStrategyType LoadStrategy { get; set; } = LoadStrategyType.Reflection;

    public enum LoadStrategyType
    {
        Compilation,
        Reflection
    }
}
