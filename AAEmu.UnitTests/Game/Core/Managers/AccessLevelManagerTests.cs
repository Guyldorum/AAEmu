using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;

namespace AAEmu.UnitTests.Game.Core.Managers
{
    public class AccessLevelManagerTests
    {
        private readonly AccessLevelManager _manager;

        public AccessLevelManagerTests()
        {
            _manager = new AccessLevelManager();
            ResetAppConfiguration();
        }

        private void ResetAppConfiguration()
        {
            AppConfiguration.Instance.AccessLevel?.Clear();
        }

        [Test]
        public async Task GetLevel_WhenCommandNotExists_ShouldReturnDefaultLevel()
        {
            _manager.Load();
            var result = _manager.GetLevel("non_existent_command");
            await Assert.That(result).IsEqualTo(100);
        }

        [Test]
        public async Task GetLevel_WhenCommandExists_ShouldReturnCorrectLevel()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel.Add("test_command", 5);

            _manager.Load();
            var result = _manager.GetLevel("test_command");
            await Assert.That(result).IsEqualTo(5);
        }

        [Test]
        public async Task Load_ShouldLoadMultipleCommandsCorrectly()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel["cmd1"] = 1;
            accessLevel["cmd2"] = 2;
            accessLevel["cmd3"] = 3;

            _manager.Load();
            await Assert.That(_manager.GetLevel("cmd1")).IsEqualTo(1);
            await Assert.That(_manager.GetLevel("cmd2")).IsEqualTo(2);
            await Assert.That(_manager.GetLevel("cmd3")).IsEqualTo(3);
        }

        [Test]
        public async Task Load_WhenDuplicateCommands_ShouldOverwriteLevel()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel["duplicate"] = 5;
            accessLevel["duplicate"] = 10;

            _manager.Load();
            await Assert.That(_manager.GetLevel("duplicate")).IsEqualTo(10);
        }

        [Test]
        public async Task Load_WhenEmptyConfig_ShouldNotLoadCommands()
        {
            _manager.Load();
            await Assert.That(_manager.GetLevel("any_command")).IsEqualTo(100);
        }
    }
}
