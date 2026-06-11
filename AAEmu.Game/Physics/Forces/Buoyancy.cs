using AAEmu.Game.Models.Game.Units;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Forces;

/// <summary>
/// Simple Helper that adds buoyancy forces to a body if it is within
/// the FluidVolume. The volume is represented by an axis aligned bounding box or by
/// the user.
/// </summary>
public class Buoyancy : ForceGenerator
{
    // [lot-10.1] Dedicated NLog logger, qualified to avoid clashing with Jitter2.Logger
    private static readonly NLog.Logger SpawnDiagLogger = NLog.LogManager.GetCurrentClassLogger();

    // [lot-10.1.2] Settling window : after the corrective snap at PortalTime end,
    // an external system (Replication smoothing?) tugs the Hull back toward its
    // pre-snap position. For 2s we lock Hull.Y at equilibrium and zero velocities
    // each tick to defeat this perturbation.
    private readonly Dictionary<uint, DateTime> _settlingEndsAt = new();

    // [lot-10.1.5] Track ships that have already received their POST-PORTAL
    // CORRECTIVE SNAP this spawn. Prevents the settling lock (which sets
    // AffectedByGravity=false in lot-10.1.4) from re-triggering the snap at
    // every release in an infinite loop.
    private readonly HashSet<uint> _hasSnappedThisSpawn = new();

    public static float BaseWaterDensity = 1.025f;

    /// <summary>
    /// Hot-reload tuning knob: scales ship buoyancy via an "effective water density" multiplier.
    /// 1.0 = default, higher floats higher (less draft), lower sits deeper.
    /// Applied only for ships (bodies tagged with <see cref="Slave"/> that have a <c>ShipController</c>).
    /// </summary>
    public static float ShipWaterDensityMul = 3f; // [lot-10.2] Aligné sur DEV upstream pour vitesse équivalente. Rebond au spawn neutralisé par lot-10.1.x


    /// <summary>
    /// Returns true if the given point is within the area.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>True if the given point is within the area.</returns>
    public delegate bool DefineFluidArea(ref JVector point);

    private readonly Dictionary<Shape, JVector[]> _samples = [];
    private readonly List<RigidBody> _bodies = [];

    /// <summary>
    /// The axis aligned bounding box representing the fluid.
    /// </summary>
    public JBoundingBox FluidBox { get; set; }

    /// <summary>
    /// Density of the fluid. Default is 2.0.
    /// </summary>
    public float Density { get; set; }

    /// <summary>
    /// Damping applied to the body if it is in contact with the fluid.
    /// Default is 0.1.
    /// </summary>
    public float Damping { get; set; }

    /// <summary>
    /// Flow direction and magnitude.
    /// </summary>
    public JVector Flow { get; set; }

    private DefineFluidArea _fluidArea;
    private float WaterSurfaceLevel => FluidBox.Max.Y;

    /// <summary>
    /// Creates a new instance of the FluidVolume class.
    /// </summary>
    /// <param name="world">The world.</param>
    public Buoyancy(World world) : base(world)
    {
        Density = BaseWaterDensity;
        Damping = 0.1f;
        Flow = JVector.Zero;
    }

    /// <summary>
    /// Removes bodies from the fluid.
    /// </summary>
    /// <param name="body"></param>
    public void Remove(RigidBody body)
    {
        var flag = false;

        foreach (var b in _bodies)
        {
            if (body.Shapes[0] == b.Shapes[0])
            {
                flag = true;
                break;
            }
        }

        _bodies.Remove(body);
        if (!flag) _samples.Remove(body.Shapes[0]);
    }

    /// <summary>
    /// Removes all bodies from the fluid.
    /// </summary>
    public void Clear()
    {
        _bodies.Clear();
        _samples.Clear();
    }

    /// <summary>
    /// If you don't want to use the default axis aligned bounding box as
    /// fluid area representation you can define your own area using the FluidAreaDelegate.
    /// </summary>
    /// <param name="fluidArea">A delegate specifying the fluid area. Set to null if you
    /// want to use the default box.</param>
    public void UseOwnFluidArea(DefineFluidArea fluidArea)
    {
        _fluidArea = fluidArea;
    }

