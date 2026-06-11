using AAEmu.Game.GameData;
namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for BuffGameData class
/// </summary>
public class BuffGameDataTests
{
    [Test]
    public async Task Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = BuffGameData.Instance;
        var instance2 = BuffGameData.Instance;

        // Assert
        await Assert.That(instance2).IsSameReferenceAs(instance1);
    }

    [Test]
    public async Task Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = BuffGameData.Instance;

        // Assert
        await Assert.That(instance).IsNotNull();
    }
}
