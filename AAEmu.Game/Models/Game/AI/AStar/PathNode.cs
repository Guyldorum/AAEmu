// https://lsreg.ru/realizaciya-algoritma-poiska-a-na-c/

using System.Collections.ObjectModel;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.AStar;

/// <summary>
/// Reusable A* pathfinder.
/// </summary>
public class PathNode
{
    /// <summary>
    /// Current zone.Id 
    /// </summary>
    public uint ZoneKey { get; set; }

    /// <summary>
    /// The current point on the map.
    /// </summary>
    public Vector3 CurrentTargetPos { get; set; }

    /// <summary>
    /// Coordinates of the start point on the map (for the script).
    /// </summary>
    public Vector3 StartPointPos { get; set; } = Vector3.Zero;

    /// <summary>
    /// Coordinates of the end point on the map (for the script).
    /// </summary>
    public Vector3 EndPointPos { get; set; } = Vector3.Zero;

    /// <summary>
    /// List of found points (for the script).
    /// </summary>
    public Queue<Vector3> FoundPath { get; set; } = [];

    /// <summary>
    /// The coordinates of the point on the map. And the coordinates of the point on the map where the Npc goes.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Path length from the start (G).
    /// </summary>
    private float PathLengthFromStart { get; set; }

    /// <summary>
    /// The point from which it came to this point.
    /// </summary>
    private PathNode CameFrom { get; set; }

    /// <summary>
    /// Approximate distance to target (H).
    /// </summary>
    private float PathLengthToEnd { get; init; }

    /// <summary>
    /// Expected total distance to target (F).
    /// </summary>
    private float EstimateFullPathLength => PathLengthFromStart + PathLengthToEnd;

    /// <summary>
    /// Copy of the nodeDescription type used for this position
    /// </summary>
    public byte NodeType { get; set; }

    /// <summary>
    /// A* on the NetMission graph (lot-5b.c rewrite).
    /// Returns waypoints from start to goal, with goalLocation appended for final approach.
    /// </summary>
    public List<Vector3> FindPath(WorldInstance world, Vector3 startLocation, Vector3 goalLocation, out bool containsDifferentNodeTypes)
    {
        containsDifferentNodeTypes = false;

        // Resolve start and end as actual NetMission nodes (the closest one to each query position).
        var posStart = world.Template.GeoData.FindСlosestToTheCurrent(ZoneKey, startLocation, 0);
        var posEnd = world.Template.GeoData.FindСlosestToTheCurrent(ZoneKey, goalLocation, 0);
        if (posStart == null || posEnd == null)
            return [];

        if (posStart.Type != posEnd.Type)
            containsDifferentNodeTypes = true;

        EndPointPos = posEnd.Pos;

        // Trivial case: same node. Return the NPC's actual position as the first
        // waypoint and goalLocation as the second. CurrentTargetPos = startLocation
        // matches the FindPath2 convention that tells BaseCombatBehavior to enter
        // its Dequeue branch on the next tick (dist <= ModelSize).
        if (posStart.Id == posEnd.Id)
        {
            Position = startLocation;
            CurrentTargetPos = startLocation;
            return [startLocation, goalLocation];
        }

        // A* state. Keys are NetMission node Ids (int, promoted to long for Dict<long,...>).
        var gScore = new Dictionary<long, float> { [posStart.Id] = 0f };
        var cameFrom = new Dictionary<long, long>();
        var nodes = new Dictionary<long, NodeDescriptor>
        {
            [posStart.Id] = posStart,
            [posEnd.Id] = posEnd
        };
        var openSet = new HashSet<long> { posStart.Id };
        var closedSet = new HashSet<long>();

        // Iteration cap: scales with raw distance but bounded. Each hop ~ a few meters on the
        // NetMission, so 5 hops/m is generous; min 500, max 5000.
        var rawDistance = Vector3.Distance(posStart.Pos, posEnd.Pos);
        var maxIterations = (int)Math.Min(5000.0, Math.Max(500.0, rawDistance * 5.0 + 200.0));
        var iterations = 0;

        while (openSet.Count > 0 && iterations++ < maxIterations)
        {
            // Pick the node in openSet with the lowest F = G + H. Linear scan; openSet stays small.
            long currentId = -1;
            var bestF = float.MaxValue;
            foreach (var id in openSet)
            {
                var nDesc = nodes[id];
                var h = Vector3.Distance(nDesc.Pos, posEnd.Pos);
                var f = gScore[id] + h;
                if (f < bestF)
                {
                    bestF = f;
                    currentId = id;
                }
            }

            // Goal reached: reconstruct path from cameFrom chain.
            if (currentId == posEnd.Id)
            {
                var path = new List<Vector3>();
                var traceId = currentId;
                path.Add(nodes[traceId].Pos);
                while (cameFrom.TryGetValue(traceId, out var prevId))
                {
                    traceId = prevId;
                    path.Add(nodes[traceId].Pos);
                }
                path.Reverse();

                // Prefix with the NPC's actual position so the first waypoint isn't a
                // navmesh node potentially behind the NPC (which would cause a backward step
                // when Dequeue runs). DP smoothing will fuse it with posStart.Pos if close.
                path.Insert(0, startLocation);

                // Append the true goal location so the NPC finishes on the player, not on the closest node.
                path.Add(goalLocation);

                // Smooth with Douglas-Peucker (tolerance 2m).
                path = AiGeoDataManager.DouglasPeuckerReduction(path, 2.0);
                Position = startLocation;
                // BaseCombatBehavior:140 expects CurrentTargetPos near NPC.pos to trigger
                // its Dequeue branch on the next tick. Vector3.Zero would point at the
                // world origin and make the NPC march south-west until leash reset.
                CurrentTargetPos = startLocation;
                return path;
            }

            openSet.Remove(currentId);
            closedSet.Add(currentId);

            var current = nodes[currentId];

            // Outgoing edges from current node. point.NetMission?.LinkDescriptorList is the
            // (already loaded) graph for this NetMissionReader; Where() bounds to ~5k links.
            var links = current.NetMission?.LinkDescriptorList;
            if (links == null) continue;

            foreach (var link in links)
            {
                if (link.SourceNode != current.Id) continue;

                var tgt = link.TargetNodeDescriptor;
                if (tgt == null) continue;
                if (closedSet.Contains(tgt.Id)) continue;

                // Z-aware forbidden check (lot-5b.b). Note: Z of tgt.Pos is the navmesh Z,
                // NOT overwritten by GetHeight() — preserves multi-level structures (caves,
                // domes, upper platforms).
                if (world.Template.GeoData.CheckImpossibleWalk(tgt.Pos)) continue;

                if (!nodes.ContainsKey(tgt.Id))
                    nodes[tgt.Id] = tgt;

                var tentativeG = gScore[currentId] + Vector3.Distance(current.Pos, tgt.Pos);

                if (!gScore.TryGetValue(tgt.Id, out var existingG) || tentativeG < existingG)
                {
                    gScore[tgt.Id] = tentativeG;
                    cameFrom[tgt.Id] = currentId;
                    openSet.Add(tgt.Id);
                }
            }
        }

        // No path within iteration budget.
        return [];
    }

