using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// One row of the parameters drawer: a name, an editable value, and Get/Set against the live
/// ROS node.
///
/// The edit box is deliberately free text rather than a typed control. ROS parameters coming
/// back from <c>/api/get_param</c> are bools, ints, doubles and strings through one untyped
/// field, and the setter stringifies whatever it is given
/// (<c>str(data['value'])</c> in dashboard.py) before handing it to the parameter service to
/// parse back. Guessing an editor from the current value would mean a checkbox appearing or
/// disappearing depending on what the robot last returned, and would make
/// <c>color_bumper</c>'s <c>"0,140,255"</c> unrepresentable. Validation is the robot's job;
/// its rejection message is shown in <see cref="StatusText"/>.
/// </summary>
public partial class ParameterRowViewModel : ObservableObject
{
    private readonly ApiClient _api;

    /// <summary>How long a ✓ or an error lingers before the row goes quiet again.
    /// Matches the web dashboard's 3 s.</summary>
    private const int StatusLingerMs = 3000;

    private const string OkColor    = "#00C853";
    private const string ErrorColor = "#FF5252";

    /// <summary>
    /// Guards the delayed status clear. Every status message takes the next token; the timer
    /// only clears the text if its own token is still the current one, so a Set that lands
    /// two seconds after a Get does not have its ✓ wiped by the Get's expiring timer.
    /// </summary>
    private int _statusToken;

    public ParameterRowViewModel(ApiClient api, string node, ParamSpec spec)
    {
        _api = api;
        Node = node;
        Name = spec.Name;
        Tip  = spec.Tip;
    }

    /// <summary>The node that owns this parameter. Not unique across the drawer — three groups
    /// share <c>auto_driver</c> — which is why rows are objects rather than keyed by
    /// node+name.</summary>
    public string Node { get; }

    public string Name { get; }

    /// <summary>Operator-facing description, shown as the row's tooltip.</summary>
    public string Tip { get; }

    /// <summary>What is in the edit box. Diverges from <see cref="LiveValue"/> as soon as the
    /// operator types, and converges again on a successful Set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingEdit))]
    private string _value = string.Empty;

    /// <summary>The last value the robot reported. Empty until the first successful Get.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingEdit))]
    [NotifyPropertyChangedFor(nameof(IsChangedFromDefault))]
    private string _liveValue = string.Empty;

    /// <summary>
    /// The packaged default from params.yaml, when the robot reports one. Empty for parameters
    /// the YAML does not mention — which is not an error, several nodes declare parameters the
    /// config file leaves at their in-code default.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultHint))]
    [NotifyPropertyChangedFor(nameof(IsChangedFromDefault))]
    private string _defaultValue = string.Empty;

    [ObservableProperty] private string _statusText  = string.Empty;
    [ObservableProperty] private string _statusColor = OkColor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetCommand))]
    private bool _isBusy;

    /// <summary>Shown beside the name, as the web version's <c>(def: X)</c> suffix was.</summary>
    public string DefaultHint =>
        string.IsNullOrEmpty(DefaultValue) ? string.Empty : $"def {DefaultValue}";

    /// <summary>The box holds something that has not been pushed to the robot yet.</summary>
    public bool IsPendingEdit =>
        LiveValue.Length > 0 && !string.Equals(Value.Trim(), LiveValue, StringComparison.Ordinal);

    /// <summary>
    /// The robot is running something other than what params.yaml packages — either because
    /// this drawer changed it, or because a previous session did and never saved. Worth
    /// flagging: it is the difference between "tuned" and "tuned and will survive a rebuild".
    /// </summary>
    public bool IsChangedFromDefault =>
        LiveValue.Length > 0 && DefaultValue.Length > 0 && !ValuesEqual(LiveValue, DefaultValue);

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Re-read this one parameter, overwriting whatever is in the box.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task GetAsync() => LoadAsync(initialLoad: false);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SetAsync()
    {
        var text = Value.Trim();
        if (text.Length == 0)
        {
            ShowStatus("empty", ErrorColor);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.SetParameterAsync(Node, Name, text);
            if (result.IsSuccess)
            {
                // Trust, then verify: the parameter service can accept the call and coerce
                // the value (0.5 into an int parameter becomes 0), so the row re-reads rather
                // than assuming the box is now the truth.
                ShowStatus("✓", OkColor);
                await LoadAsync(initialLoad: false, quiet: true);
            }
            else
            {
                ShowStatus(result.ErrorMessage ?? "failed", ErrorColor);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAct() => !IsBusy;

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the parameter and fills the box with it.
    /// </summary>
    /// <param name="initialLoad">
    /// True for the group's first expand, which also captures the default. Later reads leave
    /// <see cref="DefaultValue"/> alone — the endpoint keeps returning the same packaged
    /// default, but re-assigning it on every refresh would churn the change markers for no
    /// reason.
    /// </param>
    /// <param name="quiet">Suppress the status flash, for the read that follows a Set — the
    /// Set's own ✓ is the message the operator is looking at.</param>
    public async Task LoadAsync(bool initialLoad, bool quiet = false)
    {
        IsBusy = true;
        try
        {
            var result = await _api.GetParameterAsync(Node, Name);

            if (!result.IsSuccess || result.Data is null)
            {
                if (!quiet) ShowStatus(result.ErrorMessage ?? "unreachable", ErrorColor);
                return;
            }

            LiveValue = Stringify(result.Data.Value);
            Value     = LiveValue;

            if (initialLoad && result.Data.Default is { } def)
                DefaultValue = Stringify(def);

            if (!quiet) ShowStatus("✓", OkColor);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens the endpoint's untyped value into the text the edit box holds.
    /// <c>GetRawText</c> is used for numbers so the robot's own formatting survives — going
    /// through a double would turn <c>0.1</c> into <c>0.1</c> on most values but
    /// re-render some as <c>0.10000000000000001</c>, and the operator would see the drawer
    /// mangling numbers it had merely displayed.
    /// </summary>
    private static string Stringify(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String                        => element.GetString() ?? string.Empty,
        JsonValueKind.True                          => "true",
        JsonValueKind.False                         => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _                                           => element.GetRawText(),
    };

    /// <summary>
    /// Numeric-tolerant comparison, so <c>5</c> and <c>5.0</c> do not read as a change. The
    /// live value comes from a ROS parameter and the default from YAML, and the two paths do
    /// not always render the same number identically.
    /// </summary>
    private static bool ValuesEqual(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
            && Math.Abs(da - db) < 1e-9;
    }

    private void ShowStatus(string text, string color)
    {
        StatusText  = text;
        StatusColor = color;
        ClearStatusLater(++_statusToken);
    }

    /// <summary>
    /// async void is safe here: nothing inside can throw, and the continuation resumes on the
    /// dispatcher because this is only ever called from the UI thread.
    /// </summary>
    private async void ClearStatusLater(int token)
    {
        await Task.Delay(StatusLingerMs);
        if (_statusToken == token) StatusText = string.Empty;
    }
}