    /// <summary>
    /// Adds a body to the fluid. Only bodies which where added
    /// to the fluid volume gets affected by buoyancy forces.
    /// </summary>
    /// <param name="body">The body which should be added.</param>
    /// <param name="subdivisions">The object is subdivided in smaller objects
    /// for which buoyancy force is calculated. The more subdivisions the better
    /// the results. Note that the total number of subdivisions is subdivisions³.</param>
    public void Add(RigidBody body, int subdivisions)
    {
        List<JVector> massPoints = [];

        var diff = body.Shapes[0].WorldBoundingBox.Max - body.Shapes[0].WorldBoundingBox.Min;

        if (MathHelper.CloseToZero(diff))
            throw new InvalidOperationException("BoundingBox volume of the shape is zero.");

        for (var i = 0; i < subdivisions; i++)
        {
            for (var e = 0; e < subdivisions; e++)
            {
                for (var k = 0; k < subdivisions; k++)
                {
                    JVector testVector;
                    testVector.X = body.Shapes[0].WorldBoundingBox.Min.X + diff.X / (subdivisions - 1) * i;
                    testVector.Y = body.Shapes[0].WorldBoundingBox.Min.Y + diff.Y / (subdivisions - 1) * e;
                    testVector.Z = body.Shapes[0].WorldBoundingBox.Min.Z + diff.Z / (subdivisions - 1) * k;

                    if (NarrowPhase.PointTest(body.Shapes[0], in testVector))
                    {
                        massPoints.Add(testVector);
                    }
                }
            }
        }

        _samples.Add(body.Shapes[0], massPoints.ToArray());
        _bodies.Add(body);
    }

    public void AddForRectangularParallelepiped(RigidBody body, int subdivisions)
    {
        if (body.Shapes.Count == 0)
            throw new ArgumentException("body has no shapes.");

        var shape = body.Shapes[0];
        var bbox = shape.WorldBoundingBox;
        var min = bbox.Min;
        var max = bbox.Max;

        // Dimensions of the parallelepiped
        var size = max - min;

        if (MathHelper.CloseToZero(size))
            throw new InvalidOperationException("BoundingBox volume is zero.");

        var massPoints = new List<JVector>();

        // Step between points on each axis
        var stepX = size.X / subdivisions;
        var stepY = size.Y / subdivisions;
        var stepZ = size.Z / subdivisions;

        // Generating points inside a parallelepiped
        for (var i = 0; i < subdivisions; i++)
        {
            for (var j = 0; j < subdivisions; j++)
            {
                for (var k = 0; k < subdivisions; k++)
                {
                    // Current point coordinates
                    var x = min.X + (i + 0.5f) * stepX;
                    var y = min.Y + (j + 0.5f) * stepY;
                    var z = min.Z + (k + 0.5f) * stepZ;

                    var point = new JVector(x, y, z);

                    // For a parallelepiped, all points inside the BoundingBox are considered to belong to the body
                    massPoints.Add(point);
                }
            }
        }

        // Save points
        _samples.Add(shape, massPoints.ToArray());
        _bodies.Add(body);
    }

