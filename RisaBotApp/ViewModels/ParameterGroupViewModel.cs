using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// One collapsible section of the parameters drawer.
///
/// Values are fetched on first expand, not on open. There are around 200 parameters across
/// seventeen groups and no batch endpoint — <c>/api/get_param</c> answers one name at a time,
/// each a round trip plus a ROS parameter service call — so reading everything up front would
/// stall the drawer for the better part of a minute to populate boxes the operator will not
/// look at. Expanding a group of 26 costs a couple of seconds, which is the right place to
/// spend it.
///
/// Crucially, the fetch list is <see cref="ParamGroup.Params"/> from this instance, not a
/// lookup by node name. The web dashboard looks its group up with
/// <c>PARAM_GROUPS.find(g =&gt; g.node === nodeName)</c>, so with <c>auto_driver</c> owning
/// three groups, expanding Hill Climb loads the Auto Driver list and the hill parameters stay
/// blank forever.
/// </summary>
public partial class ParameterGroupViewModel : ObservableObject
{
    /// <summary>
    /// Whether the initial read completed with every row answered. Left false on a partial or
    /// failed read so that collapsing and re-expanding retries, rather than leaving the group
    /// permanently half-populated because the launch happened to be down the first time.
    /// </summary>
    private bool _loadedOk;

    /// <summary>Whether defaults have been captured yet — they only come back on the first
    /// read, and only for parameters params.yaml actually mentions.</summary>
    private bool _defaultsCaptured;

    public ParameterGroupViewModel(ApiClient api, ParamGroup group)
    {
        Node  = group.Node;
        Label = group.Label;
        Rows  = [.. group.Params.Select(spec => new ParameterRowViewModel(api, group.Node, spec))];
    }

    public string Node  { get; }
    public string Label { get; }

    public IReadOnlyList<ParameterRowViewModel> Rows { get; }

    /// <summary>Shown in the header, so the cost of expanding is visible before clicking.</summary>
    public string CountText => Rows.Count == 1 ? "1 param" : $"{Rows.Count} params";

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _loadedCount;

    /// <summary>Group-level failure, as opposed to one row's. Set when the read finished with
    /// rows still unanswered, which in practice means the node is not running.</summary>
    [ObservableProperty] private string _errorText = string.Empty;

    public string ProgressText => IsLoading ? $"reading {LoadedCount}/{Rows.Count}…" : string.Empty;

    /// <summary>Bound by the header; the collapsed/expanded chevron follows the same flag.</summary>
    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>Re-read every row in this group. Available once expanded, for when the robot
    /// has been restarted underneath the drawer.</summary>
    [RelayCommand]
    private Task Refresh() => LoadAsync();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loadedOk && !IsLoading)
            _ = LoadAsync();
    }

    /// <summary>
    /// Reads the group's parameters one at a time.
    ///
    /// Sequentially on purpose. The dashboard's HTTP server is threaded, so 26 parallel
    /// requests would be accepted — and would then pile 26 concurrent parameter service calls
    /// onto the single-threaded rclpy executor that is also servicing the 50 Hz control loop.
    /// A drawer that is a second slower to fill is a much better trade than a control loop
    /// that stutters while it fills.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading   = true;
        LoadedCount = 0;
        ErrorText   = string.Empty;

        try
        {
            foreach (var row in Rows)
            {
                // quiet: the per-row ✓ is for deliberate single-row actions. Twenty-six of
                // them flashing green at once says nothing the filled boxes do not.
                await row.LoadAsync(initialLoad: !_defaultsCaptured, quiet: true);
                LoadedCount++;
            }

            _defaultsCaptured = true;

            var unanswered = Rows.Count(r => r.LiveValue.Length == 0);
            _loadedOk = unanswered == 0;

            if (unanswered == Rows.Count)
                ErrorText = $"No response from {Node}. Is the launch running?";
            else if (unanswered > 0)
                ErrorText = $"{unanswered} of {Rows.Count} parameters did not answer.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
