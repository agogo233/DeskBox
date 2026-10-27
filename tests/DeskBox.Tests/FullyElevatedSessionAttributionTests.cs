// Copyright (c) DeskBox. All rights reserved.

using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// Pins the fully-elevated-desktop attribution contract (2026-10-22
/// informed-notice batch): same-level elevated sessions are attributed by
/// cause — UAC-off, built-in Administrator (RID 500), or a generic elevated
/// desktop — so the notice and the diagnostic card can name the actual
/// restore path. Asymmetric sessions never attribute here (they keep the
/// legacy asymmetry notice machinery). Also pins the aligned relaunch
/// predicate — the diagnostic must not steer a same-level elevated desktop
/// into a restart that lands elevated again — and the environment-issue
/// ranking, where asymmetry outranks UAC-off.
/// </summary>
public class FullyElevatedSessionAttributionTests
{
    [Theory]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-500", true)]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1500", false)]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-501", false)]
    [InlineData("S-1-5-500", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not-a-sid", false)]
    public void IsBuiltInAdministratorSid_MatchesOnlyRid500Tail(string? sid, bool expected)
    {
        Assert.Equal(
            expected,
            DragDropPermissionService.IsBuiltInAdministratorSid(sid));
    }

    [Theory]
    // Asymmetric sessions never attribute.
    [InlineData(false, true, false, false, FullyElevatedSessionAttribution.None)]
    [InlineData(true, false, false, false, FullyElevatedSessionAttribution.None)]
    [InlineData(false, false, true, true, FullyElevatedSessionAttribution.None)]
    // Same-level elevated: UAC-off explains first, then built-in admin,
    // then the generic fallback.
    [InlineData(true, true, true, true, FullyElevatedSessionAttribution.UacDisabled)]
    [InlineData(true, true, true, false, FullyElevatedSessionAttribution.UacDisabled)]
    [InlineData(true, true, false, true, FullyElevatedSessionAttribution.BuiltInAdminAccount)]
    [InlineData(true, true, false, false, FullyElevatedSessionAttribution.GenericElevatedDesktop)]
    public void DecideFullyElevatedSessionAttribution_RanksCauses(
        bool processElevated,
        bool explorerElevated,
        bool uacDisabled,
        bool builtInAdmin,
        FullyElevatedSessionAttribution expected)
    {
        Assert.Equal(
            expected,
            DragDropPermissionService.DecideFullyElevatedSessionAttribution(
                processElevated,
                explorerElevated,
                uacDisabled,
                builtInAdmin));
    }

    [Fact]
    public void Assessment_DerivesAttributionFromSnapshots()
    {
        var builtInAdminDesktop = new StartupElevationAssessment(
            IsCurrentProcessElevated: true,
            IsExplorerElevated: true,
            IsUacDisabled: false,
            CanRelaunchUnelevated: false,
            IsBuiltInAdminAccount: true);
        Assert.Equal(
            FullyElevatedSessionAttribution.BuiltInAdminAccount,
            builtInAdminDesktop.FullyElevatedAttribution);

        // The legacy four-argument shape still compiles and reads as a
        // generic elevated desktop.
        var genericDesktop = new StartupElevationAssessment(true, true, false, false);
        Assert.Equal(
            FullyElevatedSessionAttribution.GenericElevatedDesktop,
            genericDesktop.FullyElevatedAttribution);

        // Asymmetry outranks attribution even on a RID-500 machine: a
        // same-level desktop is required for the fully-elevated copy.
        var asymmetric = new StartupElevationAssessment(
            IsCurrentProcessElevated: true,
            IsExplorerElevated: false,
            IsUacDisabled: false,
            CanRelaunchUnelevated: true,
            IsBuiltInAdminAccount: true);
        Assert.Equal(FullyElevatedSessionAttribution.None, asymmetric.FullyElevatedAttribution);
        Assert.Equal(ShellIntegrityMismatch.AppAboveShell, asymmetric.Mismatch);
    }

    [Theory]
    // AppAboveShell with UAC on: the Explorer-parented relaunch really
    // lands unelevated.
    [InlineData(true, false, false, true)]
    // Built-in-admin / UAC-off desktops: a relaunch would land elevated
    // again (UAC-off stays conservative — the launch gate's loop breaker).
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    // Not elevated: nothing to relaunch.
    [InlineData(false, false, false, false)]
    public void ComputeNeedsRelaunchDiagnostic_AlignsWithCanRelaunch(
        bool processElevated,
        bool explorerElevated,
        bool uacDisabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            DragDropPermissionService.ComputeNeedsRelaunchDiagnostic(
                processElevated,
                uacDisabled,
                explorerElevated));
        // The diagnostic and the launch gate agree on the pairs the launch
        // gate can actually serve (explorer probed, not unknown).
        if (!uacDisabled)
        {
            Assert.Equal(
                expected,
                DragDropPermissionService.ComputeCanRelaunchUnelevated(uacDisabled, explorerElevated) &&
                processElevated);
        }
    }

    [Theory]
    // Asymmetry outranks UAC-off: an asymmetric session has a DeskBox-side
    // fix while UAC-off is only the backdrop.
    [InlineData(true, false, true, FullyElevatedSessionAttribution.None, DragDropDiagnosticIssue.PermissionMismatch)]
    [InlineData(false, true, true, FullyElevatedSessionAttribution.None, DragDropDiagnosticIssue.ShellAboveApp)]
    // UAC-off on its own (standard-user desktop, or the same-level case the
    // attribution also names UacDisabled for).
    [InlineData(false, false, true, FullyElevatedSessionAttribution.None, DragDropDiagnosticIssue.UacDisabled)]
    [InlineData(false, false, false, FullyElevatedSessionAttribution.UacDisabled, DragDropDiagnosticIssue.UacDisabled)]
    // Fully-elevated family with UAC on.
    [InlineData(false, false, false, FullyElevatedSessionAttribution.BuiltInAdminAccount, DragDropDiagnosticIssue.FullyElevatedSession)]
    [InlineData(false, false, false, FullyElevatedSessionAttribution.GenericElevatedDesktop, DragDropDiagnosticIssue.FullyElevatedSession)]
    // Clean environment.
    [InlineData(false, false, false, FullyElevatedSessionAttribution.None, DragDropDiagnosticIssue.None)]
    public void DecideEnvironmentIssue_RanksAsymmetryAboveUacOff(
        bool permissionMismatch,
        bool shellAboveApp,
        bool isUacDisabled,
        FullyElevatedSessionAttribution attribution,
        DragDropDiagnosticIssue expected)
    {
        Assert.Equal(
            expected,
            DragDropPermissionService.DecideEnvironmentIssue(
                isUacDisabled,
                permissionMismatch,
                shellAboveApp,
                attribution));
    }
}
