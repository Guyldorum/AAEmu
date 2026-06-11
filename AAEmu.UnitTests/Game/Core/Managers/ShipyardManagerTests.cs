// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ShipyardManagerTests
{
    [Test]
    public async Task Initialize_SchedulesTick()
    {
        var mockTask = Mock.Of<ITaskManager>();
        mockTask.Setup(t => t.Schedule(Moq.It.IsAny<AaEmuTask>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<int>())).Returns(true);
        var manager = new ShipyardManager(
            mockTask.Object,
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IShipyardIdManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaxationsManager>().Object,
            Mock.Of<ISkillManager>().Object);
        manager.Initialize();

        mockTask.Verify(t => t.Schedule(Moq.It.IsAny<AaEmuTask>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<int>()), Moq.Times.Once);
    }
}


#endif
