using AAEmu.Game.GameData;
namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for AchievementGameData class
/// </summary>
public class AchievementGameDataTests : SqliteTestBase
{
    private readonly AchievementGameData _cut = AchievementGameData.Instance;

    [Test]
    public async Task Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = AchievementGameData.Instance;
        var instance2 = AchievementGameData.Instance;

        // Assert
        await Assert.That(instance2).IsSameReferenceAs(instance1);
    }

    [Test]
    public async Task Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = AchievementGameData.Instance;

        // Assert
        await Assert.That(instance).IsNotNull();
    }
}
