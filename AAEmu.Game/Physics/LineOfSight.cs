using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using Jitter2.Collision;
using Jitter2.Collision.Shapes;
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

    /// <summary>Lambda minimal sur un hit brush pour le considérer comme un obstacle réel.
    /// Les hits avec lambda &lt; 0.5m sont des self-collisions : le caster est lui-même
    /// dans le BBox englobant d'un brush (sous un toit, sur un seuil, etc.) et le raycast
    /// tape immédiatement les bords intérieurs.</summary>
    public const float MinHitLambda = 0.5f;

    /// <summary>TTL du cache LoS en millisecondes. À 500ms, un NPC en combat consulte le
    /// cache ~5 fois entre deux recalculs réels.</summary>
    public const int CacheTtlMs = 500;

    private readonly struct CacheEntry
    {
        public DateTime Expiry { get; init; }
        public bool Result { get; init; }
    }

    private static readonly ConcurrentDictionary<long, CacheEntry> _losCache = new();

    /// <summary>
    /// Pre-filter pour DynamicTree.RayCast : accepte uniquement les RigidBodyShape de bodies
    /// statiques (brushes Phase 4B5, voxels). Exclut automatiquement HeightmapTester (qui n'est
    /// pas RigidBodyShape, géré par notre boucle heightmap dédiée) et les Slaves/ships
    /// (IsStatic = false).
    /// </summary>
    private static readonly DynamicTree.RayCastFilterPre StaticObstacleFilter = proxy =>
    {
        if (proxy is not RigidBodyShape rbs) return false;
        return rbs.RigidBody?.IsStatic ?? false;
    };

    /// <summary>
    /// Variante cachée + lock-safe de TestUnits, conçue pour être appelée à haute fréquence
    /// (combat AI tick). Le cache (TTL 500ms par paire ObjId) limite les vrais raycasts à
    /// ~2x/sec/paire ; combiné au DynamicTree (O(log N)), l'impact sur le PhysicsThread est
    /// négligeable.
    /// Bypass cache si l'un des ObjId est 0 (sécurité).
    /// </summary>
    public static bool HasLosCached(AAEmu.Game.Models.Game.Units.BaseUnit caster,
                                    AAEmu.Game.Models.Game.Units.BaseUnit target)
    {
        if (caster == null || target == null) return true;
        if (caster.ObjId == 0 || target.ObjId == 0)
            return TestUnits(caster, target, out _);

        var key = ((long)caster.ObjId << 32) | target.ObjId;
        var now = DateTime.UtcNow;

        if (_losCache.TryGetValue(key, out var entry) && entry.Expiry > now)
            return entry.Result;

        var result = TestUnits(caster, target, out _);
        _losCache[key] = new CacheEntry
        {
            Expiry = now.AddMilliseconds(CacheTtlMs),
            Result = result
        };
        return result;
    }

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

        // 2) Brushes/voxels raycast via Jitter2 DynamicTree (broad-phase BVH).
        //    O(log N) au lieu de O(N) sur 15000+ shapes. Sous lock partagé court (~µs).
        //    Le pre-filter StaticObstacleFilter exclut Slaves (dynamic) et HeightmapTester.
        var physics = world.Physics;
        if (physics?.PhysWorld?.DynamicTree != null)
        {
            // AAEmu Z-up → Jitter2 Y-up : (X, Y, Z) → (X, Z, Y)
            var jOrigin = new JVector(from.X, from.Z, from.Y);
            var jDirNorm = new JVector(dirNorm.X, dirNorm.Z, dirNorm.Y);

            float minLambda = float.MaxValue;
            bool hitFound = false;

            lock (physics.WorldLock)
            {
                if (physics.PhysWorld.DynamicTree.RayCast(jOrigin, jDirNorm, distance,
                        StaticObstacleFilter, null,
                        out _, out _, out var rayLambda))
                {
                    // lambda > MinHitLambda : filtre self-collision (caster dans un BBox brush)
                    if (rayLambda > MinHitLambda)
                    {
                        minLambda = rayLambda;
                        hitFound = true;
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

        // Minimums absolus : un humanoïde standard fait ~2m, donc oeil à 1.5m
        // et chest/torse à 1.0m. Sans ces planchers, les petites valeurs
        // ModelSize*Scale (~0.5 pour un Character) donnent un ray qui vole à
        // 0.5-0.7m du sol et se fait bloquer par des obstacles de 30cm
        // (steps, planters, bordures de cour intérieure).
        var casterEyeOffset = Math.Max(casterSize * 1.5f, 1.5f);
        var targetTorsoOffset = Math.Max(targetSize * 1.0f, 1.0f);

        var eye = casterPos with { Z = casterPos.Z + casterEyeOffset };
        var torso = targetPos with { Z = targetPos.Z + targetTorsoOffset };

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
