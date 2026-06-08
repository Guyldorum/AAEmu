using System;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Phase 5 — Line-of-Sight raycast entre 2 points world-space ou 2 units.
/// - Heightmap sampling discret (catch les collines/terrain).
/// - Brushes raycast Jitter2 (catch les structures construites Phase 4B5).
/// Convention d'axes : AAEmu est Z-up, Jitter2 est Y-up. On swap Y↔Z.
/// </summary>
public static class LineOfSight
{
    /// <summary>Distance max au-delà de laquelle on retourne true (out-of-range).
    /// Les skills étant cappés à 25m via DB, 30m est une marge généreuse.</summary>
    public const float MaxLosDistance = 30f;

    /// <summary>Pas d'échantillonnage heightmap en mètres.</summary>
    public const float HeightmapSampleStep = 1f;

    /// <summary>Tolérance verticale : si terrainZ &gt; rayZ + tol → blocked.</summary>
    public const float HeightmapTolerance = 0.1f;

    /// <summary>
    /// Test LoS entre 2 points AAEmu (Z-up). Retourne true si dégagé.
    /// </summary>
    public static bool TestLine(WorldInstance world, Vector3 from, Vector3 to, out LosHit hit)
    {
        hit = LosHit.MakeClear();
        if (world == null) { hit = LosHit.MakeNoWorld(); return true; }

        var dir = to - from;
        var distance = dir.Length();
        if (distance < 0.01f) return true;
        if (distance > MaxLosDistance) { hit = LosHit.MakeOutOfRange(distance); return true; }

        var dirNorm = dir / distance;

        // 1) Heightmap sampling
        int numSamples = Math.Max(2, (int)Math.Ceiling(distance / HeightmapSampleStep));
        for (int i = 1; i < numSamples; i++)
        {
            float t = (float)i / numSamples;
            Vector3 p = from + dirNorm * (distance * t);
            float terrainZ = world.GetHeight(p.X, p.Y);
            if (terrainZ > p.Z + HeightmapTolerance)
            {
                hit = new LosHit
                {
                    Clear = false,
                    HitType = "heightmap",
                    HitPoint = new Vector3(p.X, p.Y, terrainZ),
                    DistanceToHit = distance * t
                };
                return false;
            }
        }

        // 2) Brushes raycast (Jitter2)
        var physics = world.Physics;
        if (physics?.BrushObjects != null && physics.BrushObjects.Count > 0)
        {
            // AAEmu Z-up → Jitter2 Y-up : (X, Y, Z) → (X, Z, Y)
            var jOrigin = new JVector(from.X, from.Z, from.Y);
            var jDirNorm = new JVector(dirNorm.X, dirNorm.Z, dirNorm.Y);

            float minLambda = float.MaxValue;
            bool hitFound = false;

            foreach (var brush in physics.BrushObjects.ToArray())
            {
                foreach (var brushShape in brush.Shapes)
                {
                    if (brushShape.RayCast(jOrigin, jDirNorm, out _, out var lambda))
                    {
                        if (lambda > 0f && lambda < distance && lambda < minLambda)
                        {
                            minLambda = lambda;
                            hitFound = true;
                        }
                    }
                }
            }

            if (hitFound)
            {
                var jHitPoint = jOrigin + jDirNorm * minLambda;
                // Jitter Y-up → AAEmu Z-up : (X, Y, Z) → (X, Z, Y)
                hit = new LosHit
                {
                    Clear = false,
                    HitType = "brush",
                    HitPoint = new Vector3(jHitPoint.X, jHitPoint.Z, jHitPoint.Y),
                    DistanceToHit = minLambda
                };
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Wrapper pratique testant LoS entre 2 units (œil caster vers torse cible).
    /// Hauteurs estimées via ModelSize * Scale.
    /// </summary>
    public static bool TestUnits(BaseUnit caster, BaseUnit target, out LosHit hit)
    {
        hit = LosHit.MakeClear();
        if (caster == null || target == null) return true;

        var world = (caster as Unit)?.ParentWorld ?? (target as Unit)?.ParentWorld;
        if (world == null) { hit = LosHit.MakeNoWorld(); return true; }

        var casterPos = caster.Transform.World.Position;
        var targetPos = target.Transform.World.Position;

        var casterSize = Math.Max(caster.ModelSize, 0.5f) * Math.Max(caster.Scale, 0.5f);
        var targetSize = Math.Max(target.ModelSize, 0.5f) * Math.Max(target.Scale, 0.5f);

        var eye = casterPos with { Z = casterPos.Z + casterSize * 1.5f };
        var torso = targetPos with { Z = targetPos.Z + targetSize * 0.75f };

        return TestLine(world, eye, torso, out hit);
    }
}

public readonly struct LosHit
{
    public bool Clear { get; init; }
    /// <summary>"none" | "heightmap" | "brush" | "no-world" | "out-of-range"</summary>
    public string HitType { get; init; }
    public Vector3 HitPoint { get; init; }
    public float DistanceToHit { get; init; }

    public static LosHit MakeClear() => new() { Clear = true, HitType = "none" };
    public static LosHit MakeNoWorld() => new() { Clear = true, HitType = "no-world" };
    public static LosHit MakeOutOfRange(float dist) => new() { Clear = true, HitType = "out-of-range", DistanceToHit = dist };
}
