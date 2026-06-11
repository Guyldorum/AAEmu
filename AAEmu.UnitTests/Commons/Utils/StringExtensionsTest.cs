// === PHASE 12.2 TODO === migration manuelle requise (build KO après migration mécanique lot-12.1)
#if false
﻿using AAEmu.Commons.Utils;
namespace AAEmu.UnitTests.Commons.Utils;

public class StringExtensionsTest
{

    [Test]
    [Arguments("test", "Test"),
    Arguments("Test", "Test"),
    Arguments("tEST", "TEST"),
    Arguments("TEST", "TEST"),
    Arguments("test test", "Test test"),
    Arguments("t", "T"),
    Arguments("T", "T"),
    Arguments("1", "1")]
    public Task FirstCharToUpper_ShouldWorkAsExpected(string input, string expected)
    {
        // Act
        var actual = input.FirstCharToUpper();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
        return Task.CompletedTask;
    }

    [Test]
    [Arguments("")]
    [Arguments(null)]
    public Task FirstCharToUpper_ShouldThrowWhenInvalid(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(input.FirstCharToUpper);

        return Task.CompletedTask;
    }

    [Test]
    [Arguments("test", "Test"),
     Arguments("Test", "Test"),
     Arguments("tEST", "Test"),
     Arguments("TEST", "Test"),
     Arguments("TEST ", "Test"),
     Arguments(" tEsT ", "Test"),
     Arguments("test test", "Test test"),
     Arguments("t", "T"),
     Arguments("T", "T"),
     Arguments("1", "1"),
     Arguments(" \t ", " \t ")]
    public Task NormalizeName_ShouldWorkAsExpected(string input, string expected)
    {
        // Act
        var actual = input.NormalizeName();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
        return Task.CompletedTask;
    }
}

#endif
