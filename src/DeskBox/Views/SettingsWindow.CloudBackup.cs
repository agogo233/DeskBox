using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace DeskBox.Views;

/// <summary>
/// Cloud-backup settings page (roadmap §10 PR-3): provider selection,
/// WebDAV credential entry, connection test, manual backup, remote
/// snapshot inventory and per-domain restore. The password never touches
/// bindings or settings.json — it goes straight from the PasswordBox to
/// <see cref="ICredentialStore"/>.
/// </summary>
public sealed partial class SettingsWindow
{
    private bool _cloudBackupSnapshotSyncHooked;

    private async Task InitializeCloudBackupSectionAsync()
    {
        if (!_cloudBackupSnapshotSyncHooked)
        {
            _cloudBackupSnapshotSyncHooked = true;
            // The ListView holds a detached object[] snapshot, so every
            // collection mutation (endpoint-invalidation Clear, refresh
            // repopulation) must be mirrored here — the CLR event needs
            // no WinRT marshaling and is safe under Native AOT.
            ViewModel.CloudBackupRemoteSnapshots.CollectionChanged += (_, _) =>
                SyncCloudBackupSnapshotList();
            // Scheduled uploads finish without any UI participation —
            // this is what lets an open page notice them.
            App.Current.CloudBackupService.BackupRunCompleted += OnCloudBackupRunCompleted;
        }

        // The section element may have been recreated since the
        // collection last changed — re-mirror whatever it holds.
        SyncCloudBackupSnapshotList();

        long generation = ViewModel.CloudBackupEndpointGeneration;
        try
        {
            bool saved = await App.Current.CloudBackupService.HasCredentialAsync();
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupCredentialSaved = saved;
            }

            ViewModel.RefreshCloudBackupStatus();

            // Auto-refresh the remote snapshot list on entry — users
            // shouldn't have to discover the Refresh button to see that
            // their backups exist. Deliberately does NOT take
            // CloudBackupBusy: this is a passive read, and an unreachable
            // endpoint could otherwise freeze every control in the
            // section for the full transport timeout on every visit.
            if (saved &&
                generation == ViewModel.CloudBackupEndpointGeneration &&
                App.Current.CloudBackupService.Options.HasEndpoint)
            {
                await RefreshRemoteSnapshotsAsync(generation);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Section init failed: {ex}");
        }
    }

