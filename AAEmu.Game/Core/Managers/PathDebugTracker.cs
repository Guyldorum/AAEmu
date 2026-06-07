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

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Singleton de diagnostic pathfinding pour NPCs. Capture des snapshots
/// ponctuels via SampleNpc() et supporte un mode tracking continu a 10 Hz.
///
/// Sortie : Data/Custom/pathdebug.csv (header auto si absent).
///
/// Le tracker auto-arrete son loop quand la liste de NPCs trackes se vide.
/// Lecture seule : ne modifie pas le PathNode ni la FoundPath.
/// </summary>
public sealed class PathDebugTracker : Singleton<PathDebugTracker>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int SampleIntervalMs = 100;   // 10 Hz par NPC tracke
    private const int FlushIntervalMs = 1000;   // flush CSV toutes les 1s
    private const string CsvFileName = "pathdebug.csv";

    private const string CsvHeader =
        "timestamp;mode;name;objId;templateId;world;" +
        "posX;posY;posZ;" +
        "behavior;isInCombat;" +
        "targetName;targetObjId;targetX;targetY;targetZ;" +
        "hasPath;foundPathCount;" +
        "currX;currY;currZ;" +
        "endX;endY;endZ;" +
        "peekX;peekY;peekZ;" +
        "dCurr;dEnd;dDrift;dTargetMoved";

    private readonly ConcurrentDictionary<uint, WeakReference<Npc>> _tracked = new();
    private readonly ConcurrentQueue<string> _writeBuffer = new();
    private CancellationTokenSource _cts;
    private Task _samplerTask;
    private Task _flusherTask;
    private readonly object _lifecycleLock = new();

    public struct Sample
    {
        public DateTime Timestamp;
        public string Mode;            // "dump" | "track"
        public uint ObjId;
        public string Name;
        public uint TemplateId;
        public string WorldName;

        // NPC
        public Vector3 Pos;
        public string Behavior;
        public bool IsInCombat;

        // Target (aggro)
        public bool HasTarget;
        public string TargetName;
        public uint TargetObjId;
        public Vector3 TargetPos;

        // PathNode
        public bool HasPathNode;
        public int FoundPathCount;
        public Vector3 CurrentTargetPos;
        public Vector3 EndPointPos;
        public bool HasPeekNext;
        public Vector3 PeekNext;

        // Distances calculees (NaN si donnee manquante)
        public float DCurr;            // dist NPC -> CurrentTargetPos
        public float DEnd;             // dist NPC -> EndPointPos
        public float DDrift;           // dist CurrentTargetPos -> PeekNext (desynchro)
        public float DTargetMoved;     // dist EndPointPos -> TargetPos (besoin replan)
    }

    /// <summary>Capture un snapshot complet du NPC (lecture only).</summary>
    public Sample SampleNpc(Npc npc, string mode)
    {
        var s = new Sample
        {
            Timestamp = DateTime.UtcNow,
            Mode = mode,
            ObjId = npc.ObjId,
            Name = string.IsNullOrEmpty(npc.Name) ? "unknown" : npc.Name,
            TemplateId = npc.TemplateId,
            WorldName = npc.ParentWorld?.Template?.Name ?? "unknown",
            Pos = npc.Transform?.World?.Position ?? Vector3.Zero,
            Behavior = "n/a",
            TargetName = string.Empty,
            DCurr = float.NaN,
            DEnd = float.NaN,
            DDrift = float.NaN,
            DTargetMoved = float.NaN,
        };

        // Etat combat + behavior
        try
        {
            s.IsInCombat = npc.IsInBattle;
            var behavior = npc.Ai?.GetCurrentBehavior();
            s.Behavior = behavior?.GetType().Name ?? "n/a";
        }
        catch { /* ignored */ }

        // Aggro target
        try
        {
            var target = npc.CurrentAggroTarget;
            if (target != null)
            {
                s.HasTarget = true;
                s.TargetName = target.Name ?? "?";
                s.TargetObjId = target.ObjId;
                s.TargetPos = target.Transform?.World?.Position ?? Vector3.Zero;
            }
        }
        catch { /* ignored */ }

        // PathNode (read-only ; les Queue ne sont pas thread-safe -> try)
        try
        {
            var pn = npc.Ai?.PathNode;
            if (pn != null)
            {
                s.HasPathNode = true;
                s.CurrentTargetPos = pn.CurrentTargetPos;
                s.EndPointPos = pn.EndPointPos;

                var fp = pn.FoundPath;
                if (fp != null)
                {
                    s.FoundPathCount = fp.Count;
                    if (s.FoundPathCount > 0)
                    {
                        // Peek peut lever si Dequeue concurrent => protege par try
                        try
                        {
                            s.PeekNext = fp.Peek();
                            s.HasPeekNext = true;
                        }
                        catch { /* race avec game tick */ }
                    }
                }
            }
        }
        catch { /* ignored */ }

        // Distances (calcul une fois pour CSV + chat coherents)
        if (s.HasPathNode)
        {
            s.DCurr = Vector3.Distance(s.Pos, s.CurrentTargetPos);
            s.DEnd = Vector3.Distance(s.Pos, s.EndPointPos);
            if (s.HasPeekNext)
                s.DDrift = Vector3.Distance(s.CurrentTargetPos, s.PeekNext);
            if (s.HasTarget)
                s.DTargetMoved = Vector3.Distance(s.EndPointPos, s.TargetPos);
        }

        return s;
    }

    /// <summary>Format CSV d'un sample (1 ligne).</summary>
    public string FormatCsvLine(Sample s)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("F3", ci);
        string V(Vector3 v, bool has)
        {
            if (!has) return ";;";
            return $"{v.X.ToString("F3", ci)};{v.Y.ToString("F3", ci)};{v.Z.ToString("F3", ci)}";
        }

        var sb = new StringBuilder(256);
        sb.Append(s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", ci)).Append(';');
        sb.Append(s.Mode).Append(';');
        sb.Append(s.Name).Append(';');
        sb.Append(s.ObjId.ToString(ci)).Append(';');
        sb.Append(s.TemplateId.ToString(ci)).Append(';');
        sb.Append(s.WorldName).Append(';');
        sb.Append(V(s.Pos, true)).Append(';');
        sb.Append(s.Behavior).Append(';');
        sb.Append(s.IsInCombat ? "1" : "0").Append(';');
        sb.Append(s.TargetName).Append(';');
        sb.Append(s.TargetObjId.ToString(ci)).Append(';');
        sb.Append(V(s.TargetPos, s.HasTarget)).Append(';');
        sb.Append(s.HasPathNode ? "1" : "0").Append(';');
        sb.Append(s.FoundPathCount.ToString(ci)).Append(';');
        sb.Append(V(s.CurrentTargetPos, s.HasPathNode)).Append(';');
        sb.Append(V(s.EndPointPos, s.HasPathNode)).Append(';');
        sb.Append(V(s.PeekNext, s.HasPeekNext)).Append(';');
        sb.Append(F(s.DCurr)).Append(';');
        sb.Append(F(s.DEnd)).Append(';');
        sb.Append(F(s.DDrift)).Append(';');
        sb.Append(F(s.DTargetMoved));
        return sb.ToString();
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
            Logger.Info($"[PathDebugTracker] Start tracking {npc.Name} " +
                $"(objId={npc.ObjId}, templateId={npc.TemplateId})");
            EnsureSamplerRunning();
            EnsureFlusherRunning();
        }
        return added;
    }

    public int StopAll()
    {
        var count = _tracked.Count;
        _tracked.Clear();
        Logger.Info($"[PathDebugTracker] Stop all : {count} NPC(s) untracked");
        return count;
    }

    public List<(uint ObjId, uint TemplateId, string Name)> GetTrackedList()
    {
        var result = new List<(uint, uint, string)>();
        foreach (var kvp in _tracked)
        {
            var name = "??";
            uint tpl = 0;
            if (kvp.Value.TryGetTarget(out var npc))
            {
                name = npc.Name;
                tpl = npc.TemplateId;
            }
            result.Add((kvp.Key, tpl, name));
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
        Logger.Info("[PathDebugTracker] Sampler loop started (100ms)");
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
                Logger.Warn(ex, "[PathDebugTracker] sampler loop error");
            }

            if (_tracked.IsEmpty)
            {
                Logger.Info("[PathDebugTracker] Sampler loop stop (no NPC to track)");
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
            catch (Exception ex) { Logger.Warn(ex, "[PathDebugTracker] csv header init failed"); }
        }

        Logger.Info($"[PathDebugTracker] Flusher loop started (1s) -> {logPath}");

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
                Logger.Warn(ex, "[PathDebugTracker] flusher loop error");
            }

            if (_writeBuffer.IsEmpty && _tracked.IsEmpty)
            {
                Logger.Info("[PathDebugTracker] Flusher loop stop (buffer empty and no tracking)");
                break;
            }

            try { await Task.Delay(FlushIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
