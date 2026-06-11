// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
using AAEmu.Game.Core.Managers;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class ManaRegenManagerTests
{
    [Test]
    public async Task Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.SetupGet(t => t.OnTick).Returns(handler);

        var manager = new ManaRegenManager(mockTick.Object);
        manager.Initialize();

        mockTick.VerifyGet(t => t.OnTick, Moq.Times.Once);
    }
}


#endif
