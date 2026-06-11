// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class IndunManagerTests
{
    [Test]
    public async Task Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());
        var manager = new IndunManager(mockTick.Object, Mock.Of<IWorldManager>().Object, Mock.Of<IZoneManager>().Object, Mock.Of<ITeamManager>().Object);
        manager.Initialize();

        mockTick.VerifyGet(t => t.OnTick, Moq.Times.Once);
    }
}


#endif
