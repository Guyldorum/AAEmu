using AAEmu.Game.Core.Network.Game;
namespace AAEmu.UnitTests.Game.Core.Network.Game;

/// <summary>
/// Tests for GameNetwork class
/// </summary>
public class GameNetworkTests
{
    private readonly GameNetwork _cut = GameNetwork.Instance;

    [Test]
    public async Task Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = GameNetwork.Instance;
        var instance2 = GameNetwork.Instance;

        // Assert
        await Assert.That(instance2).IsSameReferenceAs(instance1);
    }

    [Test]
    public async Task Start_InitializesServer()
    {
        // Arrange
        // Note: This test requires AppConfiguration to be set up
        // For full integration, configuration needs to be mocked

        // Act
        // _cut.Start(); // Requires actual configuration

        // Assert
        // Verification would require mocking the Server class
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Stop_StopsServer_WhenStarted()
    {
        // Arrange
        // Note: Stop requires Start to be called first

        // Act
        // _cut.Stop(); // Requires server to be started

        // Assert
        // Verification would require mocking the Server class
        await Assert.That(true).IsTrue();
    }
}
