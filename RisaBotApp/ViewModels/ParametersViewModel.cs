using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// The parameters drawer — live ROS parameter tuning, ported from the web dashboard's
/// <c>.param-drawer</c> (dashboard_templates.py:768).
///
/// Two behaviours are worth stating because they are easy to break and hard to notice:
///
/// <b>Set is not save.</b> <c>/api/set_param</c> changes a running node's parameter and
/// nothing else — the next launch reads params.yaml and the value is gone. Only
/// <see cref="SaveDefaultsCommand"/> writes to disk, and it rewrites the file in the robot's
/// source tree, which then needs a colcon build to reach the install tree that actually runs.
/// So tuning is a three-step commitment (Set → Save → rebuild), and the drawer says so rather
/// than letting an operator assume the first step stuck.
///
/// <b>The drawer must not be modal.</b> The whole point of live tuning is watching the camera
/// overlay or the state pill react while a gain changes; a scrim over the telemetry would make
/// the feature useless. That is why this drawer has no backdrop, unlike the navigation one.
/// </summary>
public partial class ParametersViewModel : ObservableObject
{
    private readonly ApiClient     _api;
    private readonly DialogService _dialogs;

    public ParametersViewModel(ApiClient api, DialogService dialogs)
    {
        _api     = api;
        _dialogs = dialogs;

        Groups = [.. ParameterCatalog.Groups.Select(g => new ParameterGroupViewModel(api, g))];
    }

    /// <summary>Every section, collapsed. Nothing is fetched until one is expanded.</summary>
    public IReadOnlyList<ParameterGroupViewModel> Groups { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDefaultsCommand))]
    private bool _isSaving;

    [ObservableProperty] private string _saveStatus      = string.Empty;
    [ObservableProperty] private string _saveStatusColor = "#00C853";

    /// <summary>
    /// Writes every live parameter back into params.yaml on the robot.
    ///
    /// Confirmed first, and deliberately with the blunt wording the web version used. This
    /// overwrites a file in the git working tree on the robot — an operator who has been
    /// experimenting with values that happened to work on one carpet can silently commit them
    /// as the team's defaults. The rebuild caveat is in the prompt because without it the
    /// change looks applied when the next launch will still run the old install tree.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveDefaultsAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Save as defaults?",
            "Save ALL current runtime parameters as the new defaults in params.yaml?\n\n" +
            "This overwrites the file on the robot's disk. A rebuild (colcon build) is needed " +
            "for the change to take effect on the next launch.",
            "Overwrite",
            "Cancel");

        if (!confirmed) return;

        IsSaving   = true;
        SaveStatus = "Writing params.yaml…";
        SaveStatusColor = "#8899AA";

        try
        {
            var result = await _api.SaveParameterDefaultsAsync();

            if (!result.IsSuccess || result.Data is null)
            {
                SaveStatus      = result.ErrorMessage ?? "Save failed.";
                SaveStatusColor = "#FF5252";
                return;
            }

            // updated == 0 is a successful no-op, not a save. Saying "saved" there would have
            // an operator believe an edit landed when they never pressed Set on it.
            SaveStatus = result.Data.Updated == 0
                ? "Nothing to save — no parameter differs from params.yaml."
                : result.Data.Msg ?? $"Saved {result.Data.Updated} parameters.";

            SaveStatusColor = result.Data.Updated == 0 ? "#8899AA" : "#00C853";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => !IsSaving;

    /// <summary>Collapses every section. Cheap way back to the top of a 200-row drawer, and it
    /// leaves fetched values in place — re-expanding is instant.</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in Groups)
            group.IsExpanded = false;
    }
}
