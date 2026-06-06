using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.NPChar;

using Jitter2.Dynamics;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Singleton de diagnostic Z pour NPCs. Capture des snapshots ponctuels via
/// SampleNpc() et supporte un mode tracking continu a 10 Hz par NPC inscrit.
///
/// Sortie : Data/Custom/npcheight.csv (header auto si absent).
///
/// Le tracker auto-arrete son loop quand la liste de NPCs trackes se vide.
/// </summary>
public sealed class NpcHeightTracker : Singleton<NpcHeightTracker>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int SampleIntervalMs = 100;   // 10 Hz par NPC tracke
    private const int FlushIntervalMs = 1000;   // flush CSV toutes les 1s
    private const string CsvFileName = "npcheight.csv";

    private const string CsvHeader =
        "timestamp;mode;name;objId;templateId;spawnerId;world;X;Y;Z;" +
        "spawnerZ;dPosSpawner;HmapZ;dHmap;raycastZ;raycastHit;dRaycast;" +
        "isInCombat;behavior;aggroTarget";

    private readonly ConcurrentDictionary<uint, WeakReference<Npc>> _tracked = new();
    private readonly ConcurrentQueue<string> _writeBuffer = new();
    private CancellationTokenSource _cts;
    private Task _samplerTask;
    private Task _flusherTask;
    private readonly object _lifecycleLock = new();

    public struct Sample
    {
        public DateTime Timestamp;
        public string Mode;          // "dump" | "track"
        public uint ObjId;
        public string Name;
        public uint TemplateId;
        public uint SpawnerId;
        public string WorldName;
        public Vector3 Pos;
        public float SpawnerZ;
        public bool HasSpawnerZ;
        public float HeightmapZ;
        public bool HasHmapZ;
        public float RaycastZ;
        public string RaycastHit;    // "heightmap" | "voxel" | "brush" | "n/a"
        public bool HasRaycastZ;
        public bool IsInCombat;
        public string Behavior;
        public string AggroTarget;
    }

    /// <summary>Capture un snapshot complet du NPC (lecture only, pas de modif).</summary>
    public Sample SampleNpc(Npc npc, string mode)
    {
        var s = new Sample
        {
            Timestamp = DateTime.UtcNow,
            Mode = mode,
            ObjId = npc.ObjId,
            Name = string.IsNullOrEmpty(npc.Name) ? "unknown" : npc.Name,
            TemplateId = npc.TemplateId,
            SpawnerId = npc.Spawner?.Id ?? 0u,
            WorldName = npc.ParentWorld?.Template?.Name ?? "unknown",
            Pos = npc.Transform?.World?.Position ?? Vector3.Zero,
            RaycastHit = "n/a",
            Behavior = "n/a",
            AggroTarget = string.Empty,
        };

        // SpawnerZ = position du JSON (npc_spawns.json)
        if (npc.Spawner != null)
        {
            s.SpawnerZ = npc.Spawner.Position.Z;
            s.HasSpawnerZ = true;
        }

        // HeightmapZ = sol naturel bilineaire
        try
        {
            var hmap = npc.ParentWorld?.Template?.GetHeight(s.Pos.X, s.Pos.Y);
            if (hmap.HasValue && !float.IsNaN(hmap.Value) && !float.IsInfinity(hmap.Value))
            {
                s.HeightmapZ = hmap.Value;
                s.HasHmapZ = true;
            }
        }
        catch { /* ignored */ }

        // RaycastZ = ce que la physique voit (voxel terrain + brush structures + hmap fallback)
        try
        {
            if (npc.ParentWorld != null)
            {
                var rZ = npc.ParentWorld.GetHeight(s.Pos, out var hit);
                s.RaycastZ = rZ;
                s.HasRaycastZ = !float.IsNaN(rZ) && !float.IsInfinity(rZ);
                s.RaycastHit = ClassifyHit(npc.ParentWorld, hit);
            }
        }
        catch { /* ignored */ }

        // Etat IA / combat
        try
        {
            s.IsInCombat = npc.IsInBattle;
            s.AggroTarget = npc.CurrentAggroTarget?.Name ?? string.Empty;
            var behavior = npc.Ai?.GetCurrentBehavior();
            s.Behavior = behavior?.GetType().Name ?? "n/a";
        }
        catch { /* ignored */ }

        return s;
    }

    private static string ClassifyHit(AAEmu.Game.Models.Game.World.WorldInstance world, RigidBody hit)
    {
        if (hit == null) return "heightmap";
        try
        {
            if (world.Physics?.BrushObjects != null
                && world.Physics.BrushObjects.ToArray().Any(b => ReferenceEquals(b, hit)))
                return "brush";
            if (world.Physics?.VoxelObjects != null
                && world.Physics.VoxelObjects.ToArray().Any(v => ReferenceEquals(v, hit)))
                return "voxel";
        }
        catch { /* ignored */ }
        return "other";
    }

    /// <summary>Format CSV d'un sample (1 ligne).</summary>
    public string FormatCsvLine(Sample s)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(float v) => float.IsNaN(v) ? "" : v.ToString("F3", ci);

        var dPosSpawner = s.HasSpawnerZ ? s.Pos.Z - s.SpawnerZ : float.NaN;
        var dHmap = s.HasHmapZ ? s.Pos.Z - s.HeightmapZ : float.NaN;
        var dRaycast = s.HasRaycastZ ? s.Pos.Z - s.RaycastZ : float.NaN;

        return string.Join(";", new[]
        {
            s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", ci),
            s.Mode,
            s.Name,
            s.ObjId.ToString(ci),
            s.TemplateId.ToString(ci),
            s.SpawnerId.ToString(ci),
            s.WorldName,
            s.Pos.X.ToString("F3", ci),
            s.Pos.Y.ToString("F3", ci),
            s.Pos.Z.ToString("F3", ci),
            s.HasSpawnerZ ? F(s.SpawnerZ) : "",
            F(dPosSpawner),
            s.HasHmapZ ? F(s.HeightmapZ) : "",
            F(dHmap),
            s.HasRaycastZ ? F(s.RaycastZ) : "",
            s.RaycastHit,
            F(dRaycast),
            s.IsInCombat ? "1" : "0",
            s.Behavior,
            s.AggroTarget,
        });
    }

    /// <summary>Enqueue une ligne CSV. Demarre le flusher si necessaire.</summary>
    public void WriteCsv(Sample s)
    {
        _writeBuffer.Enqueue(FormatCsvLine(s));
        EnsureFlusherRunning();
    }

    public bool StartTracking(Npc npc)
    {
        if (npc == null) return false;
        var added = _tracked.TryAdd(npc.ObjId, new WeakReference<Npc>(npc));
        if (added)
        {
            Logger.Info($"[NpcHeightTracker] Start tracking {npc.Name} (objId={npc.ObjId})");
            EnsureSamplerRunning();
            EnsureFlusherRunning();
        }
        return added;
    }

    public int StopAll()
    {
        var count = _tracked.Count;
        _tracked.Clear();
        Logger.Info($"[NpcHeightTracker] Stop all : {count} NPC(s) untracked");
        return count;
    }

    public List<(uint ObjId, string Name)> GetTrackedList()
    {
        var result = new List<(uint, string)>();
        foreach (var kvp in _tracked)
        {
            var name = "??";
            if (kvp.Value.TryGetTarget(out var npc))
                name = npc.Name;
            result.Add((kvp.Key, name));
        }
        return result;
    }

    private void EnsureSamplerRunning()
    {
        lock (_lifecycleLock)
        {
            if (_samplerTask != null && !_samplerTask.IsCompleted) return;
            _cts ??= new CancellationTokenSource();
            var token = _cts.Token;
            _samplerTask = Task.Run(() => SamplerLoop(token), token);
        }
    }

    private void EnsureFlusherRunning()
    {
        lock (_lifecycleLock)
        {
            if (_flusherTask != null && !_flusherTask.IsCompleted) return;
            _cts ??= new CancellationTokenSource();
            var token = _cts.Token;
            _flusherTask = Task.Run(() => FlusherLoop(token), token);
        }
    }

    private async Task SamplerLoop(CancellationToken ct)
    {
        Logger.Info("[NpcHeightTracker] Sampler loop started (100ms)");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var kvp in _tracked.ToArray())
                {
                    if (!kvp.Value.TryGetTarget(out var npc) || npc.ObjId == 0)
                    {
                        _tracked.TryRemove(kvp.Key, out _);
                        continue;
                    }
                    var sample = SampleNpc(npc, "track");
                    _writeBuffer.Enqueue(FormatCsvLine(sample));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[NpcHeightTracker] sampler loop error");
            }

            if (_tracked.IsEmpty)
            {
                Logger.Info("[NpcHeightTracker] Sampler loop stop (no NPC to track)");
                break;
            }

            try { await Task.Delay(SampleIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task FlusherLoop(CancellationToken ct)
    {
        var logDir = Path.Combine(FileManager.AppPath, "Data", "Custom");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, CsvFileName);
        if (!File.Exists(logPath))
        {
            try { File.WriteAllText(logPath, CsvHeader + Environment.NewLine); }
            catch (Exception ex) { Logger.Warn(ex, "[NpcHeightTracker] csv header init failed"); }
        }

        Logger.Info($"[NpcHeightTracker] Flusher loop started (1s) -> {logPath}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_writeBuffer.IsEmpty)
                {
                    var sb = new StringBuilder();
                    while (_writeBuffer.TryDequeue(out var line))
                        sb.AppendLine(line);
                    if (sb.Length > 0)
                        await File.AppendAllTextAsync(logPath, sb.ToString(), ct);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[NpcHeightTracker] flusher loop error");
            }

            // Si plus rien a flush ET plus rien a tracker, on s'arrete
            if (_writeBuffer.IsEmpty && _tracked.IsEmpty)
            {
                Logger.Info("[NpcHeightTracker] Flusher loop stop (buffer empty and no tracking)");
                break;
            }

            try { await Task.Delay(FlushIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
