using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Pure pacing rules for the periodic background update-check loop (the
/// PowerToys runner cadence): a definitive answer — new version found or
/// verifiably up to date — waits a full day, while anything that left the
/// question open retries in two hours. Invalid manifests retry short too:
/// a partially deployed feed self-heals on the next attempt. The short
/// retry exists because deskbox.fun DNS blackouts on individual machines
/// are a documented field reality, and a one-shot launch check that fails
/// used to silence updates for the whole process lifetime.
/// </summary>
public static class BackgroundUpdateCheckPolicy
{
    public static readonly TimeSpan DefinitiveAnswerInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan FailureRetryInterval = TimeSpan.FromHours(2);

    public static bool IsDefinitive(AppUpdateCheckStatus status)
    {
        return status is AppUpdateCheckStatus.UpdateAvailable or AppUpdateCheckStatus.UpToDate;
    }

    public static TimeSpan GetDelayUntilNextCheck(AppUpdateCheckStatus? status)
    {
        return status is not null && IsDefinitive(status.Value)
            ? DefinitiveAnswerInterval
            : FailureRetryInterval;
    }
}
