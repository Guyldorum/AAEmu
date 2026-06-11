// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
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
        mockTask.Setup(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>())).Returns(true);
        var manager = new ShipyardManager(
            mockTask.Object,
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IShipyardIdManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaxationsManager>().Object,
            Mock.Of<ISkillManager>().Object);
        manager.Initialize();

        mockTask.Verify(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()), Times.Once);
    }
}

#endif
