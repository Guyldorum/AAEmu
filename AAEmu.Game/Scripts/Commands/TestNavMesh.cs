using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestNavMesh : ICommand
{
    public string[] CommandNames { get; set; } = ["testnavmesh", "test_navmesh"];
    public static List<BaseUnit> Markers { get; set; } = [];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Shows route to target. Modes: (no arg)=FindPath actuel, 'astar'=A* direct, 'los'=diag LoS shortcut (lot-5b.o)";
    }

    private static void ClearMarkers()
    {
        foreach (var marker in Markers)
        {
            ObjectIdManager.Instance.ReleaseId(marker.ObjId);
            marker.Delete();
        }
        Markers.Clear();
    }

    private static void AddDoodadMarker(WorldInstance world, Vector3 pos, uint doodadTemplateId)
    {
        var markerDoodad = DoodadManager.Instance.Create(world, 0, doodadTemplateId);
        markerDoodad.Transform.Local.SetPosition(pos);
        markerDoodad.Show();
        Markers.Add(markerDoodad);
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var stonePostDoodad = 5622u; // Stone Post
        var crescentThroneFlagDoodad = 4763u; // Crescent Throne Flag 
        
        ClearMarkers();
        if (character.CurrentTarget is not Npc npc)
        {
            var closestNodePos = character.ParentWorld.Template.GeoData.FindСlosestToTheCurrent(character.Transform.ZoneId, character.Transform.World.Position, 0);
            messageOutput.SendMessage($"Your closest node is: {closestNodePos}");
            AddDoodadMarker(character.ParentWorld, closestNodePos.Pos, crescentThroneFlagDoodad);
            return;
        }
        var world = character.ParentWorld;
        var pos = world.Template.GeoData.FindСlosestToTheCurrent(npc.Transform.ZoneId, npc.Transform.World.Position, 0);
        messageOutput.SendMessage($"Closest to {npc.Transform.World.Position} -> {pos}");
        // [lot-5b.o] sub-command 'los' : diag du LoS-shortcut. Vérifie HasLineOfSight,
        // affiche la décision (direct vs A*), et écrit Data/Custom/losnavtest.csv.
        var useLos = args.Length > 0 && string.Equals(args[0], "los", System.StringComparison.OrdinalIgnoreCase);
        if (useLos)
        {
            var losStart = npc.Transform.World.Position;
            var losGoal = character.Transform.World.Position;
            var distance = (losGoal - losStart).Length();

            var losWatch = new Stopwatch();
            losWatch.Start();
            var hasLos = npc.HasLineOfSight(character);
            losWatch.Stop();

            // [lot-5b.o.4] Tri-état decision avec slope-based check. Mirror exact
            // du calcul dans Npc.FindPath : allowedClimb = max(1.5m, dist × 25%).
            const float MaxDirectClimbBase = 1.5f;
            const float MaxDirectClimbSlope = 0.25f;
            var verticalDelta = losGoal.Z - losStart.Z;
            var dx = losGoal.X - losStart.X;
            var dy = losGoal.Y - losStart.Y;
            var horizontalDistance = MathF.Sqrt(dx * dx + dy * dy);
            var allowedClimb = MathF.Max(MaxDirectClimbBase, horizontalDistance * MaxDirectClimbSlope);
            var slope = horizontalDistance > 0.01f ? verticalDelta / horizontalDistance : 0f;
            string decision;
            if (!hasLos) decision = "ASTAR";
            else if (verticalDelta > allowedClimb) decision = "ASTAR_CLIMB";
            else decision = "DIRECT";

            messageOutput.SendMessage($"[lot-5b.o LoS diag]");
            messageOutput.SendMessage($"  NPC: {npc.Name} (objId={npc.ObjId}, tpl={npc.TemplateId})");
            messageOutput.SendMessage($"  Distance: {distance:F1}m, verticalDelta: {verticalDelta:F2}m");
            messageOutput.SendMessage($"  Slope: {slope*100:F1}% (max {MaxDirectClimbSlope*100:F0}%), allowedClimb: {allowedClimb:F2}m");
            messageOutput.SendMessage($"  HasLineOfSight: {hasLos} (took {(long)losWatch.Elapsed.TotalMicroseconds}us)");
            messageOutput.SendMessage($"  Decision: {decision}");

            // [lot-5b.o.3] Markers : DIRECT = segment NPC→cible visualisé via stone+flag.
            // ASTAR_CLIMB ou ASTAR = run A* pour montrer le chemin alternatif (escalier ou
            // contournement structure).
            if (decision == "DIRECT")
            {
                AddDoodadMarker(world, losStart, stonePostDoodad);          // origine NPC
                AddDoodadMarker(world, losGoal, crescentThroneFlagDoodad);  // cible (flag)
            }
            else
            {
                AddDoodadMarker(world, losGoal, stonePostDoodad);
                npc.Ai.PathNode.ZoneKey = character.Transform.ZoneId;
                var fallbackPath = npc.Ai.PathNode.FindPath(
                    npc.ParentWorld, losStart, losGoal, out _).ToList();
                messageOutput.SendMessage($"  A* fallback path: {fallbackPath.Count} nodes");
                foreach (var v3 in fallbackPath)
                {
                    AddDoodadMarker(world, v3, stonePostDoodad);
                }
            }

            // CSV log Data/Custom/losnavtest.csv (header auto)
            try
            {
                var csvDir = Path.Combine(FileManager.AppPath, "Data", "Custom");
                Directory.CreateDirectory(csvDir);
                var csvFile = Path.Combine(csvDir, "losnavtest.csv");
                var needsHeader = !File.Exists(csvFile);
                using var writer = new StreamWriter(csvFile, append: true);
                if (needsHeader)
                {
                    writer.WriteLine("timestamp;world;npcName;npcObjId;templateId;npcX;npcY;npcZ;targetName;targetX;targetY;targetZ;distance;verticalDelta;slope;allowedClimb;hasLos;losUs;decision");
                }
                var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
                writer.WriteLine($"{ts};{world.Template.Name};{npc.Name};{npc.ObjId};{npc.TemplateId};{losStart.X:F2};{losStart.Y:F2};{losStart.Z:F2};{character.Name};{losGoal.X:F2};{losGoal.Y:F2};{losGoal.Z:F2};{distance:F2};{verticalDelta:F2};{slope:F3};{allowedClimb:F2};{hasLos};{(long)losWatch.Elapsed.TotalMicroseconds};{decision}");
                messageOutput.SendMessage($"  CSV: Data/Custom/losnavtest.csv (append)");
            }
            catch (Exception ex)
            {
                messageOutput.SendMessage($"  CSV write error: {ex.Message}");
            }
            return;
        }

        var watch = new Stopwatch();
        watch.Start();
        // 5b diag: sub-command 'astar' switches to PathNode.FindPath (A* on NetMission)
        // for visual comparison with default FindPath2 (greedy forbidden-area path).
        // ClearMarkers() at start of Execute lets you alternate commands at same point.
        var useAstar = args.Length > 0 && string.Equals(args[0], "astar", System.StringComparison.OrdinalIgnoreCase);
        List<Vector3> foundPath;
        if (useAstar)
        {
            npc.Ai.PathNode.ZoneKey = character.Transform.ZoneId;
            foundPath = npc.Ai.PathNode.FindPath(
                npc.ParentWorld,
                npc.Transform.World.Position,
                character.Transform.World.Position,
                out _).ToList();
            messageOutput.SendMessage("Using A* (PathNode.FindPath on NetMission)");
        }
        else
        {
            foundPath = npc.FindPath(character).ToList();
        }
        watch.Stop();
        messageOutput.SendMessage($"FindPath Took {watch.ElapsedMilliseconds}ms");
        foundPath.Insert(0, npc.Transform.World.Position);
        //foundPath.Add(character.Transform.World.Position);
        //npc.Ai.PathNode.FoundPath = foundPath;
        var lastPos = npc.Transform.World.Position;
        foreach (var v3 in foundPath)
        {
            var d = (lastPos - v3).Length();
            messageOutput.SendMessage($"-> {v3} (d {d:F1}, r {(v3 - character.Transform.World.Position).Length():F1}, a {MathUtil.CalculateAngleFrom(lastPos,v3):F1}°)");
            lastPos = v3;
            AddDoodadMarker(world, v3, stonePostDoodad);
        }
        messageOutput.SendMessage($"Reduced:");
        // messageOutput.SendMessage($"Reduced (multi-type {hasDifferentNodeTypes}):");
        // var reducedPath = hasDifferentNodeTypes ? foundPath : world.Template.GeoData.ReducePath(foundPath.ToList(), 5).ToList();
        var reducedPath = world.Template.GeoData.ReducePath(foundPath.ToList(), 5).ToList();
        //reducedPath.Insert(0, npc.Transform.World.Position);
        //reducedPath.Add(character.Transform.World.Position);
        lastPos = npc.Transform.World.Position;
        foreach (var v3 in reducedPath)
        {
            var d = (lastPos - v3).Length();
            messageOutput.SendMessage($"=> {v3} (d {d:F1}, r {(v3 - character.Transform.World.Position).Length():F1}, a {MathUtil.CalculateAngleFrom(lastPos,v3):F1}°)");
            lastPos = v3;
            AddDoodadMarker(world, v3, crescentThroneFlagDoodad);
        }
    }
}
