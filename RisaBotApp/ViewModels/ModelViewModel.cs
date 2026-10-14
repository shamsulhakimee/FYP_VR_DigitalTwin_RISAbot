// Named explicitly: the WPF SDK's implicit usings do not include System.IO, so Path and
// FileInfo have to be brought in by hand here.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// The BPU model card on the Settings screen — replaces the robot's YOLO <c>.bin</c> without
/// an SFTP session.
///
/// This exists because the model is the one deployable artefact the normal workflow cannot
/// touch. It lives at <c>/home/sunrise/risabot_signs_640x640_nv12.bin</c>, *outside* the colcon
/// workspace, so `git pull` and `colcon build` never update it — which is why
/// tools/deploy_to_robots_paramiko.py carries a separate SFTP path just for this file. A team
/// that trains its own model otherwise needs paramiko, the robot's password and a working
/// laptop at the venue.
///
/// Three facts about the robot shape this whole screen, and each one is a trap if the UI hides
/// it:
///
/// <b>Uploading is not deploying.</b> <c>dnn.load()</c> runs once in
/// <c>SignageDetector.__init__</c>, so a model dropped in while the stack is up is inert. The
/// robot reports <c>restart_required</c> rather than this guessing at it, and the card says so
/// in as many words instead of implying the upload took effect.
///
/// <b>A model that loads can still be wrong.</b> <c>signage_detector.py</c> hardcodes class
/// indices 0-9 — <c>class_ids != 9</c> to drop a class, <c>cid in (6, 7, 8)</c> for the
/// traffic-light re-check, <c>1 in class_ids</c> for the hill. A model trained with a different
/// class *order* mismaps silently: it will detect confidently and act on the wrong sign. No
/// amount of validation here can catch that, so the card warns instead of pretending.
///
/// <b>Conversion cannot happen on the robot.</b> The openexplorer toolchain image is published
/// amd64-only, and the RDK X5 is ARM64 — so this screen deals exclusively in an already
/// compiled <c>.bin</c>, produced by tools/bpu_model/compile_model.bat on a PC.
/// </summary>
public partial class ModelViewModel : ObservableObject
{
    private readonly ApiClient     _api;
    private readonly DialogService _dialogs;

    public ModelViewModel(ApiClient api, DialogService dialogs)
    {
        _api     = api;
        _dialogs = dialogs;
    }

    // ── What is on the robot ─────────────────────────────────────────────────

    /// <summary>False before the first successful read, so the card can say "unknown" rather
    /// than "no model installed" — the two call for very different reactions.</summary>
    [ObservableProperty] private bool _isInfoKnown;

    [ObservableProperty] private bool _isModelPresent;

    [ObservableProperty] private string _robotModelName = "—";
    [ObservableProperty] private string _robotModelSize = "—";
    [ObservableProperty] private string _robotModelDate = "—";
    [ObservableProperty] private string _robotModelHash = "—";

    /// <summary>Full sha256, kept for comparison against the picked file. The card shows the
    /// short form; this is what decides whether an upload would be a no-op.</summary>
    private string? _robotModelHashFull;

