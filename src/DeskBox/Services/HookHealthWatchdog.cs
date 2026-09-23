using DeskBox.Platform;
using Microsoft.UI.Dispatching;

namespace DeskBox.Services;

/// <summary>
/// A service whose low-level hook should currently be listening. Windows
/// removes WH_KEYBOARD_LL/WH_MOUSE_LL hooks without any notification once a
/// starved callback misses enough deliveries (LowLevelHooksTimeout), so a
/// healthy-looking handle is not proof the hook is still wired in.
/// </summary>
internal interface IHookHealthProbeTarget
{
    /// <summary>Short diagnostic name for log lines.</summary>
    string ProbeName { get; }

    /// <summary>A low-level hook is expected to be listening right now.</summary>
    bool HookProbeWanted { get; }

    /// <summary>
    /// The hook thread/handle is gone — liveness probing is pointless, recover
    /// directly. Distinct from silent removal, which keeps IsActive true.
    /// </summary>
    bool HookConfirmedDead { get; }

    /// <summary>Environment.TickCount64 of the last delivered callback, or 0.</summary>
    long LastHookCallbackTicks { get; }

    /// <summary>
    /// Injects one tagged synthetic input event and waits for the hook to
    /// echo it. Returns false when nothing came back within the window.
    /// </summary>
    Task<bool> ProbeHookAliveAsync(int echoWaitMilliseconds);

    /// <summary>Re-registers the hook. Always invoked on the UI dispatcher.</summary>
    void RecoverHook();
}

/// <summary>
/// Periodic health check for the low-level input hooks. Windows can silently
/// unhook a callback whose owning process is memory-trimmed or throttled for
/// too long, and nothing in the process notices until input is missed
/// permanently. Detection is two-staged so the check itself has no side
/// effects during idle: divergence (user input is flowing per
/// GetLastInputInfo while the hook's callback stays silent) is free to
/// observe; only a divergent target gets a single tagged canary injection as
/// confirmation, and only a failed echo triggers re-registration. A target
/// whose canary just echoed is left alone for a success cooldown: input
/// flowing while a keyboard hook stays silent is the normal state of a
/// mouse-only session, and re-probing it every cycle would feed synthetic
/// keystrokes into the focused app once per silence window.
/// </summary>
internal sealed class HookHealthWatchdog : IDisposable
{
    internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private const uint RecentInputWindowMs = 20_000;
    private const uint CallbackSilentThresholdMs = 60_000;
    private const int CanaryEchoWaitMs = 1_200;
    private const long RecoveryCooldownMs = 5 * 60_000;
    private const long ProbeSuccessCooldownMs = 3 * 60_000;

    private sealed class WatchSlot
    {
        internal required Func<IHookHealthProbeTarget?> Resolve { get; init; }
        internal bool HasRecovered;
        internal long LastRecoveryTicks;
        internal long LastProbeSuccessTicks;
    }

    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly Action<string> _log;
    private readonly List<WatchSlot> _slots = new();
    private readonly object _slotsLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private bool _disposed;

    internal HookHealthWatchdog(DispatcherQueue? dispatcherQueue, Action<string>? log = null)
    {
        _dispatcherQueue = dispatcherQueue;
        _log = log ?? (_ => { });
        _loop = Task.Run(LoopAsync);
    }

    internal void Watch(Func<IHookHealthProbeTarget?> resolve)
    {
        lock (_slotsLock)
        {
            _slots.Add(new WatchSlot { Resolve = resolve });
        }
    }

    /// <summary>
    /// Input is flowing (per GetLastInputInfo) but the hook callback has been
    /// silent long enough that real input must have crossed it. The tick
    /// counts are 32/64-bit GetTickCount-domain values; the subtraction is
    /// intentionally unchecked so a 49.7-day wraparound stays correct.
    /// </summary>
    internal static bool IsDivergent(
        uint lastInputTick,
        long lastCallbackTicks,
        long nowTicks,
        uint recentInputWindowMs,
        uint callbackSilentThresholdMs)
    {
        uint inputAgeMs = unchecked((uint)nowTicks - lastInputTick);
        long callbackSilentMs = nowTicks - lastCallbackTicks;
        return inputAgeMs <= recentInputWindowMs &&
               callbackSilentMs >= callbackSilentThresholdMs;
    }

