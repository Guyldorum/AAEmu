using AAEmu.Game.GameData;
namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for ItemGameData class
/// </summary>
public class ItemGameDataTests
{
    [Test]
    public async Task Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = ItemGameData.Instance;
        var instance2 = ItemGameData.Instance;

        // Assert
        await Assert.That(instance2).IsSameReferenceAs(instance1);
    }

    [Test]
    public async Task Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = ItemGameData.Instance;

        // Assert
        await Assert.That(instance).IsNotNull();
    }
}
