using System.Text;
using Companion.Character;
using Xunit;

namespace Companion.Tests;

public class TypewriterPacerTests
{
    /// <summary>Runs the pacer in 1 ms steps until everything is shown; returns the elapsed ms and the text.</summary>
    private static (double Ms, string Text) Drain(TypewriterPacer p, double limitMs = 120_000)
    {
        var sb = new StringBuilder();
        double t = 0;
        sb.Append(p.Advance(0));
        while (p.HasPending && t < limitMs) { t += 1; sb.Append(p.Advance(1)); }
        return (t, sb.ToString());
    }

    private static TypewriterPacer With(string text)
    {
        var p = new TypewriterPacer();
        p.Push(text);
        return p;
    }

    [Fact]
    public void FirstCharacterIsShownAtOnceWithNoDelay()
    {
        var p = With("Hello there.");
        Assert.Equal("H", p.Advance(0));
    }

    [Fact]
    public void ShortTextRunsAtTheBasePace()
    {
        var p = With(new string('a', 40));                  // under CatchUpFromChars, no punctuation
        var (ms, text) = Drain(p);
        Assert.Equal(40, text.Length);
        // first char is free, the other 39 take 1000/45 ms each
        double expected = 39 * 1000.0 / TypewriterTuning.BaseCps;
        Assert.InRange(ms, expected - 25, expected + 25);
    }

    [Fact]
    public void PaceRisesGraduallyWithTheBufferAndIsCapped()
    {
        Assert.Equal(TypewriterTuning.BaseCps, TypewriterPacer.RateCps(0));
        Assert.Equal(TypewriterTuning.BaseCps, TypewriterPacer.RateCps(TypewriterTuning.CatchUpFromChars));
        double prev = TypewriterPacer.RateCps(TypewriterTuning.CatchUpFromChars);
        for (int n = TypewriterTuning.CatchUpFromChars + 10; n <= 2000; n += 10)
        {
            double r = TypewriterPacer.RateCps(n);
            Assert.True(r >= prev, $"rate fell at {n}");
            Assert.True(r <= TypewriterTuning.MaxCps);
            prev = r;
        }
        Assert.True(TypewriterPacer.RateCps(TypewriterTuning.CatchUpFromChars + 20) < TypewriterTuning.MaxCps, "not an instant jump");
        Assert.Equal(TypewriterTuning.MaxCps, TypewriterPacer.RateCps(5000));
    }

    [Fact]
    public void LongTextIsFasterThanTheBasePaceButNeverFasterThanTheCap()
    {
        var p = With(new string('a', 600));
        var (ms, text) = Drain(p);
        Assert.Equal(600, text.Length);
        Assert.True(ms < 600 * 1000.0 / TypewriterTuning.BaseCps * 0.6, $"long reply dragged: {ms} ms");
        Assert.True(ms > 600 * 1000.0 / TypewriterTuning.MaxCps * 0.95, $"faster than the cap: {ms} ms");
    }

    [Fact]
    public void SentenceEndAndCommaAddAPause()
    {
        double Time(string s) => Drain(With(s)).Ms;
        double plain = Time("aaaa bbbb cccc");
        double comma = Time("aaaa, bbbb cccc");
        double sentence = Time("aaaa. bbbb cccc");
        Assert.InRange(comma - plain, TypewriterTuning.CommaPauseMs - 25, TypewriterTuning.CommaPauseMs + 25);
        Assert.InRange(sentence - plain, TypewriterTuning.SentencePauseMs - 25, TypewriterTuning.SentencePauseMs + 25);
        Assert.True(sentence > comma);
    }

    [Fact]
    public void ADecimalPointIsNotASentenceEnd()
    {
        Assert.InRange(Drain(With("it costs 3.5 euro")).Ms - Drain(With("it costs 3x5 euro")).Ms, -25, 25);
    }

    [Fact]
    public void AnEllipsisPausesOnlyAfterTheLastDot()
    {
        double plain = Drain(With("wait xxx go")).Ms;
        double dots = Drain(With("wait... go")).Ms;   // 3 dots replace "xxx", pause once at the end
        Assert.InRange(dots - plain, TypewriterTuning.SentencePauseMs - 25, TypewriterTuning.SentencePauseMs + 25);
    }

    [Fact]
    public void EachSentenceEndInsideAReplyPausesOnce()
    {
        // two sentence ends inside the text; the one after the last character has nothing left to delay
        const string s = "One two. Three four. Five six.";
        double withPauses = Drain(With(s)).Ms;
        double none = Drain(With(s.Replace(".", "x"))).Ms;
        Assert.InRange(withPauses - none, 2 * TypewriterTuning.SentencePauseMs - 40, 2 * TypewriterTuning.SentencePauseMs + 40);
    }

    [Fact]
    public void ASentencePauseSurvivesAChunkBoundary()
    {
        // streaming often ends a chunk right after the full stop
        var p = new TypewriterPacer();
        p.Push("Hi.");
        double t = 0;
        string shown = p.Advance(0);
        while (p.HasPending) { t += 1; shown += p.Advance(1); }
        Assert.Equal("Hi.", shown);
        p.Push(" Next");
        // the pause after "." is still owed: right after the push nothing comes out until it has run down
        Assert.Equal("", p.Advance(TypewriterTuning.SentencePauseMs - 30));
    }

    [Fact]
    public void IdleTimeNeverTurnsIntoABurst()
    {
        var p = new TypewriterPacer();
        p.Push("ab");
        Drain(p);
        Assert.Equal("", p.Advance(10_000));           // nothing buffered: the wait is not saved up
        p.Push(new string('c', 30));
        Assert.True(p.Advance(0).Length <= 1, "a late chunk must not appear all at once");
    }

    [Fact]
    public void StopWhereItIsDropsTheRest()
    {
        var p = With("This will be cut off in the middle of the sentence.");
        string shown = p.Advance(0) + p.Advance(200);   // about 10 characters
        Assert.InRange(shown.Length, 5, 15);
        p.Reset();                                       // what an interrupt does
        Assert.False(p.HasPending);
        Assert.Equal("", p.Advance(60_000));
    }

    [Fact]
    public void AfterResetTheNextReplyAgainStartsAtOnce()
    {
        var p = With("first reply");
        Drain(p);
        p.Reset();
        p.Push("second");
        Assert.Equal("s", p.Advance(0));
    }

    [Fact]
    public void AllTextIsDeliveredInOrderWhateverTheStepSize()
    {
        const string s = "Well, that's one way to do it. Is it the best? Probably not!";
        foreach (double step in new[] { 1.0, 7.0, 33.0, 250.0 })
        {
            var p = With(s);
            var sb = new StringBuilder(p.Advance(0));
            int guard = 0;
            while (p.HasPending && guard++ < 100_000) sb.Append(p.Advance(step));
            Assert.Equal(s, sb.ToString());
        }
    }
}
