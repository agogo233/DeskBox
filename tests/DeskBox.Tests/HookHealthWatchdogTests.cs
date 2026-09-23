using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class HookHealthWatchdogTests
{
    private const uint RecentInputWindowMs = 20_000;
    private const uint CallbackSilentThresholdMs = 60_000;
    private const long RecoveryCooldownMs = 5 * 60_000;
    private const long ProbeSuccessCooldownMs = 3 * 60_000;

    private sealed class FakeHookTarget : IHookHealthProbeTarget
    {
        public bool Wanted { get; set; }
        public bool ConfirmedDead { get; set; }
        public long CallbackTicks { get; set; }
        public bool ProbeResult { get; set; } = true;
        public int ProbeCalls;
        public int RecoverCalls;

        public string ProbeName => "fake";
        public bool HookProbeWanted => Wanted;
        public bool HookConfirmedDead => ConfirmedDead;
        public long LastHookCallbackTicks => CallbackTicks;

        public Task<bool> ProbeHookAliveAsync(int echoWaitMilliseconds)
        {
            ProbeCalls++;
            return Task.FromResult(ProbeResult);
        }

        public void RecoverHook() => RecoverCalls++;
    }

    private static HookHealthWatchdog CreateWatchdog(FakeHookTarget target)
    {
        // A null dispatcher makes the watchdog invoke recovery inline, which
        // keeps the decision logic synchronous and deterministic in tests.
        var watchdog = new HookHealthWatchdog(dispatcherQueue: null);
        watchdog.Watch(() => target);
        return watchdog;
    }

    [Fact]
    public async Task TargetNotWanted_SkipsProbeAndRecovery()
    {
        var target = new FakeHookTarget { Wanted = false, ConfirmedDead = true };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: true, lastInputTick: 0, nowTicks: 0);

        Assert.Equal(0, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public async Task ConfirmedDeadTarget_RecoversWithoutCanary()
    {
        var target = new FakeHookTarget { Wanted = true, ConfirmedDead = true };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: true, lastInputTick: 0, nowTicks: 0);

        Assert.Equal(0, target.ProbeCalls);
        Assert.Equal(1, target.RecoverCalls);
    }

    [Fact]
    public async Task DivergentTargetWithFailedCanary_Recovers()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
            ProbeResult = false,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: true,
            lastInputTick: unchecked((uint)(now - 5_000)),
            nowTicks: now);

        Assert.Equal(1, target.ProbeCalls);
        Assert.Equal(1, target.RecoverCalls);
    }

    [Fact]
    public async Task DivergentTargetWithEchoedCanary_StaysRegistered()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
            ProbeResult = true,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: true,
            lastInputTick: unchecked((uint)(now - 5_000)),
            nowTicks: now);

        Assert.Equal(1, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public async Task SuccessfulCanary_LeavesTargetAloneUntilCooldownElapses()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
            ProbeResult = true,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(now - 5_000)), now);
        Assert.Equal(1, target.ProbeCalls);

        // Still divergent (input flowing, callback silent) — this is the
        // mouse-only-session steady state, and a just-echoed canary bought
        // quiet time, so the target is not re-probed every cycle.
        long midCycle = now + 90_000;
        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(midCycle - 5_000)), midCycle);
        Assert.Equal(1, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);

        long afterCooldown = now + ProbeSuccessCooldownMs + 1;
        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(afterCooldown - 5_000)), afterCooldown);
        Assert.Equal(2, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public async Task SilentCallbackWithoutRecentInput_SkipsCanary()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
        };
        using var watchdog = CreateWatchdog(target);

        // Input last seen outside the recent window — nobody is typing, so a
        // quiet hook is expected rather than suspicious.
        await watchdog.RunCycleOnceAsync(
            inputKnown: true,
            lastInputTick: unchecked((uint)(now - RecentInputWindowMs - 1)),
            nowTicks: now);

        Assert.Equal(0, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public async Task RecentlyActiveCallback_SkipsCanary()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - 10_000,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: true,
            lastInputTick: unchecked((uint)(now - 5_000)),
            nowTicks: now);

        Assert.Equal(0, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public async Task RecoveryCooldown_PreventsRepeatedRecovery()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
            ProbeResult = false,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(now - 5_000)), now);
        long midCycle = now + 60_000;
        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(midCycle - 5_000)), midCycle);

        Assert.Equal(1, target.ProbeCalls);
        Assert.Equal(1, target.RecoverCalls);

        // Once the cooldown elapses a still-dead hook is retried.
        long lateCycle = now + RecoveryCooldownMs + 1;
        target.CallbackTicks = lateCycle - CallbackSilentThresholdMs - 1;
        await watchdog.RunCycleOnceAsync(
            true, unchecked((uint)(lateCycle - 5_000)), lateCycle);

        Assert.Equal(2, target.ProbeCalls);
        Assert.Equal(2, target.RecoverCalls);
    }

    [Fact]
    public async Task UnknownInputTick_SkipsDivergenceProbing()
    {
        long now = 1_000_000;
        var target = new FakeHookTarget
        {
            Wanted = true,
            CallbackTicks = now - CallbackSilentThresholdMs - 1,
            ProbeResult = false,
        };
        using var watchdog = CreateWatchdog(target);

        await watchdog.RunCycleOnceAsync(
            inputKnown: false, lastInputTick: 0, nowTicks: now);

        Assert.Equal(0, target.ProbeCalls);
        Assert.Equal(0, target.RecoverCalls);
    }

    [Fact]
    public void IsDivergent_HandlesTickCountWraparound()
    {
        // The 32-bit GetTickCount domain wrapped: nowTicks sits just past
        // 2^32 while the last input tick landed 5s before the wrap.
        long now = (long)uint.MaxValue + 101;
        uint lastInputTick = uint.MaxValue - 4_999;

        bool divergent = HookHealthWatchdog.IsDivergent(
            lastInputTick,
            lastCallbackTicks: now - CallbackSilentThresholdMs - 1,
            nowTicks: now,
            recentInputWindowMs: RecentInputWindowMs,
            callbackSilentThresholdMs: CallbackSilentThresholdMs);

        Assert.True(divergent);
    }

    [Fact]
    public void IsDivergent_NeverFiredCallbackCountsAsSilent()
    {
        bool divergent = HookHealthWatchdog.IsDivergent(
            lastInputTick: 999_999,
            lastCallbackTicks: 0,
            nowTicks: 1_000_000,
            recentInputWindowMs: RecentInputWindowMs,
            callbackSilentThresholdMs: CallbackSilentThresholdMs);

        Assert.True(divergent);
    }
}
