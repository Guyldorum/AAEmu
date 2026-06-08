using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Phase 5 — Singleton de diagnostic Line-of-Sight.
/// Dump ponctuel via SampleLos() et tracking continu 10Hz.
/// Sortie : Data/Custom/losdebug.csv.
/// </summary>
public sealed class LosDebugTracker : Singleton<LosDebugTracker>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int TrackingHz = 10;
    private const string CsvPath = "Data/Custom/losdebug.csv";

    private const string CsvHeader =
        "timestamp;mode;caster;casterObjId;cX;cY;cZ;" +
        "target;targetObjId;tX;tY;tZ;dist3D;" +
        "clear;hitType;hX;hY;hZ;distToHit";

    private CancellationTokenSource _cts;
    private Task _loop;

    // tracking unique : 1 paire caster→target à la fois
    private readonly ConcurrentDictionary<uint, TrackPair> _tracked = new();

    public readonly struct Sample
    {
        public DateTime Timestamp { get; init; }
        public string Mode { get; init; }
        public string CasterName { get; init; }
        public uint CasterObjId { get; init; }
        public Vector3 CasterPos { get; init; }
        public string TargetName { get; init; }
        public uint TargetObjId { get; init; }
        public Vector3 TargetPos { get; init; }
        public float Distance { get; init; }
        public bool Clear { get; init; }
        public string HitType { get; init; }
        public Vector3 HitPoint { get; init; }
        public float DistanceToHit { get; init; }
    }

    private sealed class TrackPair
    {
        public Character Caster { get; init; }
        public BaseUnit Target { get; init; }
    }

    public Sample SampleLos(Character caster, BaseUnit target, string mode)
    {
        var casterPos = caster.Transform.World.Position;
        var targetPos = target.Transform.World.Position;

        LineOfSight.TestUnits(caster, target, out var hit);

        return new Sample
        {
            Timestamp = DateTime.UtcNow,
            Mode = mode,
            CasterName = caster.Name ?? "?",
            CasterObjId = caster.ObjId,
            CasterPos = casterPos,
            TargetName = target.Name ?? "?",
            TargetObjId = target.ObjId,
            TargetPos = targetPos,
            Distance = Vector3.Distance(casterPos, targetPos),
            Clear = hit.Clear,
            HitType = hit.HitType ?? "none",
            HitPoint = hit.HitPoint,
            DistanceToHit = hit.DistanceToHit
        };
    }

    public void WriteCsv(Sample s)
    {
        try
        {
            EnsureCsv();
            var line = FormatCsv(s);
            File.AppendAllText(CsvPath, line + Environment.NewLine);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "LosDebugTracker.WriteCsv failed");
        }
    }

    public bool StartTracking(Character caster, BaseUnit target)
    {
        if (caster == null || target == null) return false;
        if (_tracked.ContainsKey(caster.ObjId)) return false;
        _tracked[caster.ObjId] = new TrackPair { Caster = caster, Target = target };
        EnsureLoop();
        return true;
    }

    public void StopTracking()
    {
        _tracked.Clear();
        // le loop s'auto-arrête quand _tracked est vide
    }

    public string StatusSummary()
    {
        var n = _tracked.Count;
        if (n == 0) return "Tracking LoS : inactif";
        var pair = _tracked.Values.GetEnumerator();
        if (pair.MoveNext())
        {
            var p = pair.Current;
            return $"Tracking LoS actif : {p.Caster.Name} → {p.Target.Name} @ {TrackingHz}Hz";
        }
        return $"Tracking LoS : {n} entrée(s)";
    }

    private void EnsureLoop()
    {
        if (_loop != null && !_loop.IsCompleted) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromMilliseconds(1000.0 / TrackingHz);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_tracked.IsEmpty) break;

                foreach (var kv in _tracked)
                {
                    var p = kv.Value;
                    if (p.Caster == null || p.Target == null) { _tracked.TryRemove(kv.Key, out _); continue; }
                    try
                    {
                        var s = SampleLos(p.Caster, p.Target, "track");
                        WriteCsv(s);
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e, "LosDebugTracker loop sample failed");
                    }
                }
                await Task.Delay(period, ct);
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            Logger.Info("LosDebugTracker loop stopped");
        }
    }

    private static void EnsureCsv()
    {
        var dir = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        if (!File.Exists(CsvPath))
            File.WriteAllText(CsvPath, CsvHeader + Environment.NewLine);
    }

    private static string FormatCsv(Sample s)
    {
        var sb = new StringBuilder(256);
        sb.Append(s.Timestamp.ToString("o")).Append(';');
        sb.Append(s.Mode).Append(';');
        sb.Append(Escape(s.CasterName)).Append(';');
        sb.Append(s.CasterObjId).Append(';');
        sb.Append(s.CasterPos.X.ToString("F2")).Append(';');
        sb.Append(s.CasterPos.Y.ToString("F2")).Append(';');
        sb.Append(s.CasterPos.Z.ToString("F2")).Append(';');
        sb.Append(Escape(s.TargetName)).Append(';');
        sb.Append(s.TargetObjId).Append(';');
        sb.Append(s.TargetPos.X.ToString("F2")).Append(';');
        sb.Append(s.TargetPos.Y.ToString("F2")).Append(';');
        sb.Append(s.TargetPos.Z.ToString("F2")).Append(';');
        sb.Append(s.Distance.ToString("F2")).Append(';');
        sb.Append(s.Clear ? "1" : "0").Append(';');
        sb.Append(s.HitType).Append(';');
        sb.Append(s.HitPoint.X.ToString("F2")).Append(';');
        sb.Append(s.HitPoint.Y.ToString("F2")).Append(';');
        sb.Append(s.HitPoint.Z.ToString("F2")).Append(';');
        sb.Append(s.DistanceToHit.ToString("F2"));
        return sb.ToString();
    }

    private static string Escape(string s) => (s ?? "").Replace(';', ',');
}
