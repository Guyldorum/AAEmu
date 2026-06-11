// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
using AAEmu.Game.Core.Managers;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class AccountManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockTick = Mock.Of<ITickManager>();
        var mockTimedRewards = Mock.Of<ITimedRewardsManager>();

        var manager = new AccountManager(mockTick.Object, mockTimedRewards.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockTick);
        Mock.VerifyNoOtherCalls(mockTimedRewards);
    }

    [Test]
    public async Task Initialize_AccessesOnTickProperty()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.Setup(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new AccountManager(mockTick.Object, Mock.Of<ITimedRewardsManager>().Object);
        manager.Initialize();

        mockTick.Verify(t => t.OnTick, Moq.Times.Once);
    }
}


#endif
