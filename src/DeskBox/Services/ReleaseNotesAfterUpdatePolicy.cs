namespace DeskBox.Services;

/// <summary>
/// Pure decision logic for the post-update release-notes window. The window
/// opens exactly once per observed version change, and only after onboarding
/// has been completed — a fresh install is the onboarding flow's first
/// impression to make, not the changelog's (the same ordering PowerToys
/// applies between its OOBE and What's-New windows).
/// </summary>
public static class ReleaseNotesAfterUpdatePolicy
{
    public static bool ShouldShowWindow(
        bool showReleaseNotesAfterUpdate,
        bool hasCompletedOnboarding,
        string? lastRunVersion,
        string currentVersion)
    {
        if (!showReleaseNotesAfterUpdate || !hasCompletedOnboarding)
        {
            return false;
        }

        // A missing previous version means this is the first observed run
        // (fresh install or pre-existing install upgraded from a build that
        // did not track it); neither should surprise the user with notes.
        if (string.IsNullOrWhiteSpace(lastRunVersion) ||
            string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        return !string.Equals(
            lastRunVersion.Trim(),
            currentVersion.Trim(),
            StringComparison.Ordinal);
    }
}