    [ObservableProperty] private bool _isLaunchActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RollbackCommand))]
    private bool _hasBackup;

    [ObservableProperty] private string _backupSummary = string.Empty;

    // ── The file the operator picked ─────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedPath;

    [ObservableProperty] private string _selectedName = string.Empty;
    [ObservableProperty] private string _selectedSize = string.Empty;
    [ObservableProperty] private string _selectedHash = string.Empty;

    public bool HasSelection => !string.IsNullOrEmpty(SelectedPath);

    /// <summary>
    /// True when the picked file is byte-identical to what the robot already has. Uploading it
    /// would work fine and change nothing, so the card offers a restart instead — the far more
    /// likely intent when someone re-picks the same file is "make it take effect".
    /// </summary>
    [ObservableProperty] private bool _isSelectionAlreadyInstalled;

    // ── Progress and status ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand), nameof(RefreshCommand),
                                nameof(BrowseCommand), nameof(RollbackCommand))]
    private bool _isBusy;

    /// <summary>0-1. Bound straight to the ProgressBar rather than animated: the whole value of
    /// a real progress bar here is that it stalls when the transfer stalls, and an animation
    /// would occupy the animation layer and outrank the binding anyway.</summary>
    [ObservableProperty] private double _uploadProgress;

    [ObservableProperty] private bool _isUploading;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Drives the status line's colour. Kept as a hex string to match the convention
    /// the rest of the app's cards already use with StringToBrush.</summary>
    [ObservableProperty] private string _statusColor = "#8899AA";

    /// <summary>
    /// Whether the model on disk is newer than the running stack. Set from the robot's
    /// <c>restart_required</c> after an upload, and the reason the card keeps nagging until the
    /// operator restarts.
    /// </summary>
    [ObservableProperty] private bool _isRestartPending;

    /// <summary>Restart risabot.service as part of the upload. On by default: an upload that
    /// does not take effect is almost never what was wanted, and the alternative is an operator
    /// wondering why their new model changed nothing.</summary>
    [ObservableProperty] private bool _restartAfterUpload = true;

    private void SetStatus(string message, string color)
    {
        StatusMessage = message;
        StatusColor   = color;
    }

    // ── Reading the robot's state ────────────────────────────────────────────

    /// <summary>Called when the Settings screen loads. Cheap enough to repeat, but only the
    /// first visit needs it, so a known-good read is not re-fetched.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!IsInfoKnown && !IsBusy)
            await RefreshAsync();
    }

    /// <summary>The Refresh button: a read the operator asked for, so it narrates itself.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() => LoadAsync(announce: true);

    /// <summary>
    /// Re-reads the robot's model state.
    ///
    /// <paramref name="announce"/> is what keeps upload and rollback from erasing their own
    /// result. Those commands finish by re-reading the robot to prove the bytes landed, and a
    /// verification pass that narrated itself would overwrite "Installed and the stack
    /// restarted" with "Reading the robot's model…" and then with nothing at all — throwing away
    /// the only confirmation the operator gets. A refresh the operator asked for still speaks up,
    /// and either way a *failed* read overrides the message, because at that point the success
    /// line is no longer trustworthy.
    /// </summary>
    private async Task LoadAsync(bool announce)
    {
        IsBusy = true;
        if (announce) SetStatus("Reading the robot's model…", "#8899AA");

        try
        {
            var result = await _api.GetModelInfoAsync();

            if (!result.IsSuccess || result.Data is null)
            {
                IsInfoKnown = false;
                SetStatus(result.ErrorMessage ?? "Could not reach the robot.", "#FF5252");
                return;
            }

            var info = result.Data;
            IsInfoKnown    = true;
            IsModelPresent = info.Present;
            IsLaunchActive = info.LaunchActive;

            HasBackup = info.Backup?.Present == true;
            BackupSummary = HasBackup
                ? $"Previous model kept ({FormatSize(info.Backup!.Size)}, {info.Backup.MTimeIso})"
                : string.Empty;

            if (!info.Present)
            {
                _robotModelHashFull = null;
                RobotModelName = "Not installed";
                RobotModelSize = "—";
                RobotModelDate = "—";
                RobotModelHash = "—";
                SetStatus(
                    $"No model at {info.Path}. The signage detector cannot start without it.",
                    "#FF5252");
            }
            else
            {
                _robotModelHashFull = info.Sha256;
                RobotModelName = info.Name ?? Path.GetFileName(info.Path);
                RobotModelSize = FormatSize(info.Size);
                RobotModelDate = info.MTimeIso ?? "—";
                RobotModelHash = ShortHash(info.Sha256);
                if (announce) SetStatus(string.Empty, "#8899AA");
            }

            // A file picked before this refresh may now match (or no longer match) what is
            // installed, so the "already installed" hint is re-derived rather than left stale.
            RecomputeSelectionMatch();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAct() => !IsBusy;

    // ── Picking a file ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens the file picker, then hashes the choice so the card can show what will be sent and
    /// spot a re-pick of the already-installed model.
    ///
    /// It starts in tools/bpu_model/model_output when that folder exists, because that is where
    /// compile_model.bat puts its output and hunting for it is the first friction point.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select a compiled BPU model",
            Filter = "BPU model (*.bin)|*.bin|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        var guess = GuessModelOutputDir();
        if (guess is not null)
            dialog.InitialDirectory = guess;

        if (dialog.ShowDialog() != true) return;

        var path = dialog.FileName;
        var file = new FileInfo(path);

        SelectedPath = path;
        SelectedName = file.Name;
        SelectedSize = FormatSize(file.Length);
        SelectedHash = "hashing…";

        // Warn on the mistake the extension cannot catch. The robot rejects an ONNX by its
        // header anyway, but saying so before a 30-second upload is kinder.
        if (file.Extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            SelectedHash = "—";
            SetStatus(
                "That is an ONNX file. It has to be compiled to a .bin first — run " +
                "tools/bpu_model/compile_model.bat and pick model_output/*.bin.",
                "#FF5252");
            return;
        }

        try
        {
            var hash = await ApiClient.ComputeSha256Async(path);
            SelectedHash = ShortHash(hash);
            _selectedHashFull = hash;
            RecomputeSelectionMatch();

            SetStatus(
                IsSelectionAlreadyInstalled
                    ? "This is already the model on the robot — byte for byte. Restart the " +
                      "stack if it has not taken effect yet."
                    : string.Empty,
                IsSelectionAlreadyInstalled ? "#FFB300" : "#8899AA");
        }
        catch (Exception ex)
        {
            SelectedHash = "—";
            _selectedHashFull = null;
            SetStatus($"Could not read {file.Name}: {ex.Message}", "#FF5252");
        }
    }

    private string? _selectedHashFull;

    private void RecomputeSelectionMatch() =>
        IsSelectionAlreadyInstalled =
            _selectedHashFull is not null &&
            _robotModelHashFull is not null &&
            string.Equals(_selectedHashFull, _robotModelHashFull, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// tools/bpu_model/model_output inside the repo, when the app is running from a build tree.
    /// A guess, and treated as one — the picker just opens somewhere less annoying when it is
    /// right, and falls back to the OS default when it is not.
    /// </summary>
    private static string? GuessModelOutputDir()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "bpu_model", "model_output");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch
        {
            // A permission fault while probing for a convenience path is not worth surfacing.
        }

        return null;
    }

    // ── Uploading ────────────────────────────────────────────────────────────

    private bool CanUpload() => !IsBusy && HasSelection;

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (SelectedPath is null) return;

        // The confirmation is not ceremony. Replacing the model while the stack is running and
        // restarting it drops the camera stream and every node with it, which is a genuinely bad
        // surprise if the robot is mid-run on a track.
        var warning = RestartAfterUpload && IsLaunchActive
            ? "\n\nThe stack is running and will be restarted — the camera feed and all nodes " +
              "drop for a few seconds."
            : string.Empty;

        var confirmed = await _dialogs.ConfirmAsync(
            "Replace the robot's model?",
            $"{SelectedName} ({SelectedSize}) will replace " +
            $"{(IsModelPresent ? RobotModelName : "the missing model")} on the robot.\n\n" +
            "The current model is kept, so this can be rolled back from here." + warning,
            "Upload", "Cancel");

        if (!confirmed) return;

        IsBusy         = true;
        IsUploading    = true;
        UploadProgress = 0;
        SetStatus($"Uploading {SelectedName}…", "#FFB300");

        try
        {
            // Progress arrives off a worker thread; IProgress marshals it back through the
            // dispatcher captured here, which is why this is constructed on the UI thread.
            var progress = new Progress<double>(f =>
            {
                UploadProgress = f;
                StatusMessage  = $"Uploading {SelectedName}… {f * 100:0}%";
            });

            var result = await _api.UploadModelAsync(SelectedPath, RestartAfterUpload, progress);

            if (!result.IsSuccess || result.Data is null)
            {
                UploadProgress = 0;
                SetStatus(result.ErrorMessage ?? "Upload failed.", "#FF5252");
                return;
            }

            var data = result.Data;
            UploadProgress   = 1;
            IsRestartPending = data.RestartRequired;

            if (!string.IsNullOrWhiteSpace(data.Warning))
            {
                SetStatus(data.Warning!, "#FFB300");
            }
            else if (data.Restarted)
            {
                SetStatus(
                    $"Installed and the stack restarted — {FormatSize(data.Size)} is live.",
                    "#00C853");
            }
            else if (data.RestartRequired)
            {
                SetStatus(
                    "Installed. It is not in use yet — the detector only reads the model when " +
                    "it starts, so restart the stack to load it.",
                    "#FFB300");
            }
            else
            {
                SetStatus($"Installed — {FormatSize(data.Size)}.", "#00C853");
            }
        }
        finally
        {
            IsUploading = false;
            IsBusy      = false;
        }

        // Re-read rather than trusting the response: this is what proves the bytes on the
        // robot's disk are the ones that were sent, and it refreshes the backup state that
        // Rollback depends on. Silent, so it does not wipe the outcome message set above.
        await LoadAsync(announce: false);
    }

    // ── Rollback and restart ─────────────────────────────────────────────────

    private bool CanRollback() => !IsBusy && HasBackup;

    [RelayCommand(CanExecute = nameof(CanRollback))]
    private async Task RollbackAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Roll back to the previous model?",
            "The model currently installed is swapped with the one it replaced. This can be " +
            "undone by rolling back again." +
            (IsLaunchActive ? "\n\nThe running stack will be restarted." : string.Empty),
            "Roll back", "Cancel");

        if (!confirmed) return;

        IsBusy = true;
        SetStatus("Rolling back…", "#FFB300");

        try
        {
            var result = await _api.RollbackModelAsync(restartAfter: IsLaunchActive);

            if (!result.IsSuccess || result.Data is null)
            {
                SetStatus(result.ErrorMessage ?? "Rollback failed.", "#FF5252");
                return;
            }

            IsRestartPending = result.Data.RestartRequired;
            SetStatus(
                result.Data.Restarted
                    ? "Rolled back and the stack restarted."
                    : "Rolled back. Restart the stack to load it.",
                result.Data.Restarted ? "#00C853" : "#FFB300");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(announce: false);
    }

    /// <summary>
    /// Restarts risabot.service on its own, for the "uploaded earlier, restarting now" case and
    /// for anyone who cleared <see cref="RestartAfterUpload"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RestartStackAsync()
    {
        if (!await _dialogs.ConfirmAsync(
                "Restart the robot's stack?",
                "Every ROS 2 node stops and starts again. The camera feed drops for a few " +
                "seconds and any run in progress ends.",
                "Restart", "Cancel"))
            return;

        IsBusy = true;
        SetStatus("Restarting the stack…", "#FFB300");

        try
        {
            var result = await _api.RestartRosAsync();

            if (!result.IsSuccess || result.Data is null)
            {
                SetStatus(result.ErrorMessage ?? "Restart failed.", "#FF5252");
                return;
            }

            var active = result.Data.Status.Trim() == "active";
            IsRestartPending = !active;
            SetStatus(
                active
                    ? "Stack restarted — the model on disk is the one running."
                    : $"Stack did not come back up (state: {result.Data.Status}). Check the Logs tab.",
                active ? "#00C853" : "#FF5252");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(announce: false);
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024        => $"{bytes / 1024.0:0.0} KB",
        _              => $"{bytes} B",
    };

    /// <summary>First and last six of a sha256. Nobody reads 64 hex characters off a screen,
    /// but the ends are enough to tell two builds apart at a glance.</summary>
    private static string ShortHash(string? hash) =>
        string.IsNullOrWhiteSpace(hash) || hash.Length < 16
            ? "—"
            : $"{hash[..6]}…{hash[^6..]}";
}
