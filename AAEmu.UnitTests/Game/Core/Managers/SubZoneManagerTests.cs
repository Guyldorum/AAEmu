// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class SubZoneManagerTests
{
    [Test]
    public async Task Load_CallsGetWorlds()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.Setup(w => w.GetWorlds()).Returns([]);
        var manager = new SubZoneManager(mockWorld.Object, Mock.Of<IZoneManager>().Object);
        manager.Load();

        mockWorld.Verify(w => w.GetWorlds(), Moq.Times.Once);
    }
}


#endif