    private async void CloudBackupSavePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy)
        {
            return;
        }

        ViewModel.CloudBackupBusy = true;
        long generation = ViewModel.CloudBackupEndpointGeneration;
        try
        {
            // Read inside the try: a failure here must surface in the status
            // row like every other step, not escape as an unhandled
            // exception from an async void handler.
            string password = CloudBackupPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                await ShowInfoDialogAsync(
                    _localizationService.T("Settings.CloudBackup.Password.EmptyTitle"),
                    _localizationService.T("Settings.CloudBackup.Password.EmptyBody"));
                return;
            }

            // Flush settings first so the credential key reflects the
            // provider/host/username the user just typed.
            await _settingsService.SaveAsync();
            await App.Current.CloudBackupService.SaveCredentialAsync(password);
            if (generation != ViewModel.CloudBackupEndpointGeneration)
            {
                return; // endpoint moved on — don't stamp "saved" for it
            }

            CloudBackupPasswordBox.Password = string.Empty;
            ViewModel.CloudBackupCredentialSaved = true;
            ViewModel.CloudBackupConnectionStatusText =
                _localizationService.T("Settings.CloudBackup.Password.Saved");

            // First-run flow: the moment a credential lands, populate the
            // snapshot list so the user sees what's already up there —
            // entry auto-refresh only runs on section navigation.
            await RefreshRemoteSnapshotsAsync(generation);
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Saving credential failed: {ex}");
            ViewModel.CloudBackupConnectionStatusText = _localizationService.Format(
                "Settings.CloudBackup.Password.SaveFailed",
                ex.Message);
        }
        finally
        {
            ViewModel.CloudBackupBusy = false;
        }
    }

    private async void CloudBackupTestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy)
        {
            return;
        }

        ViewModel.CloudBackupBusy = true;
        long generation = ViewModel.CloudBackupEndpointGeneration;
        ViewModel.CloudBackupConnectionStatusText = string.Empty;
        try
        {
            await _settingsService.SaveAsync();
            // A just-typed password probes that value directly — otherwise
            // "test" would silently exercise the previously stored secret.
            string typedPassword = CloudBackupPasswordBox.Password;
            await App.Current.CloudBackupService.ProbeConnectionAsync(
                string.IsNullOrEmpty(typedPassword) ? null : typedPassword);
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupConnectionStatusText =
                    _localizationService.T("Settings.CloudBackup.TestConnection.Success");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Connection test failed: {ex}");
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupConnectionStatusText = _localizationService.Format(
                    "Settings.CloudBackup.TestConnection.Failed",
                    ex.Message);
            }
        }
        finally
        {
            ViewModel.CloudBackupBusy = false;
        }
    }

    private async void CloudBackupNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy)
        {
            return;
        }

        ViewModel.CloudBackupBusy = true;
        long generation = ViewModel.CloudBackupEndpointGeneration;
        try
        {
            await _settingsService.SaveAsync();
            CloudBackupRunResult result = await App.Current.CloudBackupService.RunBackupNowAsync();
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                // The snapshot-list refresh itself comes from the
                // BackupRunCompleted event (delayed verify-and-retry).
                string prunedSuffix = result.PrunedCount > 0
                    ? " " + _localizationService.Format(
                        "Settings.CloudBackup.BackupNow.PrunedSuffix", result.PrunedCount)
                    : string.Empty;
                string unverifiedSuffix = result.UploadUnverified
                    ? " " + _localizationService.T(
                        "Settings.CloudBackup.BackupNow.UnverifiedSuffix")
                    : string.Empty;
                ViewModel.CloudBackupConnectionStatusText = result.Uploaded
                    ? _localizationService.Format(
                        "Settings.CloudBackup.BackupNow.Success",
                        result.RemoteFilePath ?? string.Empty) + prunedSuffix + unverifiedSuffix
                    : result.AlreadyInProgress
                        ? _localizationService.T("Settings.CloudBackup.BackupNow.InProgress")
                        : result.RestorePending
                            ? _localizationService.T("Settings.CloudBackup.RestorePending")
                            : result.NoCredential
                                ? _localizationService.T("Settings.CloudBackup.Password.NotSaved")
                                : result.NoScopeSelected
                                    ? _localizationService.T("Settings.CloudBackup.NoScope")
                                    : _localizationService.T("Settings.CloudBackup.NotConfigured");
            }

            ViewModel.RefreshCloudBackupStatus();
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Manual backup failed: {ex}");
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupConnectionStatusText = _localizationService.Format(
                    "Settings.CloudBackup.BackupNow.Failed",
                    ex.Message);
            }
        }
        finally
        {
            ViewModel.CloudBackupBusy = false;
        }
    }

    private async void CloudBackupRefreshSnapshotsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy)
        {
            return;
        }

        ViewModel.CloudBackupBusy = true;
        long generation = ViewModel.CloudBackupEndpointGeneration;
        try
        {
            await _settingsService.SaveAsync();
            await RefreshRemoteSnapshotsAsync(generation);
        }
        finally
        {
            ViewModel.CloudBackupBusy = false;
        }
    }

    // Reentrancy guard for the refresh paths: the manual button holds
    // CloudBackupBusy, but the automatic entry-refresh does not, so two
    // PROPFINDs could otherwise overlap (auto in flight + manual click).
    private bool _cloudBackupSnapshotRefreshInFlight;

    /// <summary>
    /// Fetches and repopulates the remote snapshot list. Returns the raw
    /// listing on success; null when skipped (another refresh in flight),
    /// blocked (no credential), stale (endpoint moved on), or failed —
    /// the delayed verification loop retries on null.
    /// </summary>
    private async Task<IReadOnlyList<CloudBackupRemoteEntry>?> RefreshRemoteSnapshotsAsync(long generation)
    {
        if (_cloudBackupSnapshotRefreshInFlight)
        {
            return null;
        }

        _cloudBackupSnapshotRefreshInFlight = true;
        try
        {
            // Endpoint set but no saved credential — an anonymous PROPFIND
            // would just 401. Point at the password field instead, and
            // clear the flag so the credential row can't disagree with
            // what the vault actually holds (e.g. externally deleted).
            if (App.Current.CloudBackupService.Options.HasEndpoint &&
                !await App.Current.CloudBackupService.HasCredentialAsync())
            {
                if (generation == ViewModel.CloudBackupEndpointGeneration)
                {
                    ViewModel.CloudBackupCredentialSaved = false;
                    ViewModel.CloudBackupConnectionStatusText =
                        _localizationService.T("Settings.CloudBackup.Password.NotSaved");
                    // The previously fetched list belongs to a credential
                    // that no longer exists — showing it next to "no
                    // password saved" reads like it was just listed.
                    ViewModel.CloudBackupRemoteSnapshots.Clear();
                }

                return null;
            }

            IReadOnlyList<CloudBackupRemoteEntry> snapshots =
                await App.Current.CloudBackupService.ListRemoteSnapshotsAsync();

            // A slow PROPFIND can return after the user repointed the
            // page — the list it fetched belongs to the OLD endpoint.
            if (generation != ViewModel.CloudBackupEndpointGeneration)
            {
                return null;
            }

            App.Log($"[CloudBackup] Listed {snapshots.Count} remote snapshot(s).");
            ViewModel.CloudBackupRemoteSnapshots.Clear();
            foreach (CloudBackupRemoteEntry entry in snapshots)
            {
                ViewModel.CloudBackupRemoteSnapshots.Add(
                    FormatRemoteSnapshot(entry));
            }

            SyncCloudBackupSnapshotList();
            // A fresh listing supersedes whatever the status line last
            // reported — leaving a stale "test failed"/"saved" message next
            // to a populated list misleads.
            ViewModel.CloudBackupConnectionStatusText = string.Empty;
            return snapshots;
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Listing remote snapshots failed: {ex}");
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupConnectionStatusText = _localizationService.Format(
                    "Settings.CloudBackup.RefreshSnapshots.Failed",
                    ex.Message);
            }

            return null;
        }
        finally
        {
            _cloudBackupSnapshotRefreshInFlight = false;
        }
    }

    /// <summary>
    /// Some WebDAV providers cache directory listings, so a completed PUT
    /// can take a moment to appear in PROPFIND. After an upload, refresh
    /// on a short delay and retry until the new snapshot shows up —
    /// bounded, so a lagging server degrades to a "list may be stale"
    /// hint rather than a list that never mentions the file at all.
    /// </summary>
    private async Task ScheduleRemoteSnapshotVerificationAsync(
        long generation,
        string expectedRemoteName)
    {
        bool listingSeen = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1500 : 3000);
            if (_isClosed || generation != ViewModel.CloudBackupEndpointGeneration)
            {
                return;
            }

            IReadOnlyList<CloudBackupRemoteEntry>? snapshots =
                await RefreshRemoteSnapshotsAsync(generation);
            if (snapshots is null)
            {
                // Another refresh in flight, transient fetch failure, or a
                // missing credential — the next attempt may still land.
                continue;
            }

            listingSeen = true;
            if (snapshots.Any(entry => string.Equals(
                    entry.Name, expectedRemoteName, StringComparison.Ordinal)))
            {
                return;
            }
        }

        // The server accepted the PUT but its listing still doesn't show
        // the file — say so instead of leaving a contradictory "success".
        if (listingSeen && !_isClosed &&
            generation == ViewModel.CloudBackupEndpointGeneration)
        {
            ViewModel.CloudBackupConnectionStatusText =
                _localizationService.T("Settings.CloudBackup.SnapshotList.NotYetVisible");
        }
    }

    /// <summary>
    /// One delayed refresh without name verification — used after a
    /// delete, where the optimistic removal already gave feedback and the
    /// follow-up PROPFIND just corrects if the server lagged.
    /// </summary>
    private async Task ScheduleRemoteSnapshotRefreshAsync(long generation)
    {
        await Task.Delay(1500);
        if (_isClosed || generation != ViewModel.CloudBackupEndpointGeneration)
        {
            return;
        }

        await RefreshRemoteSnapshotsAsync(generation);
    }

    /// <summary>
    /// Service-side completion hook: keeps an open page's status row and
    /// snapshot list honest when a SCHEDULED run finishes while the window
    /// is open — and covers the post-upload refresh for manual runs too.
    /// </summary>
    private void OnCloudBackupRunCompleted(CloudBackupRunCompletedInfo info)
    {
        // Runs can finish on arbitrary continuations — marshal back to
        // this window's queue before touching view state.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed)
            {
                return;
            }

            ViewModel.RefreshCloudBackupStatus();
            if (info.Uploaded && info.RemoteFilePath is { } remotePath)
            {
                int slash = remotePath.LastIndexOf('/');
                string remoteName = slash >= 0 ? remotePath[(slash + 1)..] : remotePath;
                _ = ScheduleRemoteSnapshotVerificationAsync(
                    ViewModel.CloudBackupEndpointGeneration,
                    remoteName);
            }
        });
    }

    // ItemsSource is assigned in code, not bound: pushing an
    // ObservableCollection<T> through {Binding} needs the WinRT
    // IObservableVector marshaling path, which silently yields an empty
    // list under Native AOT. A plain object[] snapshot needs no CCW —
    // same pattern as BackupSnapshotsList.
    private void SyncCloudBackupSnapshotList()
    {
        // Generic lookup — the untyped overload + `is` check would silently
        // skip the assignment if AOT returned a base-class RCW, recreating
        // the blank-list bug this code exists to fix.
        if (FindCreatedSectionElement<global::Microsoft.UI.Xaml.Controls.ListView>(
                "CloudBackupSettings", "CloudBackupSnapshotsList") is { } list)
        {
            list.ItemsSource = ViewModel.CloudBackupRemoteSnapshots.Count == 0
                ? null
                : ViewModel.CloudBackupRemoteSnapshots.Cast<object>().ToArray();
        }

        // Empty-state hint: an unexplained blank area reads as "still
        // loading" on first visit — say there is simply nothing remote yet.
        if (FindCreatedSectionElement<TextBlock>(
                "CloudBackupSettings", "CloudBackupSnapshotsEmptyHint") is { } emptyHint)
        {
            emptyHint.Visibility = ViewModel.CloudBackupRemoteSnapshots.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Maps service-layer backup errors to localized text. Exception
    /// messages from <see cref="DeskBoxDataBackupService"/> are English
    /// log strings, not UI copy.
    /// </summary>
    private string FormatBackupError(Exception ex)
    {
        if (ex.Data.Contains(DeskBoxDataBackupService.BackupSchemaVersionDataKey))
        {
            return _localizationService.Format(
                "Settings.DataBackup.RestoreFailedSchemaVersion",
                ex.Data[DeskBoxDataBackupService.BackupSchemaVersionDataKey] ?? string.Empty);
        }

        if (ex.Data.Contains(DeskBoxDataBackupService.BackupManifestInvalidDataKey))
        {
            return _localizationService.T("Settings.DataBackup.RestoreFailedInvalidManifest");
        }

        return ex.Message;
    }

    private CloudBackupRemoteSnapshotItem FormatRemoteSnapshot(CloudBackupRemoteEntry entry)
    {
        // New names embed UTC (…T…Z); legacy names embed local time — the
        // shared parser handles both so titles display correctly either way.
        string title = entry.Name;
        string details = entry.Name;
        if (CloudBackupService.ParseSnapshotTimestamp(entry.Name) is { } createdUtc)
        {
            title = createdUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            string stem = entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? entry.Name[..^4]
                : entry.Name;
            // An 8-char tail after the last '-' is the device suffix;
            // legacy unsuffixed names end in the timestamp itself instead.
            string tail = stem.Split('-').Last();
            string device = tail.Length == 8 ? tail : entry.Name;
            string size = entry.Length is { } length ? $" · {ViewModel.FormatBytes(length)}" : string.Empty;
            details = _localizationService.Format(
                "Settings.CloudBackup.SnapshotDetails",
                device,
                size);
        }

        return new CloudBackupRemoteSnapshotItem(entry.Name, title, details);
    }

    /// <summary>
    /// Localized label for one restored domain in the confirm dialog —
    /// "Todo data (12)". A manifest domain can legitimately hold zero live
    /// items (backup taken before data existed); the count makes that
    /// explicit instead of letting an empty domain silently wipe local data.
    /// WidgetStyle is a settings projection and shows no count.
    /// </summary>
    private string FormatRestoreDomainLabel(
        string manifestName,
        IReadOnlyList<DeskBoxDomainItemCount>? itemCounts)
    {
        string titleKey = manifestName switch
        {
            "todo-data" => "Settings.CloudBackup.TodoData.Title",
            "quick-capture-data" => "Settings.CloudBackup.QuickCaptureData.Title",
            "widget-style" => "Settings.CloudBackup.WidgetStyle.Title",
            _ => manifestName
        };
        string title = titleKey.StartsWith("Settings.", StringComparison.Ordinal)
            ? _localizationService.T(titleKey)
            : titleKey;
        int? items = itemCounts?
            .FirstOrDefault(c => string.Equals(c.Domain, manifestName, StringComparison.Ordinal))
            ?.Items;
        return items is { } count
            ? _localizationService.Format("Settings.CloudBackup.RestoreConfirm.DomainItems", title, count)
            : title;
    }

    private async void CloudBackupDeleteSnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy ||
            SettingsRoot.XamlRoot is null ||
            sender is not FrameworkElement { DataContext: CloudBackupRemoteSnapshotItem snapshot })
        {
            return;
        }

        // Remote deletes are irreversible — confirm first, in the same
        // style as every other destructive dialog in settings.
        var confirmDialog = new ContentDialog
        {
            XamlRoot = SettingsRoot.XamlRoot,
            Title = _localizationService.T("Settings.CloudBackup.DeleteSnapshot.Title"),
            PrimaryButtonText = _localizationService.T("Common.Delete"),
            CloseButtonText = _localizationService.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = _localizationService.Format(
                    "Settings.CloudBackup.DeleteSnapshot.Body",
                    snapshot.Title),
                TextWrapping = TextWrapping.Wrap
            }
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.CloudBackupBusy = true;
        long generation = ViewModel.CloudBackupEndpointGeneration;
        try
        {
            await _settingsService.SaveAsync();
            await App.Current.CloudBackupService.DeleteRemoteSnapshotAsync(snapshot.Name);
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                // Optimistic removal — the row vanishes immediately; the
                // delayed refresh corrects if the server listing lagged.
                ViewModel.CloudBackupRemoteSnapshots.Remove(snapshot);
                ViewModel.CloudBackupConnectionStatusText = string.Empty;
            }

            _ = ScheduleRemoteSnapshotRefreshAsync(generation);
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Deleting remote snapshot failed: {ex}");
            if (generation == ViewModel.CloudBackupEndpointGeneration)
            {
                ViewModel.CloudBackupConnectionStatusText = _localizationService.Format(
                    "Settings.CloudBackup.DeleteSnapshot.Failed",
                    ex.Message);
            }
        }
        finally
        {
            ViewModel.CloudBackupBusy = false;
        }
    }

    private async void CloudBackupRestoreSnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CloudBackupBusy ||
            SettingsRoot.XamlRoot is null ||
            sender is not FrameworkElement { DataContext: CloudBackupRemoteSnapshotItem snapshot })
        {
            return;
        }

        // The busy flag covers the WHOLE flow, domain picker included. A
        // second restore starting while the first is mid-download would
        // run PrepareScopedRestoreAsync, which deletes the shared pending
        // marker — the first dialog's confirm would then apply the SECOND
        // snapshot. Confirmed must equal applied.
        ViewModel.CloudBackupBusy = true;
        bool restartScheduled = false;
        string? downloadDirectory = null;
        try
        {
            // Step 1: pick the domains to restore (all on by default).
            var todoBox = new CheckBox
            {
                IsChecked = true,
                Content = _localizationService.T("Settings.CloudBackup.TodoData.Title")
            };
            var quickCaptureBox = new CheckBox
            {
                IsChecked = true,
                Content = _localizationService.T("Settings.CloudBackup.QuickCaptureData.Title")
            };
            var widgetStyleBox = new CheckBox
            {
                IsChecked = true,
                Content = _localizationService.T("Settings.CloudBackup.WidgetStyle.Title")
            };
            var domainDialog = new ContentDialog
            {
                XamlRoot = SettingsRoot.XamlRoot,
                Title = _localizationService.T("Settings.CloudBackup.RestoreDomains.Title"),
                PrimaryButtonText = _localizationService.T("Settings.CloudBackup.RestoreDomains.Continue"),
                CloseButtonText = _localizationService.T("Common.Cancel"),
                DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _localizationService.Format(
                                "Settings.CloudBackup.RestoreDomains.Body",
                                snapshot.Title),
                            TextWrapping = TextWrapping.Wrap
                        },
                        todoBox,
                        quickCaptureBox,
                        widgetStyleBox
                    }
                }
            };

            if (await domainDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            CloudBackupDomain scope = CloudBackupDomain.None;
            if (todoBox.IsChecked == true)
            {
                scope |= CloudBackupDomain.TodoData;
            }

            if (quickCaptureBox.IsChecked == true)
            {
                scope |= CloudBackupDomain.QuickCaptureData;
            }

            if (widgetStyleBox.IsChecked == true)
            {
                scope |= CloudBackupDomain.WidgetStyle;
            }

            if (scope == CloudBackupDomain.None)
            {
                return;
            }

            // Step 2: download → prepare scoped restore → confirm → relaunch.
            downloadDirectory = Path.Combine(
                Path.GetTempPath(),
                $"deskbox-cloud-restore-{Guid.NewGuid():N}");
            string archivePath = await App.Current.CloudBackupService.DownloadSnapshotAsync(
                snapshot.Name,
                downloadDirectory);

            DeskBoxRestorePreparation preparation =
                await App.Current.DataBackupService.PrepareScopedRestoreAsync(archivePath, scope);

            string domainList = preparation.Domains is { Count: > 0 } domains
                ? string.Join(", ", domains.Select(domain =>
                    FormatRestoreDomainLabel(domain, preparation.DomainItemCounts)))
                : _localizationService.T("Settings.CloudBackup.RestoreDomains.None");
            var bodyText = new System.Text.StringBuilder(_localizationService.Format(
                "Settings.CloudBackup.RestoreConfirm.Body",
                preparation.BackupCreatedAtUtc.ToLocalTime().ToString("g"),
                preparation.AppVersion,
                preparation.FileCount,
                ViewModel.FormatBytes(preparation.TotalUncompressedBytes),
                domainList));
            if (!string.IsNullOrEmpty(preparation.SourceDeviceId))
            {
                bodyText.Append(' ').Append(_localizationService.Format(
                    "Settings.CloudBackup.RestoreConfirm.SourceDevice",
                    preparation.SourceDeviceId));
            }

            if (preparation.TodoWidgetRemaps is { Count: > 0 } remaps)
            {
                // Count target widgets, not source stores — several source
                // stores can merge into the same live widget.
                bodyText.Append(' ').Append(_localizationService.Format(
                    "Settings.CloudBackup.RestoreConfirm.Remapped",
                    remaps.Select(r => r.TargetWidgetId)
                        .Distinct(StringComparer.Ordinal)
                        .Count()));
            }

            if (preparation.UnmappedTodoWidgetIds is { Count: > 0 } unmapped)
            {
                bodyText.Append(' ').Append(_localizationService.Format(
                    "Settings.CloudBackup.RestoreConfirm.Unmapped",
                    unmapped.Count));
            }

            // Item-restore mode is a real choice with destructive potential
            // on one side, so it gets its own labelled radio group — merge
            // is the safe default, overwrite warns explicitly on selection.
            var mergeModeRadio = new RadioButton
            {
                IsChecked = true,
                GroupName = "CloudBackupRestoreMode",
                Content = _localizationService.T("Settings.CloudBackup.RestoreConfirm.Mode.Merge")
            };
            var overwriteModeRadio = new RadioButton
            {
                GroupName = "CloudBackupRestoreMode",
                Content = _localizationService.T("Settings.CloudBackup.RestoreConfirm.Mode.Overwrite")
            };
            var overwriteWarning = new InfoBar
            {
                Severity = InfoBarSeverity.Warning,
                IsClosable = false,
                IsOpen = false,
                Message = _localizationService.T("Settings.CloudBackup.RestoreConfirm.OverwriteWarning")
            };
            mergeModeRadio.Checked += (_, _) => overwriteWarning.IsOpen = false;
            overwriteModeRadio.Checked += (_, _) => overwriteWarning.IsOpen = true;

            var confirmContent = new StackPanel { Spacing = 10 };
            confirmContent.Children.Add(new TextBlock
            {
                Text = bodyText.ToString(),
                TextWrapping = TextWrapping.Wrap
            });
            if (preparation.AttachmentReferenceCount > 0)
            {
                confirmContent.Children.Add(new InfoBar
                {
                    Severity = InfoBarSeverity.Warning,
                    IsClosable = false,
                    IsOpen = true,
                    Message = _localizationService.Format(
                        "Settings.CloudBackup.RestoreConfirm.AttachmentRefs",
                        preparation.AttachmentReferenceCount)
                });
            }

            if (preparation.IsFromNewerAppVersion)
            {
                confirmContent.Children.Add(new InfoBar
                {
                    Severity = InfoBarSeverity.Warning,
                    IsClosable = false,
                    IsOpen = true,
                    Message = _localizationService.Format(
                        "Settings.CloudBackup.RestoreConfirm.NewerVersion",
                        preparation.AppVersion)
                });
            }

            confirmContent.Children.Add(new TextBlock
            {
                Text = _localizationService.T("Settings.CloudBackup.RestoreConfirm.Mode.Title"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            confirmContent.Children.Add(mergeModeRadio);
            confirmContent.Children.Add(overwriteModeRadio);
            confirmContent.Children.Add(overwriteWarning);

            var confirmDialog = new ContentDialog
            {
                XamlRoot = SettingsRoot.XamlRoot,
                Title = _localizationService.T("Settings.CloudBackup.RestoreConfirm.Title"),
                PrimaryButtonText = _localizationService.T("Settings.CloudBackup.RestoreConfirm.Button"),
                CloseButtonText = _localizationService.T("Common.Cancel"),
                DefaultButton = ContentDialogButton.Close,
                Content = confirmContent
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                await App.Current.DataBackupService.CancelPendingRestoreAsync();
                return;
            }

            // Persist the chosen mode onto the pending marker — apply runs
            // after the restart and only has the marker to consult.
            if (!await App.Current.DataBackupService.SetPendingRestoreItemReplaceModeAsync(
                    overwriteModeRadio.IsChecked == true))
            {
                await App.Current.DataBackupService.CancelPendingRestoreAsync();
                throw new InvalidOperationException("The pending restore is no longer available.");
            }

            AppRelaunchScheduleResult relaunch = AppRelaunchService.ScheduleAfterCurrentProcessExit();
            if (!relaunch.Started)
            {
                await App.Current.DataBackupService.CancelPendingRestoreAsync();
                await ShowInfoDialogAsync(
                    _localizationService.T("Settings.DataBackup.RestartFailedTitle"),
                    _localizationService.Format(
                        "Settings.DataBackup.RestartFailedBody",
                        relaunch.ErrorMessage ?? string.Empty));
                return;
            }

            restartScheduled = true;
            await App.Current.ShutdownForRestartAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[CloudBackup] Remote restore failed: {ex}");
            if (!restartScheduled)
            {
                try
                {
                    await App.Current.DataBackupService.CancelPendingRestoreAsync();
                }
                catch (Exception cancelEx)
                {
                    App.Log($"[CloudBackup] Cancelling pending restore failed: {cancelEx}");
                }
            }

            await ShowInfoDialogAsync(
                _localizationService.T("Settings.CloudBackup.RestoreFailed.Title"),
                _localizationService.Format("Settings.CloudBackup.RestoreFailed.Body", FormatBackupError(ex)));
        }
        finally
        {
            // The staged archive was already extracted into the app's own
            // restore staging — the temp download must not linger either
            // way (it can be hundreds of MB).
            if (downloadDirectory is not null)
            {
                CloudBackupService.TryDeleteDirectory(downloadDirectory);
            }

            ViewModel.CloudBackupBusy = false;
        }
    }
}
