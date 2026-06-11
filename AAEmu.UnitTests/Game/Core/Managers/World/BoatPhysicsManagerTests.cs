// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
﻿using System.Numerics;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Utils;

using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Core.Managers.World
{
    public class BoatPhysicsManagerTests
    {
        private readonly Moq.Mock<WorldManager> _mockWorldManager;
        private readonly Moq.Mock<WorldInstance> _mockWorld;
        //private readonly Moq.Mock<SlaveManager> _mockSlaveManager;
        private readonly Moq.Mock<Slave> _mockSlave;
        private readonly Moq.Mock<RigidBody> _mockRigidBody;
        //private readonly Moq.Mock<ModelManager> _mockModelManager;
        private readonly PhysicsManager _boatPhysicsManager;
        private WorldTemplate _worldTemplate;

        public BoatPhysicsManagerTests()
        {
            WorldIdManager.Instance.Initialize();
            NpcManager.Instance.Load();
            _worldTemplate = new WorldTemplate()
            {
                CellX = 1,
                CellY = 1,
                Cells = new WorldCell[0,0],
                HeightMaxCoefficient = 0.63,
                HousingZones = [],
                Id = 0,
                MaxHeight = 4096f,
                Name = "main_world",
                OceanLevel = 100f,
                SpawnPosition = null,
                SubZones = [],
                XmlWorld = new XmlWorld { Zones = [] },
                XmlWorldZones = [],
                ZoneKeyByRegions = new uint[1, 1],
                ZoneKeys = [0]
            };
            _mockWorldManager = Mock.Of<WorldManager>();
            _mockWorld = new Moq.Mock<WorldInstance>(_mockWorldManager.Object.CreateWorldInstance(_worldTemplate, 0));
            // _mockWorld = Mock.Of<WorldInstance>();
            //_mockSlaveManager = Mock.Of<SlaveManager>();
            _mockSlave = Mock.Of<Slave>();
            var mockShipModel = Mock.Of<ShipModelV1>();
            _mockRigidBody = new Moq.Mock<RigidBody>(new BoxShape(1, 1, 1));

            // Configure ModelManager to return _mockShipModel.Object for GetShipModel
            //_mockModelManager = Mock.Of<ModelManager>();
            //_mockModelManager.Setup(mm => mm.GetShipModel(Moq.It.IsAny<uint>())).Returns(mockShipModel.Object);

            _boatPhysicsManager = new PhysicsManager
            {
                //_thread = null,
                //_collisionSystem = null,
                //_physWorld = null,
                //_buoyancy = null,
                //ThreadRunning = false,
                SimulationWorld = _mockWorld.Object,
            };
            // _boatPhysicsManager.SimulationWorld = _mockWorld.Object;
            _boatPhysicsManager.SimulationWorld.Water = new WaterBodies();
            _boatPhysicsManager.SimulationWorld.Water.OceanLevel = _boatPhysicsManager.SimulationWorld.Template.OceanLevel;

            // Polygon water body (lake)
            var mockWaterBodyPoly = new WaterBodyArea("mock_lake", WaterBodyAreaType.Polygon);
            mockWaterBodyPoly.Points.Add(new Vector3(100f, 100f, 200f));
            mockWaterBodyPoly.Points.Add(new Vector3(200f, 100f, 200f));
            mockWaterBodyPoly.Points.Add(new Vector3(200f, 200f, 200f));
            mockWaterBodyPoly.Points.Add(new Vector3(100f, 200f, 200f));
            mockWaterBodyPoly.Depth = 50f;
            mockWaterBodyPoly.UpdateBounds();
            _boatPhysicsManager.SimulationWorld.Water.Areas.Add(mockWaterBodyPoly);

            // LineArray water body (river) 
            // Note: Z is at the bottom of the water body!
            var mockWaterBodyLine = new WaterBodyArea("mock_river", WaterBodyAreaType.LineArray);
            mockWaterBodyLine.Points.Add(new Vector3(300f, 100f, 200f));
            mockWaterBodyLine.Points.Add(new Vector3(350f, 100f, 200f));
            mockWaterBodyLine.Points.Add(new Vector3(400f, 100f, 200f));
            mockWaterBodyLine.Depth = 50f;
            mockWaterBodyLine.RiverWidth = 20f;
            mockWaterBodyLine.UpdateBounds();
            _boatPhysicsManager.SimulationWorld.Water.Areas.Add(mockWaterBodyLine);
        }

        //[Test]
        public async Task Initialize_Should_Initialize_Physics_World()
        {
            // Arrange
            _mockWorld.Setup(w => w.Template.Name).Returns("main_world");
            _mockWorld.Setup(w => w.Template.Cells).Returns(new WorldCell[0, 0]);
            _mockWorld.Setup(w => w.Template.HeightMaxCoefficient).Returns(1.0f);

            // Act
            _boatPhysicsManager.Initialize();

            // Assert
            await Assert.That(_boatPhysicsManager.PhysWorld).IsNotNull();
            await Assert.That(_boatPhysicsManager.Buoyancy).IsNotNull();
            //_mockWorld.Verify(w => w.HeightMaps, Moq.Times.Once);
        }

        //[Test]
        public async Task StartPhysics_WhenCalled_StartsPhysicsThread()
        {
            // Arrange
            _mockWorld.Object.Template.Name = "main_world";
            //_boatPhysicsManager.SimulationWorld = _mockWorld.Object;

            // Act
            _boatPhysicsManager.StartPhysics();

            // Assert
            await Assert.That(_boatPhysicsManager.ThreadRunning).IsTrue();

            // Verify that the thread is started
            await Assert.That(_boatPhysicsManager._thread).IsNotNull();

            Assert.NotEqual("Physics-main_world", _boatPhysicsManager._thread.Name);
            await Assert.That(_boatPhysicsManager._thread.Name).IsEqualTo("Physics-???");

            _boatPhysicsManager.Stop();
        }

        //[Test]
        public async Task RemoveShip_WhenCalled_RemovesRigidBodyFromPhysicsWorld()
        {
            // Arrange
//            _boatPhysicsManager.PhysWorld = new Jitter2.World();
//            _boatPhysicsManager.Buoyancy = new Buoyancy(_boatPhysicsManager.PhysWorld);
            //_boatPhysicsManager.SimulationWorld = _mockWorld.Object;

            _mockSlave.Setup(s => s.RigidBody).Returns(_mockRigidBody.Object);

            // Use reflection to set property values
            //_mockRigidBody.Setup(rb => rb.IsActive).Returns(true);
            var isActiveProperty = typeof(RigidBody).GetProperty("IsActive");
            isActiveProperty?.SetValue(_mockRigidBody.Object, true);

            // Add the rigid body to the physics world
            _boatPhysicsManager.PhysWorld.CreateRigidBody();

            // Act
            _boatPhysicsManager.RemoveShip(_mockSlave.Object);

            // Assert
            await Assert.That(_mockRigidBody.Object.IsActive).IsFalse();
            await Assert.That(_boatPhysicsManager.PhysWorld.RigidBodies).DoesNotContain(_mockRigidBody.Object);
        }

        //[Test]
        public async Task GetRollAngle_WhenCalled_ReturnsRollAngle()
        {
            // Arrange
            var orientation = JMatrix.CreateRotationY(45f.DegToRad()); // 45 градусов
            var rollAngle = Math.Round(PhysicsManager.GetRollAngle(orientation).RadToDeg());

            // Assert
            await Assert.That(rollAngle).IsEqualTo(45f);
        }

        //[Test]
        public async Task Stop_WhenCalled_StopsPhysicsThread()
        {
            // Arrange
            _boatPhysicsManager.ThreadRunning = true;
            _boatPhysicsManager._thread = new Thread(() => { });

            // Act
            _boatPhysicsManager.Stop();

            // Assert
            await Assert.That(_boatPhysicsManager.ThreadRunning).IsFalse();
        }

        /*
        private class WaterTestDataGenerator : IEnumerable<object[]>
        {
            private readonly List<object[]> _data =
            [
                // Ocean test
                new object[] { new Vector3(0f, 0f, 0f), true }, // origin in the corner
                new object[] { new Vector3(50f, 50f, 125f), false }, // above the ocean
                new object[] { new Vector3(50f, 50f, 50f), true }, // inside the default ocean

                // Lake test
                new object[] { new Vector3(125f, 125f, 175f), true }, // inside mock lake
                new object[] { new Vector3(125f, 125f, 225f), false }, // above mock lake
                new object[] { new Vector3(125f, 125f, 125f), false }, // below mock lake and above ocean

                // River test
                new object[] { new Vector3(350f, 100f, 190f), true }, // inside center of mock river
                new object[] { new Vector3(360f, 110f, 190f), true }, // inside mock river slightly of center but within width
                new object[] { new Vector3(350f, 100f, 125f), false }, // below mock river and above ocean
                new object[] { new Vector3(350f, 100f, 300f), false } // above mock river
            ];

            public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        */

        //[Test]
        //[ClassData(typeof(WaterTestDataGenerator))]        
        public async Task TestCustomWater(Vector3 position, bool expected)
        {
            // Check that the CustomWater method correctly defines the water area
            // _boatPhysicsManager.SimulationWorld = _mockWorld.Object;
            _mockWorld.Setup(w => w.IsWater(new Vector3(0f,0f, 0f))).Returns(true);

            var area = position.ToJVector();
            var isWater = _boatPhysicsManager.CustomWater(ref area);

            await Assert.That(expected).IsEqualTo(isWater);
        }

        //[Test]
        public async Task TestGetRollAngle()
        {
            // Проверяем вычисление угла крена из ориентации
            var orientation = JMatrix.Identity;
            var rollAngle = PhysicsManager.GetRollAngle(orientation);

            await Assert.That(rollAngle).IsEqualTo(0f);
        }

        //[Test]
        public async Task TestGetYawPitchRollFromJMatrix()
        {
            // Проверяем извлечение углов поворота из матрицы
            var mat = JMatrix.Identity;
            var (yaw, pitch, roll) = PhysicsManager.GetYawPitchRollFromJMatrix(mat);

            await Assert.That(yaw).IsEqualTo(0f);
            await Assert.That(pitch).IsEqualTo(0f);
            await Assert.That(roll).IsEqualTo(0f);
        }

        //[Test]
        public async Task TestJMatrixToQuaternion()
        {
            // Проверяем преобразование матрицы в кватернион
            var matrix = JMatrix.Identity;
            var quaternion = PhysicsManager.JMatrixToQuaternion(matrix);

            await Assert.That(quaternion.X).IsEqualTo(0f);
            await Assert.That(quaternion.Y).IsEqualTo(0f);
            await Assert.That(quaternion.Z).IsEqualTo(0f);
            await Assert.That(quaternion.W).IsEqualTo(1f);
        }

        //[Test]
        //public async Task AddShip_WhenCalled_AddsRigidBodyToPhysicsWorld()
        //{
        //    // Arrange
        //    _boatPhysicsManager._physWorld = new Jitter.World(new CollisionSystemSAP());
        //    _boatPhysicsManager._buoyancy = new Buoyancy(_boatPhysicsManager._physWorld);
        //    _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

        //    _mockSlave.Setup(s => s.ModelId).Returns(1);
        //    _mockModelManager.Setup(mm => mm.GetShipModel(1)).Returns(_mockShipModel.Object);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeX).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeZ).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeY).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.Mass).Returns(1f);
        //    //ModelManager.Instance = _mockModelManager.Object;

        //    // Act
        //    _boatPhysicsManager.AddShip(_mockSlave.Object);

        //    // Assert
        //    await Assert.That(_mockSlave.Object.RigidBody).IsNotNull();
        //    await Assert.That(_boatPhysicsManager._physWorld.RigidBodies).Contains(_mockSlave.Object.RigidBody);
        //}

        //[Test]
        //public async Task AddShip_Should_Add_Ship_To_Physics_World()
        //{
        //    // Arrange
        //    var mockShipModel = Mock.Of<ShipModel>();
        //    mockShipModel.Setup(m => m.Mass).Returns(100f);
        //    mockShipModel.Setup(m => m.MassBoxSizeX).Returns(1f);
        //    mockShipModel.Setup(m => m.MassBoxSizeY).Returns(1f);
        //    mockShipModel.Setup(m => m.MassBoxSizeZ).Returns(1f);

        //    // Создаем реальный объект Transform
        //    var transform = new Transform();
        //    transform.World = new PositionAndRotation();
        //    transform.World.Position = new Vector3(0, 0, 0);

        //    var mockSlave = Mock.Of<Slave>();
        //    mockSlave.Setup(s => s.ModelId).Returns(1);
        //    mockSlave.Setup(s => s.Transform).Returns(transform);

        //    // Act
        //    _boatPhysicsManager.AddShip(mockSlave.Object);

        //    // Assert
        //    mockSlave.VerifySet(s => s.RigidBody = Moq.It.IsAny<RigidBody>(), Moq.Times.Once);
        //    await Assert.That(mockSlave.Object.RigidBody).IsNotNull();
        //}

        //[Test]
        //public async Task StartPhysicsWhenCalledStartsPhysicsThread()
        //{
        //    // Arrange
        //    var mockThread = Mock.Of<Thread>();
        //    _boatPhysicsManager._thread = mockThread.Object;

        //    // Act
        //    _boatPhysicsManager.StartPhysics();

        //    // Assert
        //    mockThread.Verify(t => t.Start(), Moq.Times.Once());
        //    await Assert.That(_boatPhysicsManager.ThreadRunning).IsTrue();
        //}

        //[Test]
        //public async Task BoatPhysicsTick_WhenOnWaterAppliesBuoyancyAndDrag()
        //{
        //    // Arrange
        //    var mockModelManager = Mock.Of<IModelManager>();
        //    mockModelManager.Setup(mm => mm.GetShipModel(Moq.It.IsAny<uint>())).Returns(_mockShipModel.Object);

        //    var _boatPhysicsManager = new BoatPhysicsManager(mockModelManager.Object);
        //    _boatPhysicsManager._physWorld = new Jitter.World(new CollisionSystemSAP());
        //    _boatPhysicsManager._buoyancy = new Buoyancy(_boatPhysicsManager._physWorld);
        //    _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

        //    // Set RigidBody properties using reflection
        //    //_mockSlave.Setup(s => s.RigidBody).Returns(_mockRigidBody.Object);
        //    var rigidBodyProperty = typeof(Slave).GetProperty("RigidBody");
        //    rigidBodyProperty?.SetValue(_mockSlave.Object, _mockRigidBody.Object);
        //    //_mockRigidBody.Setup(rb => rb.Position).Returns(new JVector(0, 90, 0));
        //    var positionProperty = typeof(RigidBody).GetProperty("Position");
        //    positionProperty?.SetValue(_mockRigidBody.Object, new JVector(0, 90, 0)); // Position.Y < _waterLevel to be on water
        //    //_mockRigidBody.Setup(rb => rb.LinearVelocity).Returns(new JVector(1, 0, 1));
        //    var linearVelocityProperty = typeof(RigidBody).GetProperty("LinearVelocity");
        //    linearVelocityProperty?.SetValue(_mockRigidBody.Object, new JVector(1, 0, 1));

        //    // Set ShipModel properties using reflection
        //    //_mockShipModel.Setup(sm => sm.Mass).Returns(4000f);
        //    var massProperty = typeof(ShipModel).GetProperty("Mass");
        //    massProperty?.SetValue(_mockShipModel.Object, 4000f);

        //    //_mockShipModel.Setup(sm => sm.WaterDensity).Returns(1f);
        //    var waterDensityProperty = typeof(ShipModel).GetProperty("WaterDensity");
        //    waterDensityProperty?.SetValue(_mockShipModel.Object, 1f);
        //    //_mockShipModel.Setup(sm => sm.WaterResistance).Returns(0.1f);
        //    var waterResistanceProperty = typeof(ShipModel).GetProperty("WaterResistance");
        //    waterResistanceProperty?.SetValue(_mockShipModel.Object, 0.1f);

        //    // Set slave.Template and its ModelId
        //    var templateProperty = typeof(Slave).GetProperty("Template");
        //    var mockTemplate = Mock.Of<SlaveTemplate>().Object;
        //    templateProperty?.SetValue(_mockSlave.Object, mockTemplate);

        //    var templateModelIdProperty = typeof(SlaveTemplate).GetProperty("ModelId");
        //    templateModelIdProperty?.SetValue(mockTemplate, 1u);

        //    // Set slave.Transform
        //    var transformProperty = typeof(Slave).GetProperty("Transform");
        //    transformProperty?.SetValue(_mockSlave.Object, Mock.Of<Transform>().Object);

        //    // Set slave.AttachedCharacters
        //    var attachedCharactersProperty = typeof(Slave).GetProperty("AttachedCharacters");
        //    attachedCharactersProperty?.SetValue(_mockSlave.Object, new Dictionary<AttachPointKind, Character>());

        //    // Set other necessary properties on slave
        //    var moveSpeedMulProperty = typeof(Slave).GetProperty("MoveSpeedMul");
        //    moveSpeedMulProperty?.SetValue(_mockSlave.Object, 1f);

        //    var turnSpeedProperty = typeof(Slave).GetProperty("TurnSpeed");
        //    turnSpeedProperty?.SetValue(_mockSlave.Object, 10f);

        //    var throttleProperty = typeof(Slave).GetProperty("Throttle");
        //    throttleProperty?.SetValue(_mockSlave.Object, 0);

        //    var steeringProperty = typeof(Slave).GetProperty("Steering");
        //    steeringProperty?.SetValue(_mockSlave.Object, 0);

        //    var speedProperty = typeof(Slave).GetProperty("Speed");
        //    speedProperty?.SetValue(_mockSlave.Object, 0f);

        //    var rotSpeedProperty = typeof(Slave).GetProperty("RotSpeed");
        //    rotSpeedProperty?.SetValue(_mockSlave.Object, 0f);

        //    // Act
        //    _boatPhysicsManager.BoatPhysicsTick(_mockSlave.Object, _mockRigidBody.Object);

        //    // Assert
        //    // Verify that buoyancy and drag forces are added
        //    _mockRigidBody.Verify(rb => rb.AddForce(Moq.It.IsAny<JVector>()), Moq.Times.Exactly(2));
        //}
    }
}


#endif
