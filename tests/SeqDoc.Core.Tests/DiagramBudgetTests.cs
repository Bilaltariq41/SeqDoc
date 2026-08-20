using SeqDoc.Core.Configuration;
using Xunit;

namespace SeqDoc.Core.Tests;

public sealed class DiagramBudgetTests
{
    [Fact]
    public void DefaultsAreFiniteAndValueEqual()
    {
        var expected = new DiagramBudget(1024, 4096, 1024, 256, 45_000);

        Assert.Equal(expected, DiagramBudget.Default);
        Assert.Equal(1024, DiagramBudget.Default.MaxExpandedMethods);
        Assert.Equal(4096, DiagramBudget.Default.MaxExpandedCalls);
        Assert.Equal(1024, DiagramBudget.Default.MaxMaterialMessages);
        Assert.Equal(256, DiagramBudget.Default.MaxParticipants);
        Assert.Equal(45_000, DiagramBudget.Default.MaxMermaidCharacters);
    }

    [Theory]
    [InlineData(0, 4096, 1024, 256, 45000)]
    [InlineData(-1, 4096, 1024, 256, 45000)]
    [InlineData(1024, 0, 1024, 256, 45000)]
    [InlineData(1024, 4096, -1, 256, 45000)]
    [InlineData(1024, 4096, 1024, 0, 45000)]
    [InlineData(1024, 4096, 1024, 256, -1)]
    public void EveryLimitRejectsNonPositiveValues(int methods, int calls, int messages, int participants, int mermaid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DiagramBudget(methods, calls, messages, participants, mermaid));
    }
}
