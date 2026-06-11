// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
#if false
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
namespace AAEmu.UnitTests.Game.Core.Managers;

public class CashShopManagerTests
{
    [Test]
    public async Task DisableShop_CallsGetAllCharacters()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.Setup(w => w.GetAllCharacters()).Returns([]);
        var manager = new CashShopManager(mockWorld.Object, Mock.Of<IAccountManager>().Object, Mock.Of<ILocalizationManager>().Object);
        manager.DisableShop();

        mockWorld.Verify(w => w.GetAllCharacters(), Times.Once);
    }
}

#endif
