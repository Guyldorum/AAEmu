#define EXPORT_TERRAIN_ON_LOAD

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Physics.HeightMaps;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

// ReSharper disable HollowTypeName
public class PhysicsManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// WorldInstance this physics engine is running for
    /// </summary>
    public WorldInstance SimulationWorld { get; init; }

    private const float DefaultWaterLevel = 100f;

    /// <summary>
    /// Target Ticks per Second for Physics in this world, use setting as default value
    /// </summary>
    // TODO: Make this variable or configurable from a GM command or dynamic load system
    public float TargetPhysicsTps { get; set; } = AppConfiguration.Instance.World.TargetPhysicsTps;

    public float TargetPhysicsTickTime => 1f / TargetPhysicsTps;
    internal Thread _thread;

    /// <summary>
    /// The physics engine's World
    /// </summary>
    public Jitter2.World PhysWorld { get; private set; }

    /// <summary>
    /// Buoyancy handler for ships
    /// </summary>
    public Buoyancy Buoyancy { get; private set; }

    internal bool ThreadRunning { get; set; }

    /// <summary>
    /// List of Ship controllers (slaveId, controller)
    /// </summary>
    private readonly Dictionary<uint, ShipController> _shipControllers = new();
    // ===== Ship interactions + caches (added by lot 3A.6.1, consumed in 3A.6.3+) =====
    private readonly ShipShoreInteraction _shipShore = new();
    private readonly ShipShipInteraction _shipShip = new();
    private readonly ShipDoodadInteraction _shipDoodad = new();
    private readonly ShipStaticBarrierInteraction _shipStaticBarriers = new();
    private readonly ShipCliffInteraction _shipCliff = new();

    /// <summary>Physics-thread loop counter for throttling per-ship cache rebuilds.</summary>
    private ulong _physicsLoopIndex;

    /// <summary>Last loop index and XY at which water/terrain cache was rebuilt (per ship slave id).</summary>
    private readonly Dictionary<uint, (ulong Loop, Vector2 Xy)> _waterLandCacheStamp = new();
    // ===== end Ship interactions + caches =====

    private readonly ConcurrentQueue<Action> _pendingActions = new();

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _worldLock = new();
    private readonly List<RigidBody> _bodies = [];
    public List<RigidBody> VoxelObjects { get; init; } = [];
    public List<RigidBody> BrushObjects { get; init; } = [];

    /// <summary>
    /// Used heightmap tester, saved so it can be edited later
    /// </summary>
    public HeightmapTester WorldHeightMapTester { get; set; }

    /// <summary>
    /// Initialize the Physics engine and creates the ocean water body
    /// </summary>
    public void Initialize()
    {
        // Assume 8k items max / cell in the world
        var cap = new Jitter2.World.Capacity()
        {
            BodyCount = 0x2000 * SimulationWorld.Template.CellX * SimulationWorld.Template.CellY,
            ContactCount = 0x4000 * SimulationWorld.Template.CellX * SimulationWorld.Template.CellY,
        };
        PhysWorld = new Jitter2.World(cap);
        PhysWorld.Gravity = new JVector(0, -9.81f, 0);

        Buoyancy = new Buoyancy(PhysWorld)
        {
            FluidBox = new JBoundingBox(
                new JVector(0, 0, 0), // Bottom
                new JVector(SimulationWorld.Template.CellX * WorldManager.CELL_SIZE,
                    SimulationWorld.Template.OceanLevel,
                    SimulationWorld.Template.CellY * WorldManager.CELL_SIZE) // Surface
            )
        };
        Buoyancy.UseOwnFluidArea(CustomWater);

        Logger.Info($"{SimulationWorld.Template.Name} initialized.");
    }

    /// <summary>
    /// Create terrain data for the physics world
    /// </summary>
    public void InitializeTerrain()
    {
        // Add terrain shape based on height map
        // if (SimulationWorld.Id != WorldManager.DefaultInstanceId) { return; }

        Logger.Debug($"{SimulationWorld.Template.Name} initializing terrain.");
        try
        {
            // If the template hasn't been loaded yet, then load the data into the template
            if (SimulationWorld.Template.PhysicsHeightMap == null)
            {
                // Get needed size
                var dataX = SimulationWorld.Template.CellX * WorldManager.CELL_HMAP_RESOLUTION;
                var dataZ = SimulationWorld.Template.CellY * WorldManager.CELL_HMAP_RESOLUTION;

                // Create the arrays
                var hmapTerrain = new float[dataX, dataZ];
                var hmapMaterials = new byte[dataX, dataZ];

                // Create related PhysicsHeightMap
                var heightmap = new Heightmap(ref hmapTerrain, ref hmapMaterials);
                SimulationWorld.Template.PhysicsHeightMap = heightmap;
                WorldHeightMapTester = new HeightmapTester(SimulationWorld.Template.PhysicsHeightMap);

                if (AppConfiguration.Instance.World.PreLoadTerrain)
                {
                    Logger.Debug(
                        $"Loading {SimulationWorld} heightmap data of {SimulationWorld.Template.CellX * SimulationWorld.Template.CellY} cells");

                    // Read the data
                    var cellCountMax = SimulationWorld.Template.CellX * SimulationWorld.Template.CellY * 1f;
                    var cellCount = 0;
                    for (var cellY = 0; cellY < SimulationWorld.Template.CellY; cellY++)
                    {
                        for (var cellX = 0; cellX < SimulationWorld.Template.CellX; cellX++)
                        {
                            cellCount++;
                            var cell = SimulationWorld.Template.Cells[cellX, cellY];
                            cell.VerifyCellLoaded();
                            if (!cell.Loaded)
                                continue; // ignore if not loaded

                            // Commented out as VerifyCellLoaded will already populate this
                            /*
                            var cellXOff = (cellX * WorldManager.CELL_HMAP_RESOLUTION);
                            var cellYOff = (cellY * WorldManager.CELL_HMAP_RESOLUTION);
                            for (var inX = 0; inX < WorldManager.CELL_HMAP_RESOLUTION; inX++)
                            for (var inY = 0; inY < WorldManager.CELL_HMAP_RESOLUTION; inY++)
                            {
                                var x = cellXOff + inX;
                                var y = cellYOff + inY;
                                hmapTerrain[x, y] = cell.GetHeightMapDataInCell(inX, inY);
                                hmapMaterials[x, y] = cell.GetMaterialsDataInCell(inX, inY);
                            }
                            */

                            if (cellCount % 16 == 0)
                                Logger.Debug(
                                    $"Loading {SimulationWorld} heightmap data {(cellCount / cellCountMax * 100f):F0}%");
                        }
                    }

                }
            }

            // Add heightmap tester object into this instance's physics engine
            PhysWorld.BroadPhaseFilter = new HeightmapDetection(PhysWorld, WorldHeightMapTester);
            PhysWorld.DynamicTree.AddProxy(WorldHeightMapTester, false);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        Logger.Info($"{SimulationWorld.Template.Name} initialized terrain.");
    }

    /// <summary>
    /// Starts the Physics processing thread
    /// </summary>
    public void StartPhysics()
    {
        ThreadRunning = true;
        _thread = new Thread(PhysicsThread) { Name = "Physics-" + SimulationWorld };
        _thread.Start();
    }

    /// <summary>
    /// Handle physics loop
    /// </summary>
    private void PhysicsThread()
    {
        try
        {
            Logger.Debug($"Start: {Thread.CurrentThread.Name}, targetting {TargetPhysicsTps} TPS");

            var lastTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var accumulatedTime = TimeSpan.Zero;
            Thread.Sleep((int)TargetPhysicsTickTime);

            while (ThreadRunning)
            {
                // Reduce tick speed to 1/4th when loading terrain objects
                var loadBalanceMultiplier = SimulationWorld.WorldCellTerrainLoadingTask == null ? 1f : 5f;
                var targetStepTime = TimeSpan.FromSeconds(TargetPhysicsTickTime * loadBalanceMultiplier);
                var currentTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var timeSinceLastTick = currentTick - lastTick;
                accumulatedTime += timeSinceLastTick;
                var timeToNextStep = lastTick + targetStepTime - currentTick;
                // Only sleep if needed, otherwise, directly continue
                if (timeToNextStep.TotalMilliseconds > 1)
                {
                    Thread.Sleep((int)timeToNextStep.TotalMilliseconds);
                }
                else if (timeToNextStep.TotalMilliseconds < -TargetPhysicsTps)
                {
                    // If it's taking more than double the expected time, toss a warning if not loading terrain objects
                    if (SimulationWorld.WorldCellTerrainLoadingTask == null)
                    {
                        Logger.Warn($"Physics thread is running slow in {SimulationWorld} at {timeSinceLastTick.TotalMilliseconds:F1} / {targetStepTime.TotalMilliseconds:F1} ms ({PhysWorld.RigidBodies.Count} RigidBodies)");                        
                    }
                    // If it's still loading, only toss a DEBUG warning if it's taking at least 10 times long than a normal tick
                    else if (timeToNextStep.TotalMilliseconds < (TargetPhysicsTps * -9f))
                    {
                        Logger.Debug($"Physics thread is running slow in {SimulationWorld} at {timeSinceLastTick.TotalMilliseconds:F1} / {targetStepTime.TotalMilliseconds:F1} ms ({PhysWorld.RigidBodies.Count} RigidBodies)");
                    }
                }

                var physicsTotalDelta = TimeSpan.FromMilliseconds(Environment.TickCount64) - lastTick;
                lastTick = currentTick;

                // 1. Process pending add/remove actions
                while (_pendingActions.TryDequeue(out var action)) { action(); }

                if (SimulationWorld.WorldCellTerrainLoadingTask != null)
                {
                    // Skip physics if loading data
                    // continue;
                }
                
                List<(RigidBody body, JVector vel, bool moving)> snapshot = [];

                lock (_worldLock)
                {

                    // 2. Take snapshot of bodies for state synchronization
                    foreach (var body in _bodies)
                    {
                        if (body == null) { continue; }

                        var vel = body.Velocity;
                        var moving = vel.LengthSquared() > 0.001f;
                        snapshot.Add((body, vel, moving));
                    }

                    // 3. Step the physics world
                    // Potentially step multiple times to catch up if we were running behind.
                    PhysWorld.Step((float)physicsTotalDelta.TotalSeconds, false);

                    // 4. Sync positions and broadcast outside lock
                    // body, velocity, isMoving
                    foreach (var (body, _, _) in snapshot)
                    {
                        /*
                        if (body.Tag is Npc npc)
                        {
                            // Update transform
                            //UpdateNpcTransform(npc, velocity, isMoving);

                            // Update avoidance controller
                            //npc.AvoidanceController.Update(0.01f);
                        }
                        */

                        if (body.Tag is not Slave slave)
                            continue;

                        try
                        {
                            if (slave.Transform.WorldId != SimulationWorld.Id)
                                continue;

                            // Skip simulation if still summoning
                            if (slave.SpawnTime.AddSeconds(slave.Template.PortalTime) > DateTime.UtcNow)
                                continue;

                            // Skip simulation if no rigidbody applied to slave
                            if (!body.IsActive)
                                continue;

                            // TODO: move this
                            var underPos = slave.Transform.World.Position + Vector3.UnitZ *
                                (slave.ShipController?.ShipModel.MassBoxSizeZ ?? 1f) / -2f * slave.Scale;
                            if (SimulationWorld.Water.IsWater(underPos, out var flowDirection))
                            {
                                if (flowDirection.Length() > 0f)
                                {
                                    // We are in moving water, apply force
                                    // var multiplier = slave.RigidBody.Mass / TargetPhysicsTickTime;
                                    // slave.RigidBody.AddForce(new JVector(flowDirection.X * multiplier, flowDirection.Z * multiplier, flowDirection.Y * multiplier));
                                    slave.RigidBody.Position += new JVector(
                                        flowDirection.X * (float)physicsTotalDelta.TotalSeconds,
                                        flowDirection.Z * (float)physicsTotalDelta.TotalSeconds,
                                        flowDirection.Y * (float)physicsTotalDelta.TotalSeconds);
                                }
                            }

                            if (_shipControllers.TryGetValue(slave.Id, out var boat))
                            {
                                // Create floor/surface cache
                                slave.CreateWaterAndLandSurfaceCache();
                                // Sync transform
                                SyncTransformWithRigidBody(slave);
                                // Do physics tick
                                BoatPhysicsTick(slave, physicsTotalDelta);
                                // Check if we collided
                                CheckLandCollisions(slave, physicsTotalDelta);
                                // Update Controls
                                boat.ApplyForceAndTorque(slave, physicsTotalDelta);
                                SendUpdatedMovementData(slave, slave.RigidBody, physicsTotalDelta);
                            }
                        }
                        catch (Exception slaveException)
                        {
                            // Put a separate catch here to catch individual errors without it breaking all the physics in this world 
                            Logger.Error(
                                $"PhysicsThread Error on Slave {slave.Id} {slave.Name} ({slave.ObjId}): {slaveException.Message}\n{slaveException.StackTrace}");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"PhysicsThread Error: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            Logger.Debug($"PhysicsThread End: {Thread.CurrentThread.Name}");
        }
    }

    /// <summary>
    /// Copies physics engine's positions back to game server's positions
    /// </summary>
    /// <param name="slave"></param>
    private void SyncTransformWithRigidBody(Slave slave)
    {
        var slaveRigidBody = slave.RigidBody;
        var xDelta = slaveRigidBody.Position.X - slave.Transform.World.Position.X;
        var yDelta = slaveRigidBody.Position.Z - slave.Transform.World.Position.Y;
        var zDelta = slaveRigidBody.Position.Y - slave.Transform.World.Position.Z;
        //if (zDelta < -3)
        //{
        //    slaveRigidBody.Position = slaveRigidBody.Position with { Y = slave.Transform.World.Position.Z };
        //    zDelta = 0;
        //    Logger.Info($"SyncTransformWithRigidBody {slave.Name} -> {SimulationWorld.Name}, _waterLevel={DefaultWaterLevel}, OceanLevel={SimulationWorld.OceanLevel}, slave.Position.Z={slave.Transform.World.Position.Z}");
        //}

        slave.Transform.Local.Translate(xDelta, yDelta, zDelta);
        var rotation = slaveRigidBody.Orientation;
        slave.Transform.Local.ApplyFromQuaternion(rotation.X, rotation.Z, rotation.Y, rotation.W);
    }

    /// <summary>
    /// Adds a ship to physics engine
    /// </summary>
    /// <param name="slave"></param>
    public void AddShip(Slave slave)
    {
        var shipModel = ModelManager.Instance.GetShipModel(slave.ModelId);
        if (shipModel == null || shipModel.Mass <= 0)
        {
            Logger.Error($"Invalid ship model for slave {slave.Name}");
            return;
        }

        var pos = new JVector(slave.Transform.World.Position.X, slave.Transform.World.Position.Z,
            slave.Transform.World.Position.Y);
        var rot = JQuaternion.CreateRotationY(slave.Transform.World.Rotation.Z);
        //                                     Width                   Length                  Height
        // var dimensions = new JVector(shipModel.MassBoxSizeX, shipModel.MassBoxSizeY, shipModel.MassBoxSizeZ);
        var ctrl = new ShipController(PhysWorld, shipModel);

        ctrl.Build(initialPosition: pos, initialOrientation: rot);

        _shipControllers[slave.Id] = ctrl;
        slave.RigidBody = ctrl.Hull;
        slave.RigidBody.Tag = slave;
        slave.ShipController = ctrl;

        EnqueueAddBody(slave.RigidBody);
        Buoyancy.AddForRectangularParallelepiped(slave.RigidBody, 3);

        Logger.Debug($"AddShip {slave.Name} -> {SimulationWorld.Template.Name}");
    }

    /// <summary>
    /// Removes a ship from the physics engine
    /// </summary>
    /// <param name="slave"></param>
    public void RemoveShip(Slave slave)
    {
        if (slave.RigidBody == null) return;

        var rigidBody = slave.RigidBody;
        rigidBody.SetActivationState(false);
        EnqueueRemoveBody(rigidBody);
        PhysWorld.Remove(rigidBody);
        Buoyancy.Remove(rigidBody);
        slave.RigidBody = null;

        Logger.Debug($"RemoveShip {slave.Name} <- {SimulationWorld.Template.Name}");
    }

    /// <summary>
    /// Handles physics tick for a ship 
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="deltaTime"></param>
    private void BoatPhysicsTick(Slave slave, TimeSpan deltaTime)
    {
        var shipModel = slave.ShipController?.ShipModel;
        if (shipModel == null) return;

        _shipShore.ApplyOnLandPhysics(slave, deltaTime);

        // Check if the ship has a driver
        var hasDriver = slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver);
        if (hasDriver)
        {
            // Smooth toward client input in float space, then round - avoids sbyte stair-stepping on rudder animation.
            const float SmoothingFactor = 0.12f;
            slave.ThrottleSmoothed += (slave.ThrottleRequest - slave.ThrottleSmoothed) * SmoothingFactor;
            slave.SteeringSmoothed += (slave.SteeringRequest - slave.SteeringSmoothed) * SmoothingFactor;
            slave.Throttle = (sbyte)Math.Clamp((int)Math.Round(slave.ThrottleSmoothed), -128, 127);
            slave.Steering = (sbyte)Math.Clamp((int)Math.Round(slave.SteeringSmoothed), -128, 127);
        }
        else
        {
            // If there is no driver, we reset the control
            slave.ThrottleRequest = 0;
            slave.SteeringRequest = 0;
            slave.Throttle = 0;
            slave.Steering = 0;
            slave.ThrottleSmoothed = 0f;
            slave.SteeringSmoothed = 0f;
        }
    }

    /// <summary>
    /// Update ship's movement data and broadcasts it 
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="rigidBody"></param>
    private void SendUpdatedMovementData(Slave slave, RigidBody rigidBody, TimeSpan deltaTime)
    {
        var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
        moveType.UseSlaveBase(slave);

        // Get current rotation of the ship
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));

        // Visual-only bank (ship leans into turns). Applied to replicated rotation, not physics.
        var maxBankDeg = ComputeVisualMaxBankDegFromShipModel(slave.ShipController?.ShipModel, slave.Scale);
        const float bankResponse = 7.5f;
        var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
        var maxBankRad = maxBankDeg.DegToRad();
        var yawRate = rigidBody.AngularVelocity.Y;
        var horizSpeed = MathF.Sqrt(
            rigidBody.Velocity.X * rigidBody.Velocity.X +
            rigidBody.Velocity.Z * rigidBody.Velocity.Z);
        var speedFactor = Math.Clamp(horizSpeed / 2.5f, 0f, 1f);
        var targetBank = Math.Clamp(-yawRate * 0.9f, -maxBankRad, maxBankRad) * speedFactor;
        var a = 1f - MathF.Exp(-bankResponse * dt);
        slave.BankAngle += (targetBank - slave.BankAngle) * a;

        _shipShore.UpdateVisualGroundPitch(slave, rigidBody, deltaTime);

        var wavePitchRad = ComputeVisualWavePitchOnWater(slave, rigidBody, dt);

        // Replication smoothing for clients only; rigid body and transform stay physics-accurate below.
        const float repLambdaHorizFree = 22f;
        const float repLambdaHorizContact = 11f;
        const float repLambdaVertFree = 8f;
        const float repLambdaVertContact = 4f;
        const float repLambdaBankFree = 4f;
        const float repLambdaBankContact = 2f;
        var rep = slave.ShipController!.Replication;
        var repLambdaH = rep.ContactHoldTicks > 0 ? repLambdaHorizContact : repLambdaHorizFree;
        var repLambdaV = rep.ContactHoldTicks > 0 ? repLambdaVertContact : repLambdaVertFree;
        var repLambdaB = rep.ContactHoldTicks > 0 ? repLambdaBankContact : repLambdaBankFree;
        var repAlphaH = 1f - MathF.Exp(-repLambdaH * dt);
        var repAlphaV = 1f - MathF.Exp(-repLambdaV * dt);
        var repAlphaB = 1f - MathF.Exp(-repLambdaB * dt);

        var tgtX = rigidBody.Position.X;
        var tgtY = rigidBody.Position.Z;
        var tgtZ = rigidBody.Position.Y;
        var tgtVx = rigidBody.Velocity.X;
        var tgtVy = rigidBody.Velocity.Z;
        var tgtVz = rigidBody.Velocity.Y;

        if (!rep.Seeded)
        {
            rep.PosX = tgtX;
            rep.PosY = tgtY;
            rep.PosZ = tgtZ;
            rep.VelPx = tgtVx;
            rep.VelPy = tgtVy;
            rep.VelPz = tgtVz;
            rep.BankSmoothed = slave.BankAngle;
            rep.GroundPitchSmoothed = slave.GroundPitchAngle;
            rep.Seeded = true;
        }
        else
        {
            rep.PosX += (tgtX - rep.PosX) * repAlphaH;
            rep.PosY += (tgtY - rep.PosY) * repAlphaH;
            rep.PosZ += (tgtZ - rep.PosZ) * repAlphaV;
            rep.VelPx += (tgtVx - rep.VelPx) * repAlphaH;
            rep.VelPy += (tgtVy - rep.VelPy) * repAlphaH;
            rep.VelPz += (tgtVz - rep.VelPz) * repAlphaV;
            rep.BankSmoothed += (slave.BankAngle - rep.BankSmoothed) * repAlphaB;
            rep.GroundPitchSmoothed += (slave.GroundPitchAngle - rep.GroundPitchSmoothed) * repAlphaV;
        }

        var bankedRpy = (rpy.Item1, rpy.Item2 + rep.BankSmoothed, rpy.Item3 + rep.GroundPitchSmoothed + wavePitchRad);

        var (rotZ, rotY, rotX) = MathUtil.GetSlaveRotationFromDegrees(bankedRpy.Item1, bankedRpy.Item2, bankedRpy.Item3);
        moveType.RotationX = rotX;
        moveType.RotationY = rotY;
        moveType.RotationZ = rotZ;

        moveType.X = rep.PosX;
        moveType.Y = rep.PosY;
        moveType.Z = rep.PosZ;

        moveType.AngVelX = rigidBody.AngularVelocity.X;
        moveType.AngVelY = rigidBody.AngularVelocity.Z;
        moveType.AngVelZ = rigidBody.AngularVelocity.Y;

        const int velMultiplier = 2048;
        moveType.VelX = (short)(rep.VelPx * velMultiplier);
        moveType.VelY = (short)(rep.VelPy * velMultiplier);
        moveType.VelZ = (short)(rep.VelPz * velMultiplier);

        // Apply new Location/Rotation to GameObject
        slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
        slave.Transform.Local.ApplyFromQuaternion(rigidBody.Orientation);
        slave.Transform.Local.SetRotation(
            slave.Transform.Local.Rotation.X,
            slave.Transform.Local.Rotation.Y + rep.BankSmoothed,
            slave.Transform.Local.Rotation.Z + rep.GroundPitchSmoothed + wavePitchRad);

        // Send the packet
        slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);

        // Update all to main Slave and it's children
        slave.Transform.FinalizeTransform();

        if (rep.ContactHoldTicks > 0)
            rep.ContactHoldTicks--;
    }

    /// <summary>
    /// Apply land collision between the ship and the expected terrain
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="deltaTime"></param>
    private void CheckLandCollisions(Slave slave, TimeSpan deltaTime)
    {
        if (slave.ShipController?.ShipModel is null)
            return;

        var boatBottom = slave.RigidBody.Position.Y;
        //Logger.Debug($"Slave: {slave.Name}, floor: {floor:F1}, boatBottom: {boatBottom:F1}, boxSize: {boxSize}");

        if (slave.CachedWaterSurface >= slave.CachedFloorLevel)
        {
            return;
        }

        var penetration = slave.CachedFloorLevel - boatBottom;
        slave.RigidBody.Position +=
            new JVector(0, penetration, 0); // Move the boat upwards to put the center level with the floor
        var collisionForce = PhysWorld.Gravity * -1f;
        slave.RigidBody.AddForce(collisionForce);

        // Gradually reduce speed
        var collisionDamping = 0.9f;
        slave.RigidBody.Velocity *= collisionDamping;
        slave.RigidBody.AngularVelocity *= collisionDamping;

        // Logger.Debug($"Land Collision detected. Boat adjusted position: {slave.RigidBody.Position}, boat penetration depth: {penetration}");
    }

    /// <summary>
    /// Stops the physics engine from running its update loop
    /// </summary>
    public void Stop()
    {
        ThreadRunning = false;
    }

    public void Dispose() => PhysWorld?.Dispose();

    /// <summary>
    /// Helper function to check water bodies
    /// </summary>
    /// <param name="area"></param>
    /// <returns></returns>
    internal bool CustomWater(ref JVector area)
    {
        return SimulationWorld?.IsWater(new Vector3(area.X, area.Z, area.Y), out _) ??
               area.Y <= (SimulationWorld?.Template.OceanLevel ?? DefaultWaterLevel);
    }

    /// <summary>
    /// Enqueues an NPC body to be added in the next physics step.
    /// </summary>
    // ===== Ship visual helpers (added by lot 3A.6.2, consumed in 3A.6.3+) =====
    private static bool TryGetShipMassBoxWaterlineExtents(ShipModelV1 model, float scale,
        out float length, out float beam, out float height, out float mass)
    {
        if (model == null)
        {
            length = beam = height = mass = 0f;
            return false;
        }

        var s = MathF.Max(scale, 0.01f);
        var hx = model.MassBoxSizeX * s;
        var hy = model.MassBoxSizeY * s;
        length = MathF.Max(MathF.Max(hx, hy), 0.25f);
        beam = MathF.Max(MathF.Min(hx, hy), 0.25f);
        height = MathF.Max(model.MassBoxSizeZ * s, 0.15f);
        mass = MathF.Max(model.Mass, 10f);
        return true;
    }

    /// <summary>
    /// Visual wave pitch amplitude (rad) and angular frequency from hull length/mass.
    /// Longer/heavier -> smaller amp, slightly slower cycle; short/light dinghies stay closer to legacy +/-3 deg / 0.06 Hz.
    /// </summary>
    private static void GetVisualWavePitchModelFactors(ShipModelV1 model, float scale, out float maxAmpRad, out float omega)
    {
        const float baseDeg = 3f;
        const float baseHz = 0.06f;
        if (!TryGetShipMassBoxWaterlineExtents(model, scale, out var length, out _, out _, out var mass))
        {
            maxAmpRad = baseDeg.DegToRad();
            omega = 2f * MathF.PI * baseHz;
            return;
        }

        const float refLength = 14f;
        const float refMass = 85000f;

        var lenRatio = Math.Clamp(refLength / length, 0.35f, 2.5f);
        var massRatio = Math.Clamp(MathF.Sqrt(refMass / mass), 0.45f, 2.2f);
        var ampMul = MathF.Pow(lenRatio, 0.38f) * MathF.Pow(massRatio, 0.28f);
        var maxDeg = Math.Clamp(baseDeg * ampMul, 1.1f, 5.5f);
        maxAmpRad = maxDeg.DegToRad();

        var freqMul = MathF.Pow(Math.Clamp(length / refLength, 0.5f, 2.2f), -0.18f);
        var hz = Math.Clamp(baseHz * freqMul, 0.042f, 0.078f);
        omega = 2f * MathF.PI * hz;
    }

    /// <summary>
    /// Visual-only pitch oscillation on open water. Does not affect rigid body.
    /// </summary>
    private static float ComputeVisualWavePitchOnWater(Slave slave, RigidBody rigidBody, float dt)
    {
        var grounded = slave.CachedFloorLevel > slave.CachedWaterSurface || slave.GroundContactLatched;
        if (grounded)
            return 0f;

        var submerged = MathF.Max(0f, slave.CachedWaterSurface - rigidBody.Position.Y);
        const float submergedForFullAmp = 0.32f;
        var depthMul = Math.Clamp(submerged / submergedForFullAmp, 0f, 1f);
        if (depthMul <= 0f)
            return 0f;

        GetVisualWavePitchModelFactors(slave.ShipController?.ShipModel, slave.Scale, out var maxAmpRad, out var omega);
        slave.WavePitchPhase += omega * dt;
        // keep phase bounded
        if (slave.WavePitchPhase > MathF.PI * 4000f)
            slave.WavePitchPhase -= MathF.PI * 4000f;

        var phaseOff = (slave.ObjId & 511) * 0.211f;
        return MathF.Sin(slave.WavePitchPhase + phaseOff) * maxAmpRad * depthMul;
    }

    /// <summary>
    /// Max visual bank (degrees) for turn lean from ship_models mass box and mass.
    /// </summary>
    private static float ComputeVisualMaxBankDegFromShipModel(ShipModelV1 model, float scale)
    {
        if (!TryGetShipMassBoxWaterlineExtents(model, scale, out var length, out var beam, out var height, out var mass))
            return 8f;

        const float refLength = 14f;
        const float refBeam = 1.5f;
        const float refHeight = 16f;
        const float refMass = 85000f;
        const float baseDeg = 9f;

        var lengthFactor = MathF.Pow(Math.Clamp(length / refLength, 0.35f, 2.8f), 0.22f);
        var beamFactor = MathF.Pow(Math.Clamp(refBeam / beam, 0.65f, 1.6f), 0.28f);
        var massFactor = MathF.Pow(Math.Clamp(refMass / mass, 0.2f, 4f), 0.18f);
        var heightFactor = MathF.Pow(Math.Clamp(refHeight / height, 0.5f, 2f), 0.12f);

        var deg = baseDeg * lengthFactor * beamFactor * massFactor * heightFactor;
        return Math.Clamp(deg, 5f, 14f);
    }
    // ===== end Ship visual helpers =====

    private void EnqueueAddBody(RigidBody body)
    {
        if (body == null) return;
        _pendingActions.Enqueue(() =>
        {
            _bodies.Add(body);
        });
    }

    /// <summary>
    /// Enqueues an NPC body to be removed in the next physics step.
    /// </summary>
    private void EnqueueRemoveBody(RigidBody body)
    {
        if (body == null) return;
        _pendingActions.Enqueue(() =>
        {
            _bodies.Remove(body);
        });
    }

    /// <summary>
    /// Gets game angle Roll from physics engine JMatrix
    /// </summary>
    /// <param name="orientation"></param>
    /// <returns></returns>
    internal static float GetRollAngle(JMatrix orientation)
    {
        var yawPitchRoll = GetYawPitchRollFromJMatrix(orientation);
        return yawPitchRoll.Item2; // Roll angle in radians
    }

    /// <summary>
    /// Gets angle YPR from physics engine JMatrix
    /// </summary>
    /// <param name="mat"></param>
    /// <returns></returns>
    internal static (float, float, float) GetYawPitchRollFromJMatrix(JMatrix mat)
    {
        return MathUtil.GetYawPitchRollFromQuat(JMatrixToQuaternion(mat));
    }

    /// <summary>
    /// Convert JMatrix to game Quaternion 
    /// </summary>
    /// <param name="matrix"></param>
    /// <returns></returns>
    internal static Quaternion JMatrixToQuaternion(JMatrix matrix)
    {
        var jq = JQuaternion.CreateFromMatrix(matrix);

        return new Quaternion { X = jq.X, Y = jq.Y, Z = jq.Z, W = jq.W };
    }

    public void UpdateHeightMapFromCellBody(WorldCell cell)
    {
        if (WorldHeightMapTester == null)
        {
            return;
        }

        // Copy over cell's data
        for (var inY = 0; inY < WorldManager.CELL_HMAP_RESOLUTION; inY++)
        {
            for (var inX = 0; inX < WorldManager.CELL_HMAP_RESOLUTION; inX++)
            {
                var x = (cell.CellX * WorldManager.CELL_HMAP_RESOLUTION) + inX;
                var y = (cell.CellY * WorldManager.CELL_HMAP_RESOLUTION) + inY;
                WorldHeightMapTester.Heightmap.Heights[x, y] = cell.GetHeightMapDataInCell(inX, inY);
                WorldHeightMapTester.Heightmap.Materials[x, y] = cell.GetMaterialsDataInCell(inX, inY);
            }
        }

        Logger.Trace($"Post-Loaded {SimulationWorld} Cell {cell.CellX}, {cell.CellY}");
    }

/*
    /// <summary>
    /// Adds Triangle data to a list of JTriangles to form a quad
    /// </summary>
    /// <param name="triangles">List of triangles to add to</param>
    /// <param name="baseOffset">Base offset to apply to added vertex points</param>
    /// <param name="tl">Top-Left point</param>
    /// <param name="tr">Top-Right point</param>
    /// <param name="bl">Bottom-Left point</param>
    /// <param name="br">Bottom-Right point</param>
    /// <param name="holeTl">Is Top-left a hole</param>
    /// <param name="holeTr">Is Top-Right a hole</param>
    /// <param name="holeBl">Is Bottom-Left a hole</param>
    /// <param name="holeBr">Is Bottom-Right a hole</param>
    private void AddQuad(List<JTriangle> triangles, JVector baseOffset, JVector tl, JVector tr, JVector bl, JVector br, bool allowHoles, bool holeTl, bool holeTr, bool holeBl, bool holeBr)
    {
        // Don't add the triangle if even one of its sides is marked as a hole vertex
        // Jitter2 goes counter-clock-wise for triangles to get the Normal pointing up
        if (!allowHoles || (!holeTl && !holeBr && !holeTr))
        {
            triangles.Add(new(baseOffset + tl, baseOffset + br, baseOffset + tr));
        }

        if (!allowHoles || (!holeBr && !holeTl && !holeBl))
        {
            triangles.Add(new(baseOffset + br, baseOffset + tl, baseOffset + bl));
        }
    }

    /// <summary>
    /// Updates heightmap data with the data from the provided WorldCell using the Hmap nodes
    /// </summary>
    /// <param name="cell"></param>
    public void AddHeightMapMeshFromCellBody(WorldCell cell)
    {
        var cellBody = PhysWorld.CreateRigidBody();
        cellBody.Tag = cell;
        cellBody.AffectedByGravity = false;
        cellBody.Position = new JVector(cell.BoundingBox.Min.X, 0f, cell.BoundingBox.Min.Z);
        // Load all nodes data of this cell into one JTriangle list
        var cellTriangles = new List<JTriangle>();
        foreach (var nodeCell in cell.LoadedHmap.Nodes)
        {
            var nodeOffset = new JVector(nodeCell.BoxHeightmap.Min.X % WorldManager.CELL_SIZE, 0, nodeCell.BoxHeightmap.Min.Y % WorldManager.CELL_SIZE);
            if (nodeCell.NodeSize <= 1)
            {
                // Add flat surface?
                continue;
            }

            // Loop the heightmap data to generate a list of triangles that will create the surface
            for (ushort y = 0; y < nodeCell.NodeSize - 1; y++)
            for (ushort x = 0; x < nodeCell.NodeSize - 1; x++)
            {
                var xPlusOne = (ushort)(x + 1);
                var yPlusOne = (ushort)(y + 1);
                var posX1 = MathF.Round((nodeCell.BoxHeightmap.Max.X - nodeCell.BoxHeightmap.Min.X) / (nodeCell.NodeSize - 1) * x,2);
                var posY1 = MathF.Round((nodeCell.BoxHeightmap.Max.Y - nodeCell.BoxHeightmap.Min.Y) / (nodeCell.NodeSize - 1) * y,2);
                var posX2 = MathF.Round((nodeCell.BoxHeightmap.Max.X - nodeCell.BoxHeightmap.Min.X) / (nodeCell.NodeSize - 1) * xPlusOne,2);
                var posY2 = MathF.Round((nodeCell.BoxHeightmap.Max.Y - nodeCell.BoxHeightmap.Min.Y) / (nodeCell.NodeSize - 1) * yPlusOne,2);
                AddQuad(cellTriangles, nodeOffset,
                    new JVector(posX1, (nodeCell.GetHeight(x, y)), posY1), // TL
                    new JVector(posX2, (nodeCell.GetHeight(xPlusOne, y)), posY1), // TR
                    new JVector(posX1, (nodeCell.GetHeight(x, yPlusOne)), posY2), // BL
                    new JVector(posX2, (nodeCell.GetHeight(xPlusOne, yPlusOne)), posY2), // BL
                    nodeCell.NodeHasHoles > 0,
                    (nodeCell.RawDataByIndex(x, y) & NodeCell.HeightMapMaterialBits) == NodeCell.HeightMapMaterialHole,
                    (nodeCell.RawDataByIndex(xPlusOne, y) & NodeCell.HeightMapMaterialBits) == NodeCell.HeightMapMaterialHole,
                    (nodeCell.RawDataByIndex(x, yPlusOne) & NodeCell.HeightMapMaterialBits) == NodeCell.HeightMapMaterialHole,
                    (nodeCell.RawDataByIndex(xPlusOne, yPlusOne) & NodeCell.HeightMapMaterialBits) == NodeCell.HeightMapMaterialHole
                );
            }
        }

        // Load triangles into a mesh
        var cellFloorMesh = new TriangleMesh(cellTriangles);
        // Add all the Mesh's triangles as shapes to the RigidBody of the floor
        cellBody.AddShape(new TriangleShape(cellFloorMesh, 0), false); // Has no mass
        // Mark the floor as static
        cellBody.IsStatic = true;

#if EXPORT_TERRAIN_ON_LOAD
        // Save Triangles as Obj file
        var sb = new StringBuilder();
        sb.AppendLine($"# {cell.Template.Name} Cell {cell.CellX}-{cell.CellY} data");
        sb.AppendLine($"o Cell_{cell.CellX}_{cell.CellY}");
        // Add vertices
        foreach (var vertex in cellFloorMesh.Vertices)
        {
            var v = vertex.ToVector();
            sb.AppendLine($"v {v.X:F2} {v.Y:F2} {v.Z:F2}");
        }
        // Add faces
        foreach (var triangle in cellFloorMesh.Indices)
        {
            sb.AppendLine($"f {triangle.IndexA+1} {triangle.IndexB+1} {triangle.IndexC+1}");
        }

        var objFileName = Path.Combine(FileManager.AppPath, $"{cell.Template.Name}_Cell_{cell.CellX:00}_{cell.CellY:00}.obj");
        File.WriteAllText(objFileName, sb.ToString());
        Logger.Warn($"Wrote terrain export: {objFileName}");
#endif
    }
*/


    /// <summary>
    /// Adds water bodies from the world objects.dat data
    /// </summary>
    public void InitializeWater()
    {
        SimulationWorld.Water.OceanLevel = SimulationWorld.Template.OceanLevel;
        for (var y = 0; y < SimulationWorld.Template.CellY; y++)
        for (var x = 0; x < SimulationWorld.Template.CellX; x++)
        {
            var cell = SimulationWorld.Template.GetCell(x, y);
            if (cell == null)
                continue;
            SimulationWorld.Water.AddFromCellData(cell);
        }
    }

    /// <summary>
    /// Adds several static terrain objects from the objects.dat and visareas.dat files
    /// Adds Voxel terrain
    /// Adds Brush models (buildings/rocks/trees)
    /// </summary>
    /// <param name="worldCell"></param>
    public void AddStaticTerrainVoxels(WorldCell worldCell)
    {
        if (worldCell == null)
            return;

        // Main objects list
        var objectsLoadedCount = 0;
        if (worldCell.LoadedObjectDat != null)
        {
            foreach (var objectData in worldCell.LoadedObjectDat.PrefabsList)
            {
                if (objectData is ObjectDataType6Voxel voxel)
                {
                    if (voxel.Parse() && voxel.MeshReader?.Vertices.Count > 2)
                    {
                        if (AddVoxelTerrain(worldCell, voxel))
                        {
                            objectsLoadedCount++;
                        }
                    }
                }
            }
        }

        // Objects list in visareas
        if (worldCell.LoadedVisAreasDat != null)
        {
            foreach (var objectData in worldCell.LoadedVisAreasDat.PrefabsList)
            {
                if (objectData is ObjectDataType6Voxel voxel)
                {
                    if (voxel.Parse() && voxel.MeshReader?.Vertices.Count > 2)
                    {
                        if (AddVoxelTerrain(worldCell, voxel))
                        {
                            objectsLoadedCount++;
                        }
                    }
                }
            }
        }

        // Logger.Debug($"Loaded {objectsLoadedCount} voxel objects into Cell {worldCell}");
    }

    /// <summary>
    /// Adds several static terrain objects from the objects.dat and visareas.dat files
    /// Adds Voxel terrain
    /// Adds Brush models (buildings/rocks/trees)
    /// </summary>
    /// <param name="worldCell"></param>
    public void AddStaticTerrainObjects(WorldCell worldCell)
    {
        if (worldCell == null)
            return;
        // Only load if enabled
        if (!AppConfiguration.Instance.World.LoadBrushModels)
            return;

        // Main objects list
        var objectsLoadedCount = 0;
        if (worldCell.LoadedObjectDat != null)
        {
            foreach (var objectData in worldCell.LoadedObjectDat.PrefabsList)
            {
                if (objectData is ObjectDataType1Brush brush)
                {
                    if (AddBrushObject(worldCell, brush))
                    {
                        objectsLoadedCount++;
                    }
                }
            }
        }

        // Objects list in visareas
        if (worldCell.LoadedVisAreasDat != null)
        {
            foreach (var objectData in worldCell.LoadedVisAreasDat.PrefabsList)
            {
                if (objectData is ObjectDataType1Brush brush)
                {
                    if (AddBrushObject(worldCell, brush))
                    {
                        objectsLoadedCount++;
                    }
                }
            }
        }

        Logger.Debug($"Loaded {objectsLoadedCount} brush objects into Cell {worldCell}");
    }

    /// <summary>
    /// Adds a voxel object to a Cell
    /// </summary>
    /// <param name="cell"></param>
    /// <param name="brush"></param>
    private bool AddBrushObject(WorldCell cell, ObjectDataType1Brush brush)
    {
        var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);
        // var roughSize = MathUtil.CalculateDistance(brush.StartPos, brush.EndPos, false);
        if (AppConfiguration.Instance.World.LoadBrushMinimumSize > 0f && roughSize < AppConfiguration.Instance.World.LoadBrushMinimumSize)
        {
            // Too small to bother loading (if configured)
            return false;
        }
        var timer = new Stopwatch();
        timer.Start();
        var cellOffset = cell.GetCellWorldOffset().ToJVector();

        RigidBody brushObject = null;

        var modelPathName = string.Empty;
        // Try model first
        var triangleList = new List<JTriangle>();
        if (brush.MaterialId > 0)
        {
            var materialPathName = (
                cell.MaterialListFiles != null && brush.MaterialId < cell.MaterialListFiles.MaterialsList.Count
            )
                ? cell.MaterialListFiles.MaterialsList[brush.MaterialId]
                : string.Empty;

            modelPathName = (
                cell.StatObjsFiles != null && brush.PathId < cell.StatObjsFiles.MaterialList.Count
            )
                ? cell.StatObjsFiles.MaterialList[brush.PathId]
                : string.Empty;

            // Fully ignore nodraw objects
            if ((modelPathName == "game/objects/nodraw") || (materialPathName == "game/objects/nodraw"))
            {
                return false; // Don't draw, so not adding
            }

            if (!string.IsNullOrWhiteSpace(modelPathName) && ClientFileManager.FileExists(modelPathName))
            {
                triangleList = CryEngineModelHelper.MakeModel(modelPathName, materialPathName);
            }

            if (triangleList.Count <= 0)
            {
                // Logger.Warn($"Was unable to load brush model: {modelPathName} for {cell.Template.Name} Cell {cell}");
                return false;
            }
        }

        if (triangleList.Count <= 0)
        {
            return false;
        }

        // Load triangles into a mesh
        var brushMesh = new TriangleMesh(triangleList, ignoreDegenerated: true);
        // Add all the Mesh's triangles as shapes to the RigidBody of the voxel
        var shapesToAdd = new List<RigidBodyShape>();
        for (var i = 0; i < brushMesh.Indices.Length - 2; i++)
        {
            var voxelShape = new TriangleShape(brushMesh, i);
            
            // Apply transform before adding
            var m3X3 = new JMatrix(
                brush.Matrix3X4.M11, brush.Matrix3X4.M31, brush.Matrix3X4.M21,
                brush.Matrix3X4.M13, brush.Matrix3X4.M33, brush.Matrix3X4.M23,
                brush.Matrix3X4.M12, brush.Matrix3X4.M32, brush.Matrix3X4.M22);
            var transformedShape = new TransformedShape(voxelShape, JVector.Zero, m3X3);
            shapesToAdd.Add(transformedShape);
            
            // shapesToAdd.Add(voxelShape);
        }

        lock (_worldLock)
        {
            // Only create the RigidBody if it has a texture and a shape
            brushObject = PhysWorld.CreateRigidBody();
            brushObject.Tag = brush;
            brushObject.AffectedByGravity = false;
            brushObject.IsStatic = true;
            brushObject.Position = cellOffset + new JVector(brush.Matrix3X4.M14, brush.Matrix3X4.M34, brush.Matrix3X4.M24);
            /*
            var m3X3 = new JMatrix(
                brush.Matrix3X4.M11, brush.Matrix3X4.M31, brush.Matrix3X4.M21,
                brush.Matrix3X4.M13, brush.Matrix3X4.M33, brush.Matrix3X4.M23,
                brush.Matrix3X4.M12, brush.Matrix3X4.M32, brush.Matrix3X4.M22);
            brushObject.Orientation = JQuaternion.CreateFromMatrix(m3X3);
            */

            // Add the new shapes
            foreach (var rigidBodyShape in shapesToAdd)
            {
                brushObject.AddShape(rigidBodyShape, false); // Has no mass
            }

            // Apply Cell offset after transform
            // brushObject.Position += cellOffset;

            // Mark the floor as static
            brushObject.IsStatic = true;
            // brushObject.SetActivationState(true);
            EnqueueAddBody(brushObject);

            // Add to reference list
            // Need to save them here for faster heightmap floor collision testing later
            BrushObjects.Add(brushObject);
        }

        // Logger.Trace($"Loaded brush object {modelPathName} into Cell {cell}");
        // TODO: Implement Brush instances instead of create the model every time
        timer.Stop();
        if (timer.ElapsedMilliseconds > 1000)
        {
            Logger.Warn($"Loading object for {cell.Template.Name} Cell {cell} took {timer.ElapsedMilliseconds} ms: {modelPathName}");
        }

        return true;
    }

    /// <summary>
    /// Adds a voxel object to a Cell
    /// </summary>
    /// <param name="cell"></param>
    /// <param name="voxel"></param>
    private bool AddVoxelTerrain(WorldCell cell, ObjectDataType6Voxel voxel)
    {
        var cellOffset = cell.GetCellWorldOffset().ToJVector();

        if (voxel.MeshReader == null)
            return false;

        RigidBody voxelObject = null;
        lock (_worldLock)
        {
            voxelObject = PhysWorld.CreateRigidBody();
            voxelObject.Tag = voxel;
            voxelObject.AffectedByGravity = false;
            voxelObject.Position = new JVector(voxel.Matrix3X4.M14, voxel.Matrix3X4.M34, voxel.Matrix3X4.M24);
        }

        var triangleList = new List<JTriangle>();
        for (var i = 0; i < voxel.MeshReader.Indices.Count - 2; i += 3)
        {
            var i1 = voxel.MeshReader.Indices[i];
            var i2 = voxel.MeshReader.Indices[i + 1];
            var i3 = voxel.MeshReader.Indices[i + 2];
            var v1 = new JVector(voxel.MeshReader.Vertices[i1].X, voxel.MeshReader.Vertices[i1].Z,
                voxel.MeshReader.Vertices[i1].Y);
            var v2 = new JVector(voxel.MeshReader.Vertices[i2].X, voxel.MeshReader.Vertices[i2].Z,
                voxel.MeshReader.Vertices[i2].Y);
            var v3 = new JVector(voxel.MeshReader.Vertices[i3].X, voxel.MeshReader.Vertices[i3].Z,
                voxel.MeshReader.Vertices[i3].Y);

            triangleList.Add(new JTriangle(v1, v2, v3));
        }

        // Load triangles into a mesh
        var voxelMesh = new TriangleMesh(triangleList, ignoreDegenerated: true);
        // Add all the Mesh's triangles as shapes to the RigidBody of the voxel
        lock (_worldLock)
        {
            for (var i = 0; i < voxelMesh.Indices.Length - 2; i++)
            {
                var voxelShape = new TriangleShape(voxelMesh, i);
                
                // Apply transform before adding
                var m3X3 = new JMatrix(
                    voxel.Matrix3X4.M11, voxel.Matrix3X4.M31, voxel.Matrix3X4.M21,
                    voxel.Matrix3X4.M13, voxel.Matrix3X4.M33, voxel.Matrix3X4.M23,
                    voxel.Matrix3X4.M12, voxel.Matrix3X4.M32, voxel.Matrix3X4.M22);
                var transformedShape = new TransformedShape(voxelShape, JVector.Zero, m3X3);
                    voxelObject.AddShape(transformedShape, false); // Has no mass
                
                voxelObject.AddShape(voxelShape, false);
            }

            // Apply Cell offset after transform
            voxelObject.Position += cellOffset;
            /*
            var m3X3 = new JMatrix(
                voxel.Matrix3X4.M11, voxel.Matrix3X4.M31, voxel.Matrix3X4.M21,
                voxel.Matrix3X4.M13, voxel.Matrix3X4.M33, voxel.Matrix3X4.M23,
                voxel.Matrix3X4.M12, voxel.Matrix3X4.M32, voxel.Matrix3X4.M22);
            voxelObject.Orientation = JQuaternion.CreateFromMatrix(m3X3);
            */

            // Mark the floor as static
            voxelObject.IsStatic = true;
            // voxelObject.SetActivationState(true);
            EnqueueAddBody(voxelObject);

            // Add to reference list
            VoxelObjects.Add(voxelObject); // Need to save them here for faster heightmap floor collision testing
        }

        return true;
    }

    /// <summary>
    /// Updates NodeDescriptorList nodes to make them perfectly align with the surface. This should improve movement for AI
    /// </summary>
    public void ReAlignLoadedBaiNodePoints(WorldCell worldCell)
    {
        var timer = new Stopwatch();
        timer.Start();
        //lock (_worldLock)
        {
            foreach (var baseBaiLoader in worldCell.BaiLoader)
            {
                foreach (var netMissionReader in baseBaiLoader.NetMissionReaders)
                {
                    foreach (var (key, node) in netMissionReader.NodeDescriptorList)
                    {
                        var newPos = node.Pos with { Z = SimulationWorld.GetHeight(node.Pos) };
                        node.Pos = newPos;
                    }
                }
            }
        }
        timer.Stop();
        Logger.Debug($"ReAlignLoadedBaiNodePoints for {worldCell.Template.Name} Cell {worldCell.CellX:000}_{worldCell.CellY:000} took {timer.ElapsedMilliseconds} ms");
    }
}
