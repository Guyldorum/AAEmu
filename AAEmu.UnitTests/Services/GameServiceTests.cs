using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace AAEmu.UnitTests.Services;

/// <summary>
/// Tests for GameService class
/// </summary>
public class GameServiceTests
{
    [Test]
    public async Task StartTime_IsInitializedToUtcNow()
    {
        // Arrange & Act
        var startTime = GameService.StartTime;

        // Assert
        await Assert.That(startTime <= DateTime.UtcNow).IsTrue();
        await Assert.That((DateTime.UtcNow - startTime).TotalSeconds < 1).IsTrue();
    }

    [Test]
    public async Task TimeSinceStart_ReturnsTimeSpanSinceStart()
    {
        // Arrange
        var startTime = GameService.StartTime;

        // Act
        var timeSinceStart = GameService.TimeSinceStart;

        // Assert
        // Verify TimeSinceStart is non-negative
        await Assert.That(timeSinceStart >= TimeSpan.Zero).IsTrue();

        // Verify TimeSinceStart is consistent with the formula: DateTime.UtcNow - StartTime
        // Allow 100ms tolerance for execution time variation
        var expectedTimeSinceStart = DateTime.UtcNow - startTime;
        var tolerance = TimeSpan.FromMilliseconds(100);
        await Assert.That(timeSinceStart <= expectedTimeSinceStart + tolerance).IsTrue();
        await Assert.That(timeSinceStart >= expectedTimeSinceStart - tolerance).IsTrue();
    }

    [Test]
    public async Task GameService_ImplementsIHostedService()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Assert
        await Assert.That(service).IsAssignableTo<IHostedService>();
    }

    [Test]
    public async Task GameService_ImplementsIDisposable()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Assert
        await Assert.That(service).IsAssignableTo<IDisposable>();
    }

    [Test]
    public async Task Dispose_DoesNotThrow()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Act & Assert
        service.Dispose();
        await Task.CompletedTask; // Suppress warning
    }
}

