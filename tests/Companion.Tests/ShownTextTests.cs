using Companion.Character;
using Xunit;

namespace Companion.Tests;

public class ShownTextTests
{
    [Fact]
    public void CollectsOnlyWhatWasRevealedForTheTurn()
    {
        var s = new ShownText();
        s.Start(3);
        s.Add("Once ");
        s.Add("upon");
        Assert.Equal("Once upon", s.For(3));
        Assert.Equal(9, s.Length);
    }

    [Fact]
    public void AnotherTurnGetsNothing()
    {
        var s = new ShownText();
        s.Start(3);
        s.Add("seen");
        Assert.Equal("", s.For(2));
        Assert.Equal("", s.For(4));
    }

    [Fact]
    public void ANewTurnStartsEmpty()
    {
        var s = new ShownText();
        s.Start(1);
        s.Add("old");
        s.Start(2);
        Assert.Equal("", s.For(2));
        Assert.Equal(0, s.Length);
    }
}
