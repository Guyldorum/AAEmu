// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
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
            .Setup(t => t.Schedule(Moq.It.IsAny<AAEmu.Game.Models.Tasks.Task>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<int>()))
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 250.0);

        mockTaskManager.Verify(
            t => t.Schedule(Moq.It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(250.0), null, -1),
            Moq.Times.Once);
    }

    [Test]
    public async Task AddDispelTask_WithDifferentInterval_PassesCorrectTimeSpan()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(Moq.It.IsAny<AAEmu.Game.Models.Tasks.Task>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<int>()))
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 1000.0);

        mockTaskManager.Verify(
            t => t.Schedule(Moq.It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(1000.0), null, -1),
            Moq.Times.Once);
    }
}


#endif
