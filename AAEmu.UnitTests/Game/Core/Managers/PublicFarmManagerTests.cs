// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
#if false
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PublicFarmManagerTests
{
    [Test]
    public async Task Initialize_SchedulesTick()
    {
        var mockTask = Mock.Of<ITaskManager>();
        mockTask.Setup(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>())).Returns(true);
        var manager = new PublicFarmManager(mockTask.Object, Mock.Of<IWorldManager>().Object, Mock.Of<ISubZoneManager>().Object);
        manager.Load();
        manager.Initialize();

        mockTask.Verify(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()), Times.Once);
    }
}

#endif