    internal Task RunCycleOnceAsync()
    {
        bool inputKnown = Win32Helper.TryGetLastInputTickCount(out uint lastInputTick);
        return RunCycleOnceAsync(inputKnown, lastInputTick, Environment.TickCount64);
    }

    /// <summary>
    /// One watchdog pass with an externally supplied clock/input snapshot so
    /// the decision logic can be exercised without real user input.
    /// </summary>
    internal async Task RunCycleOnceAsync(bool inputKnown, uint lastInputTick, long nowTicks)
    {
        List<WatchSlot> slots;
        lock (_slotsLock)
        {
            slots = new List<WatchSlot>(_slots);
        }

        foreach (WatchSlot slot in slots)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            IHookHealthProbeTarget? target;
            try
            {
                target = slot.Resolve();
            }
            catch (Exception ex)
            {
                _log($"[HookWatchdog] Target resolution failed: {ex.Message}");
                continue;
            }

            if (target is null)
            {
                continue;
            }

            try
            {
                await CheckTargetAsync(slot, target, inputKnown, lastInputTick, nowTicks)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log($"[HookWatchdog] Probe of {target.ProbeName} failed: {ex.Message}");
            }
        }
    }

    private async Task CheckTargetAsync(
        WatchSlot slot,
        IHookHealthProbeTarget target,
        bool inputKnown,
        uint lastInputTick,
        long nowTicks)
    {
        if (!target.HookProbeWanted)
        {
            return;
        }

        // While the process is still starved a freshly re-registered hook just
        // accumulates new timeouts; bound the churn with a per-slot cooldown.
        if (slot.HasRecovered &&
            nowTicks - slot.LastRecoveryTicks < RecoveryCooldownMs)
        {
            return;
        }

        bool dead;
        if (target.HookConfirmedDead)
        {
            dead = true;
        }
        else if (
            !inputKnown ||
            !IsDivergent(
                lastInputTick,
                target.LastHookCallbackTicks,
                nowTicks,
                RecentInputWindowMs,
                CallbackSilentThresholdMs))
        {
            return;
        }
        else if (
            slot.LastProbeSuccessTicks != 0 &&
            nowTicks - slot.LastProbeSuccessTicks < ProbeSuccessCooldownMs)
        {
            return;
        }
        else
        {
            // Canary side effects (one synthetic keystroke/mouse nudge) are
            // user-visible in principle — always log them so field logs show
            // the probe cadence without flipping verbose flags.
            _log($"[HookWatchdog] {target.ProbeName} silent while input flows; issuing tagged canary");
            dead = !await target.ProbeHookAliveAsync(CanaryEchoWaitMs).ConfigureAwait(false);
        }

        if (!dead)
        {
            slot.LastProbeSuccessTicks = nowTicks;
            return;
        }

        slot.HasRecovered = true;
        slot.LastRecoveryTicks = nowTicks;
        _log($"[HookWatchdog] {target.ProbeName} hook unresponsive; re-registering");
        if (_disposed)
        {
            return;
        }

        // Without a dispatcher (unit tests, or a degenerate shutdown window)
        // run the recovery inline on the watchdog thread.
        if (_dispatcherQueue is null)
        {
            InvokeRecovery(target);
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    InvokeRecovery(target);
                }
            }))
        {
            _log(
                "[HookWatchdog] Recovery enqueue for " +
                $"{target.ProbeName} failed; dispatcher is gone");
        }
    }

    private void InvokeRecovery(IHookHealthProbeTarget target)
    {
        try
        {
            target.RecoverHook();
        }
        catch (Exception ex)
        {
            _log($"[HookWatchdog] Recovery of {target.ProbeName} failed: {ex.Message}");
        }
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await RunCycleOnceAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log($"[HookWatchdog] Cycle failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Cancel only — the loop may still be inside WaitForNextTickAsync and
        // touching the token; the CTS carries no OS resource to release early.
        _cts.Cancel();
    }
}
