// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
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

        mockTick.VerifyGet(t => t.OnTick, Times.Once);
    }
}

#endif
