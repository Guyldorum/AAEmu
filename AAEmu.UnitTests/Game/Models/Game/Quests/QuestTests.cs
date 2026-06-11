// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
#if false
﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
#pragma warning disable IDE0051

namespace AAEmu.UnitTests.Game.Models.Game.Quests;
// TODO: Re-enable the quest related test
// ReSharper disable UnusedMember.Local

public class QuestTests
{
    // [Test]
    private void Start_WhenQuestStepIsNoneAndComponentIsEmpty_ShouldDoNothing()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _, out _);
        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns([]);

        // Act
        var result = quest.StartQuest();

        // Assert
        await Assert.That(result).IsFalse();
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    // [Test]
    private void Start_WhenQuestActsIsEmpty_ShouldDoNothing()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _);
        var expectedIds = new List<uint>();

        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>()))
            .Returns<QuestComponentKind>(kind => [new QuestComponentTemplate(null) { KindId = kind }])
            .Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));

        // Act
        var result = quest.StartQuest();

        // Assert
        await Assert.That(result).IsFalse();
        foreach (var exceptedId in expectedIds)
        {
            mockQuestManager.Verify(qm => qm.GetActsInComponent(It.IsIn(exceptedId)), Times.Once);
        }
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    // [Test]
    private void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetNotMatch_ShouldAbort()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _);
        var expectedIds = new List<uint>();

        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => [
            new QuestComponentTemplate(null) { Id = (uint)kind }
        ]).Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));

        var mockQuestAct = Mock.Of<QuestActTemplate>();
        mockQuestAct.Setup(qa => qa.RunAct(It.IsAny<Quest>(), It.IsAny<QuestAct>(), It.IsAny<int>())).Returns(false);
        mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");

        mockQuestManager.Setup(qm => qm.GetActsInComponent(It.IsAny<uint>())).Returns(new[] {
            mockQuestAct.Object
        }.ToList());

        // Act
        var result = quest.StartQuest();

        // Assert
        await Assert.That(result).IsFalse();
        foreach (var exceptedId in expectedIds)
        {
            mockQuestManager.Verify(qm => qm.GetActsInComponent(It.IsIn(exceptedId)), Times.Once);
        }
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
    }

    // [Test]
    private void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetMatch_ButComponentSkillIdIsZero_ShouldNotOwnerUseSkill()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _);
        var expectedIds = new List<uint>();
        mockQuestTemplate.SetupGet(qt => qt.Components).Returns(new Dictionary<uint, QuestComponentTemplate>()
        {
            { 1, new QuestComponentTemplate(null) { Id = 1, KindId = QuestComponentKind.Drop } },
            { 2, new QuestComponentTemplate(null) { Id = 2, KindId = QuestComponentKind.Drop } }
        });
        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => [
            new QuestComponentTemplate(null) { Id = (uint)kind }
        ]);

        var mockQuestAct = Mock.Of<QuestActTemplate>();
        mockQuestAct.Setup(qa => qa.RunAct(It.IsAny<Quest>(), It.IsAny<QuestAct>(), It.IsAny<int>())).Returns(true);
        mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");
        mockQuestManager.Setup(qm => qm.GetActsInComponent(It.IsAny<uint>())).Returns(new[] {
            mockQuestAct.Object
        }.ToList);

        // Act
        var result = quest.StartQuest();

        // Assert
        await Assert.That(result).IsTrue();
        mockOwner.Verify(o => o.UseSkill(It.IsAny<uint>(), It.IsAny<ICharacter>()), Times.Never);
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    private static Quest SetupQuest(
        out Mock<ICharacter> mockCharacter,
        out Mock<IQuestTemplate> mockQuestTemplate,
        out Mock<IQuestManager> mockQuestManager,
        out Mock<TaskManager> mockTaskManager,
        out Mock<ISkillManager> mockSkillManager,
        out Mock<IExpressTextManager> mockExpressTextManager,
        out Mock<IWorldManager> mockWorldManager)
    {
        mockCharacter = Mock.Of<ICharacter>();
        mockQuestManager = Mock.Of<IQuestManager>();
        mockQuestTemplate = Mock.Of<IQuestTemplate>();
        mockQuestTemplate.SetupGet(x => x.Components).Returns(new Dictionary<uint, QuestComponentTemplate>());
        mockExpressTextManager = Mock.Of<IExpressTextManager>();
        mockSkillManager = Mock.Of<ISkillManager>();
        mockTaskManager = Mock.Of<TaskManager>();
        mockWorldManager = Mock.Of<IWorldManager>();

        var quest = new Quest(
            mockQuestTemplate.Object,
            mockCharacter.Object,
            mockQuestManager.Object,
            mockTaskManager.Object,
            mockSkillManager.Object,
            mockExpressTextManager.Object,
            mockWorldManager.Object);

        quest.Owner = mockCharacter.Object;
        quest.Template = mockQuestTemplate.Object;
        return quest;
    }
}

#endif
