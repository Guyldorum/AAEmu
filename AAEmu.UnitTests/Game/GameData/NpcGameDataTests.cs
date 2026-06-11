using AAEmu.Game.GameData;
namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for NpcGameData class
/// </summary>
public class NpcGameDataTests
{
    [Test]
    public async Task Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = NpcGameData.Instance;
        var instance2 = NpcGameData.Instance;

        // Assert
        await Assert.That(instance2).IsSameReferenceAs(instance1);
    }

    [Test]
    public async Task Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = NpcGameData.Instance;

        // Assert
        await Assert.That(instance).IsNotNull();
    }
}
