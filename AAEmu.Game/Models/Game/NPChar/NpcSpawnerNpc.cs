using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;

using NLog;

using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawnerNpc : Spawner<Npc>
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// NpcSpawnerTemplateId
    /// </summary>
    public uint NpcSpawnerTemplateId { get; init; }
    /// <summary>
    /// NpcTemplateId
    /// </summary>
    public uint MemberId { get; set; }
    /// <summary>
    /// MemberType should be "Npc" here
    /// </summary>
    public string MemberType { get; set; }
    /// <summary>
    /// Spawn priority weight
    /// </summary>
    public float Weight { get; init; }

    public NpcSpawnerNpc()
    {
        //
    }

    /// <summary>
    /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
    /// </summary>
    /// <param name="spawnerTemplateId"></param>
    public NpcSpawnerNpc(uint spawnerTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
    }

    public NpcSpawnerNpc(uint spawnerTemplateId, uint npcTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
        MemberId = npcTemplateId;
        MemberType = "Npc";
    }

    /// <summary>
    /// Spawns Npcs from a NpcSpawner
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns>List of newly spawned NPCs</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public List<Npc> Spawn(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        switch (MemberType)
        {
            case "Npc":
                return SpawnNpc(npcSpawner, ownerId);
            case "NpcGroup":
                return SpawnNpcGroup(npcSpawner, ownerId);
            default:
                throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
        }
    }

    /// <summary>
    /// Internal Spawn Npc function for Spawn
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        var npcs = new List<Npc>();
        var npc = NpcManager.Instance.Create(npcSpawner.ParentWorld, 0, MemberId);
        if (npc == null)
        {
            Logger.Warn($"Npc {MemberId}, from spawner Id {npcSpawner.Id} not exist at db. Spawner Position: {npcSpawner.Position}");
            return null;
        }

        npc.ParentWorld = npcSpawner.ParentWorld;
        npc.OwnerId = ownerId;

        npc.RegisterNpcEvents();

        Logger.Trace($"Spawn npc templateId {MemberId} objId {npc.ObjId} from spawnerId {NpcSpawnerTemplateId} at Position: {npcSpawner.Position}");

        if (!npc.CanFly)
        {
            npcSpawner.Position.Z = ResolveSpawnZ(npcSpawner, npc);
        }

        npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);
        if (npc.Transform == null)
        {
            Logger.Error($"Can't spawn npc {MemberId} from spawnerId {NpcSpawnerTemplateId}. Transform is null.");
            return null;
        }

        npc.Transform.InstanceId = npc.Transform.InstanceId;

        if (npc.Ai != null)
        {
            npc.Ai.HomePosition = npc.Transform.World.Position;
            npc.Ai.IdlePosition = npc.Ai.HomePosition;
            npc.Ai.GoToSpawn();
        }

        npc.Spawner = npcSpawner;
        npc.Spawner.RespawnTime = (int)Random.Shared.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
        npc.Spawn();

        var world = WorldManager.Instance.GetWorld(npc.Transform.InstanceId);
        world.Events.OnUnitSpawn(world, new OnUnitSpawnArgs { Npc = npc });
        npc.Simulation = new Simulation(npc);

        if (npc.Ai != null && !string.IsNullOrWhiteSpace(npcSpawner.FollowPath))
        {
            if (!npc.Ai.LoadAiPathPoints(npcSpawner.FollowPath, false))
                Logger.Warn($"Failed to load {npcSpawner.FollowPath} for NPC {npc.TemplateId} ({npc.ObjId})");
        }

        npcs.Add(npc);
        return npcs;
    }


    /// <summary>
    /// Resoudre la position Z d'un spawn de NPC non-volant. Conservatif par
    /// design : garde JSON sauf cas tres clair de bug (NPC dans le sol, ou
    /// snap mineur coherent avec le sol detecte).
    ///
    /// Decisions selon delta = JSON_Z - bestFloor (max raycast physique + hmap) :
    ///   delta &lt; -MaxDownSnap         : snap upward (NPC enferme dans geometrie)
    ///   |delta| &lt;= MaxDownSnap        : snap mineur au sol (coherent)
    ///   MaxDownSnap &lt; delta &lt;= MaxSnap : keep JSON (unmeshed structure probable)
    ///   delta &gt; MaxSnap                : keep JSON + WARN (suspect, manual review)
    ///
    /// Tous les seuils viennent de AppConfiguration.Instance.World.SpawnHeight
    /// (configures par lot-4B1, defauts conservateurs).
    ///
    /// V6.1 HANDOFF inspire mais utilise le raycast physique HM (qui couvre
    /// voxel + brush + heightmap fallback) plutot que .bai (HM a une version
    /// limitee de bai et le code .bai d'AiGeoData est commente out runtime).
    /// </summary>
    private float ResolveSpawnZ(NpcSpawner npcSpawner, Npc npc)
    {
        var jsonZ = npcSpawner.Position.Z;
        var posVec = npcSpawner.Position.AsPositionVector();
        var world = npcSpawner.ParentWorld;

        if (world == null)
            return jsonZ;

        var cfg = AppConfiguration.Instance.World?.SpawnHeight;

        // Master switch : si resolver desactive, fallback HM legacy
        if (cfg != null && !cfg.Enabled)
        {
            var legacyZ = world.GetHeight(posVec);
            return (Math.Abs(jsonZ - legacyZ) < 1f) ? legacyZ : jsonZ;
        }

        var maxSnap = cfg?.MaxSnapDistance ?? 5.0f;
        var maxDownSnap = cfg?.MaxDownwardSnapDistance ?? 0.5f;
        var unmeshedThr = cfg?.UnmeshedStructureThreshold ?? 1.5f;
        var logRes = cfg?.LogResolution ?? false;

        // Candidats
        float raycastZ;
        try { raycastZ = world.GetHeight(posVec); }
        catch { raycastZ = float.NaN; }

        var hmapZ = float.NaN;
        try
        {
            var h = world.Template?.GetHeight(posVec.X, posVec.Y);
            if (h.HasValue && !float.IsNaN(h.Value) && !float.IsInfinity(h.Value))
                hmapZ = h.Value;
        }
        catch { /* ignored */ }

        var hasRaycast = !float.IsNaN(raycastZ) && !float.IsInfinity(raycastZ);
        var hasHmap = !float.IsNaN(hmapZ);

        if (!hasRaycast && !hasHmap)
        {
            // Aucune info -> keep JSON
            if (logRes)
                Logger.Trace($"[ResolveSpawnZ] {MemberId} no candidates, keep JSON Z={jsonZ:F2}");
            return jsonZ;
        }

        // bestFloor = la plus haute surface detectee parmi les candidats valides
        var bestFloor = float.NegativeInfinity;
        if (hasRaycast) bestFloor = raycastZ;
        if (hasHmap && hmapZ > bestFloor) bestFloor = hmapZ;

        var delta = jsonZ - bestFloor;

        // Cas 1a : JSON sous le sol moderement (-MaxSnap < delta < -MaxDownSnap)
        // NPC enferme dans la geometrie -> snap upward, log Warn visible.
        if (delta < -maxDownSnap && delta >= -maxSnap)
        {
            Logger.Warn($"[ResolveSpawnZ] snap upward npc={MemberId}@spawner{NpcSpawnerTemplateId} " +
                        $"pos=({posVec.X:F1},{posVec.Y:F1},{jsonZ:F2}) " +
                        $"delta={delta:F2}m, snap {jsonZ:F2} -> {bestFloor:F2} " +
                        $"(NPC sous le sol detecte)");
            return bestFloor;
        }

        // Cas 1b : JSON tres sous le sol (delta < -MaxSnap) -> SUSPECT grotte/cave
        // Probable : NPC dans grotte ou cave profonde. Le raycast est bloque par
        // le TOIT de la grotte au-dessus, pas le vrai sol. Snapper enverrait le
        // NPC en surface (= disparition). Symetrique au cas 4 (suspect surface).
        if (delta < -maxSnap)
        {
            Logger.Warn($"[ResolveSpawnZ] SUSPECT deep npc={MemberId}@spawner{NpcSpawnerTemplateId} " +
                        $"pos=({posVec.X:F1},{posVec.Y:F1},{jsonZ:F2}) " +
                        $"delta={delta:F2}m vs bestFloor={bestFloor:F2} " +
                        $"(raycast={(hasRaycast ? raycastZ.ToString("F2") : "N/A")}, " +
                        $"hmap={(hasHmap ? hmapZ.ToString("F2") : "N/A")}) " +
                        "- keep JSON (probable cave/grotte, raycast bloque par toit)");
            return jsonZ;
        }

        // Cas 2 : JSON coherent (tolerance 0.5m) -> snap mineur
        if (Math.Abs(delta) <= maxDownSnap)
        {
            return bestFloor;
        }

        // Cas 3 : JSON entre 0.5m et MaxSnap au-dessus -> keep (unmeshed structure)
        if (delta > maxDownSnap && delta <= maxSnap)
        {
            if (logRes && delta > unmeshedThr)
                Logger.Trace($"[ResolveSpawnZ] {MemberId}@spawner{NpcSpawnerTemplateId} " +
                             $"probable unmeshed structure delta={delta:F2}m, keep JSON Z={jsonZ:F2}");
            return jsonZ;
        }

        // Cas 4 : delta > MaxSnap (>5m par defaut) -> SUSPECT mais keep JSON
        // Snap aveugle ici casserait les NPCs sur structures > 5m (tours, donjons).
        // Log warning pour review manuel des spawners suspects.
        Logger.Warn($"[ResolveSpawnZ] SUSPECT npc={MemberId}@spawner{NpcSpawnerTemplateId} " +
                    $"pos=({posVec.X:F1},{posVec.Y:F1},{jsonZ:F2}) " +
                    $"delta={delta:F2}m vs bestFloor={bestFloor:F2} " +
                    $"(raycast={(hasRaycast ? raycastZ.ToString("F2") : "N/A")}, " +
                    $"hmap={(hasHmap ? hmapZ.ToString("F2") : "N/A")}) " +
                    "- keep JSON, manual review recommended");
        return jsonZ;
    }

    /// <summary>
    /// Internal Spawn NpcGroup function for Spawn
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        return SpawnNpc(npcSpawner, ownerId);
    }
}
