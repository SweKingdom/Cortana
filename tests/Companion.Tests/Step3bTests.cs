using Companion.Character;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

/// <summary>The mood is held between messages and fades after a long quiet (D-070).</summary>
public class MoodHoldTests
{
    private sealed class FakeClock
    {
        public readonly List<(TimeSpan After, Action Run, bool[] Live)> Timers = new();
        public IDisposable Schedule(TimeSpan after, Action run)
        {
            var live = new[] { true };
            Timers.Add((after, run, live));
            return new Handle(live);
        }
        public int Live => Timers.Count(t => t.Live[0]);
        public void FireLive() { foreach (var t in Timers.Where(t => t.Live[0]).ToList()) { t.Live[0] = false; t.Run(); } }
        private sealed class Handle(bool[] live) : IDisposable { public void Dispose() => live[0] = false; }
    }

    private static (MoodHold Hold, FakeClock Clock, List<string> Log) Make()
    {
        var clock = new FakeClock(); var log = new List<string>();
        return (new MoodHold(TimeSpan.FromMinutes(10), clock.Schedule, () => log.Add("fade")), clock, log);
    }

    [Fact]
    public void ATagSetsTheExpressionAndItIsHeldWithoutATagAfterwards()
    {
        var (hold, _, _) = Make();
        Assert.Equal(Expression.Flirty, hold.Reply(Mood.Flirty));
        Assert.Equal(Expression.Flirty, hold.Reply(null));   // a reply with no tag leaves the colour
        Assert.Equal(Expression.Flirty, hold.Current);
    }

    [Fact]
    public void ADifferentTagReplacesItAndIdleClearsIt()
    {
        var (hold, _, _) = Make();
        hold.Reply(Mood.Flirty);
        Assert.Equal(Expression.Concerned, hold.Reply(Mood.Concerned));
        Assert.Equal(Expression.None, hold.Reply(Mood.Idle));
    }

    [Fact]
    public void AnInterruptedReplyAndAFailedTurnKeepTheColour()
    {
        var (hold, _, _) = Make();
        hold.Reply(Mood.Happy);
        Assert.Equal(Expression.Happy, hold.Reply(Mood.Teasing, interrupted: true));
        hold.Quiet();
        Assert.Equal(Expression.Happy, hold.Current);
    }

    [Fact]
    public void ItWaitsTenMinutesFromTheEndOfAReplyThenAsksForTheFade()
    {
        var (hold, clock, log) = Make();
        hold.Reply(Mood.Flirty);
        var t = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromMinutes(10), t.After);
        clock.FireLive();
        Assert.Equal(new[] { "fade" }, log);
        Assert.Equal(Expression.Flirty, hold.Current);   // held until the animator says the fade finished
        hold.Faded();
        Assert.Equal(Expression.None, hold.Current);
    }

    [Fact]
    public void AMessageStopsTheWaitAndTheNextReplyRestartsIt()
    {
        var (hold, clock, log) = Make();
        hold.Reply(Mood.Flirty);
        hold.Chatting();
        Assert.Equal(0, clock.Live);
        clock.FireLive();
        Assert.Empty(log);
        hold.Reply(null);
        Assert.Equal(1, clock.Live);
    }

    [Fact]
    public void ANewReplyRestartsTheWaitInsteadOfAddingOne()
    {
        var (hold, clock, _) = Make();
        hold.Reply(Mood.Flirty);
        hold.Reply(Mood.Happy);
        Assert.Equal(1, clock.Live);
    }

    [Fact]
    public void NothingIsScheduledWhileNoExpressionIsHeld()
    {
        var (hold, clock, _) = Make();
        hold.Reply(null);
        hold.Reply(Mood.Idle);
        hold.Quiet();
        Assert.Equal(0, clock.Live);
    }

    [Fact]
    public void TheHoldAndFadeTimesAreTenMinutesAndSixtySeconds()
    {
        Assert.Equal(600, OrbTuning.MoodHoldSeconds);
        Assert.Equal(60, OrbTuning.MoodFadeSeconds);
    }
}

/// <summary>The window-free rules of the chat input (D-071, D-073).</summary>
public class InputRulesTests
{
    [Fact]
    public void TypingDoesNotInterruptARunningReply()
        => Assert.False(InputRules.TypingInterrupts);

    [Fact]
    public void TheFirstKeystrokeAfterASendArmsThePreWarm()
        => Assert.True(InputRules.ShouldArm(hasText: true, replyRunning: false, nowTick: 5_000, armedTick: InputRules.NotArmed));

    [Fact]
    public void NotArmedCannotOverflowAtAnyUptime()
    {
        foreach (var uptime in new long[] { 0, 1, 20_000, long.MaxValue / 4 })
            Assert.True(InputRules.ShouldArm(true, false, uptime, InputRules.NotArmed));
    }

    [Fact]
    public void NoArmWhileAReplyRunsOrTheBoxIsEmptyOrItWasJustArmed()
    {
        Assert.False(InputRules.ShouldArm(true, true, 100_000, InputRules.NotArmed));    // skipped; armed when the reply ends
        Assert.False(InputRules.ShouldArm(false, false, 100_000, InputRules.NotArmed));
        Assert.False(InputRules.ShouldArm(true, false, 100_000, 90_000));                // 10 s ago
        Assert.True(InputRules.ShouldArm(true, false, 100_000, 70_000));                 // 30 s ago: the 60 s process may have expired
    }
}