    public override void PreStep(float timeStep)
    {
        foreach (var body in _bodies.ToArray())
        {
            if (body.IsStatic || !body.IsActive) continue;

            var slave = (Slave)body.Tag;
            if (slave == null) continue;

            // Skip if no controller or mass
            if (slave.ShipController == null || slave.ShipController.ShipModel.Mass <= 0)
                continue;

            // [lot-10.1.2] Settling-window lock. After the corrective snap, force
            // Hull at equilibrium and zero velocities every tick for 2s to defeat
            // external position perturbations. After settling expires, normal
            // buoyancy processing resumes naturally.
            if (_settlingEndsAt.TryGetValue(slave.Id, out var settlingEnd))
            {
                if (DateTime.UtcNow < settlingEnd)
                {
                    var lockedDraft = 1f / (Density * ShipWaterDensityMul);
                    var lockedPos = body.Position;
                    lockedPos.Y = WaterSurfaceLevel - lockedDraft;
                    body.Position = lockedPos;
                    body.Velocity = JVector.Zero;
                    body.AngularVelocity = JVector.Zero;
                    // [lot-10.1.4] Also disable gravity during settling so Jitter2 integrator
                    // doesn't write v_new = -g*dt at end-of-tick (which would persist past the
                    // last settling tick into the release tick and cause oscillation).
                    body.AffectedByGravity = false;
                    SpawnDiagLogger.Info($"[SPAWNDIAG] {slave.Name} SETTLING-LOCK Y={lockedPos.Y:F3} velocities=0 gravity=off (remaining={(settlingEnd - DateTime.UtcNow).TotalSeconds:F2}s)");
                    continue;
                }
                else
                {
                    _settlingEndsAt.Remove(slave.Id);
                    SpawnDiagLogger.Info($"[SPAWNDIAG] {slave.Name} SETTLING-LOCK released, normal buoyancy resumes");
                }
            }

            // [lot-10.1.1 SPAWNDIAG] Track 15s from spawn covering PortalTime + post.
            // Logged BEFORE the AffectedByGravity early-return so PortalTime drift is observable.
            var timeSinceSpawn = (DateTime.UtcNow - slave.SpawnTime).TotalSeconds;
            var portalDuration = slave.Template.PortalTime;
            var inPortal = timeSinceSpawn < portalDuration;

            if (timeSinceSpawn < 15.0)
            {
                var localOceanLvl = WaterSurfaceLevel;
                var localCenter = body.Position;
                if (_fluidArea != null && _fluidArea(ref localCenter))
                {
                    localOceanLvl = slave.CachedWaterSurface;
                }
                var localDepth = Math.Max(0, localOceanLvl - body.Position.Y);
                var phaseTag = inPortal ? "PORTAL" : "ACTIVE";
                SpawnDiagLogger.Info($"[SPAWNDIAG] {slave.Name} t={timeSinceSpawn:F3}s phase={phaseTag} posY={body.Position.Y:F3} ocean={localOceanLvl:F3} depth={localDepth:F3} velY={body.Velocity.Y:F3}");
            }

            // [lot-10.1.1] Corrective snap at the exact frame AffectedByGravity flips false->true.
            // Compensates for drift during PortalTime (runtime observed: ~14m descent on clipper).
            // Resets position to buoyancy equilibrium and zeros velocities -> no catapult.
            var wasAffected = body.AffectedByGravity;
            body.AffectedByGravity = !inPortal;
            if (!wasAffected && body.AffectedByGravity && !_hasSnappedThisSpawn.Contains(slave.Id))
            {
                _hasSnappedThisSpawn.Add(slave.Id);
                var draft = 1f / (Density * ShipWaterDensityMul);
                var preSnapY = body.Position.Y;
                var snappedPos = body.Position;
                snappedPos.Y = WaterSurfaceLevel - draft;
                body.Position = snappedPos;
                body.Velocity = JVector.Zero;
                body.AngularVelocity = JVector.Zero;
                // [lot-10.1.2] Start 2s settling window — locks Y/velocities each tick
                // to defeat external position perturbations (replication smoothing etc.).
                _settlingEndsAt[slave.Id] = DateTime.UtcNow.AddSeconds(2);
                SpawnDiagLogger.Info($"[SPAWNDIAG] {slave.Name} POST-PORTAL CORRECTIVE SNAP: preSnapY={preSnapY:F3} -> snappedY={snappedPos.Y:F3} (ocean={WaterSurfaceLevel:F3} draft={draft:F3}) velocities zeroed, settling 2s");
            }
            if (!body.AffectedByGravity)
            {
                continue;
            }

            var waterSurfaceLevel = WaterSurfaceLevel;
            var centerPosition = body.Position;
            if (_fluidArea != null && _fluidArea(ref centerPosition))
            {
                waterSurfaceLevel = slave.CachedWaterSurface;
            }

            var depth = waterSurfaceLevel - body.Position.Y;
            if (depth <= 0) continue;

            ApplyDrag(body, slave.ShipController.ShipModel.MassBoxSizeX, slave.ShipController.ShipModel.MassBoxSizeY, slave.ShipController.ShipModel.MassBoxSizeZ);
            // Calculate submerged depth and buoyancy force
            var submergedDepth = Math.Max(0, waterSurfaceLevel - body.Position.Y);
            var isOnWater = submergedDepth > 0;

            if (isOnWater)
            {
                // Apply buoyancy and drag forces
                var buoyancyForce = new JVector(0, submergedDepth * body.Mass * Density * ShipWaterDensityMul * 9.81f, 0);
                body.AddForce(buoyancyForce);

                var dragForce = new JVector(-body.Velocity.X * Density, -body.Velocity.Y * Density, -body.Velocity.Z * Density);
                body.AddForce(dragForce);
            }

            // [lot-10.1 SPAWNDIAG] Log first 3s after PortalTime expiration to observe
            // spawn rebound behavior. Auto-disables after timeSincePortal >= 3s.
            var portalEndTime = slave.SpawnTime.AddSeconds(slave.Template.PortalTime);
            var timeSincePortal = (DateTime.UtcNow - portalEndTime).TotalSeconds;
            if (timeSincePortal >= 0 && timeSincePortal < 3.0)
            {
                var buoyMag = submergedDepth * body.Mass * Density * ShipWaterDensityMul * 9.81f;
                SpawnDiagLogger.Info($"[SPAWNDIAG] {slave.Name} t={timeSincePortal:F3}s posY={body.Position.Y:F3} ocean={waterSurfaceLevel:F3} depth={submergedDepth:F3} velY={body.Velocity.Y:F3} buoyF={buoyMag:F1}");
            }
        }
    }

    private void ApplyDrag(RigidBody body, float hullWidth, float hullLength, float hullHeight)
    {
        var velocity = body.Velocity;
        var speed = velocity.Length();
        if (speed < 0.1f) return;

        const float DragCoefficient = 0.8f;
        var area = hullWidth * hullHeight;
        var drag = 0.5f * Density * DragCoefficient * area * speed * speed;
        JVector.NormalizeInPlace(ref velocity);
        JVector.NegateInPlace(ref velocity);
        velocity *= drag;
        body.AddForce(velocity);
    }
}