    /// <summary>
    /// G: Function for the distance from the starting point to the current point.
    /// </summary>
    /// <param name="to"></param>
    /// <returns></returns>
    private float GetDistanceFromStart(Vector3 to)
    {
        var fromVector = new Vector3(StartPointPos.X, StartPointPos.Y, StartPointPos.Z);
        var toVector = new Vector3(to.X, to.Y, to.Z);
        return MathUtil.CalculateDistance(fromVector, toVector);
    }

    /// <summary>
    /// H: Estimates the distance to the target.
    /// </summary>
    /// <param name="from"></param>
    /// <returns></returns>
    private float GetHeuristicPathLength(Vector3 from)
    {
        // point-to-point distance
        var fromVector = new Vector3(from.X, from.Y, from.Z);
        var toVector = new Vector3(EndPointPos.X, EndPointPos.Y, EndPointPos.Z);
        return MathUtil.CalculateDistance(fromVector, toVector);
    }

    /// <summary>
    /// Obtaining a list of neighbors
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pathNode"></param>
    /// <returns></returns>
    private Collection<PathNode> GetNeighbours(WorldInstance world, PathNode pathNode)
    {
        var result = new Collection<PathNode>();

        // Check which navmesh file is valid at this position
        var bai = world.Template.GetBaiByPos(pathNode.CurrentTargetPos);
        if (bai == null)
        {
            return result;
        }

        // Find the nearest node
        var nearestNode = bai.FindClosestNetMissionNode(pathNode.CurrentTargetPos, 0);
        if (nearestNode == null)
        {
            // Was not able to find a nearby node
            // TODO: create a fall-back system
            return result;
        }

        // The adjacent points are the points where you can go.
        var neighbourPoints = world.Template.GeoData.GetAvailablePoints(nearestNode);

        foreach (var linkDescriptor in neighbourPoints)
        {
            // Checking that the point falls within the forbidden area where it is not allowed to walk.
            if (world.Template.GeoData.CheckImpossibleWalk(linkDescriptor.TargetNodeDescriptor.Pos))
            {
                //ViewPoint(point.Position, 858u); // let's show the point for debugging purposes
                continue;
            }

            // Fill in the data for the waypoint.
            var neighbourNode = new PathNode
            {
                CurrentTargetPos = linkDescriptor.TargetNodeDescriptor.Pos,
                Position = linkDescriptor.TargetNodeDescriptor.Pos,
                EndPointPos = pathNode.EndPointPos,
                CameFrom = pathNode,
                PathLengthFromStart = (linkDescriptor.SourceNodeDescriptor.Pos - pathNode.EndPointPos).Length2D(), // GetDistanceFromStart(linkDescriptor.SourceNodeDescriptor.Pos),
                PathLengthToEnd = (linkDescriptor.TargetNodeDescriptor.Pos - pathNode.EndPointPos).Length2D() // GetHeuristicPathLength(linkDescriptor.TargetNodeDescriptor.Pos)
            };

            // Align to solid floor
            neighbourNode.Position = neighbourNode.Position with { Z = world.GetHeight(neighbourNode.Position) };
            result.Add(neighbourNode);
        }

        return result;
    }

    /// <summary>
    /// Obtaining a route. The route is represented as a list of point coordinates.
    /// </summary>
    /// <param name="pathNode"></param>
    /// <returns></returns>
    private static List<Vector3> GetPathForNode(PathNode pathNode, out bool hasDifferentNodeTypes)
    {
        var result = new List<Vector3>();
        var currentNode = pathNode;
        hasDifferentNodeTypes = false;
        var startNodeType = pathNode.NodeType;
        while (currentNode != null)
        {
            result.Add(currentNode.Position);
            //ViewPoint(currentNode.Position, 5014u); // let's show the point for debugging purposes
            currentNode = currentNode.CameFrom;
            if (currentNode?.NodeType != startNodeType)
                hasDifferentNodeTypes = true;
        }
        result.Reverse();

        return result;
    }
}
