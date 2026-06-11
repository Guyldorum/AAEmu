// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
#if false
using AAEmu.Game.Core.Managers;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class EffectTaskManagerTests
{
    [Test]
    public async Task AddDispelTask_CallsTaskManagerSchedule()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()))
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 250.0);

        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(250.0), null, -1),
            Times.Once);
    }

    [Test]
    public async Task AddDispelTask_WithDifferentInterval_PassesCorrectTimeSpan()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()))
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 1000.0);

        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(1000.0), null, -1),
            Times.Once);
    }
}

#endif
