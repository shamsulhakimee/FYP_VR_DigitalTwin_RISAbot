using System.Windows;
using System.Windows.Threading;

namespace RisaBotApp.Services;

/// <summary>
/// Replacement for MAUI's <c>MainThread</c>.
///
/// This matters far more in WPF than it did in MAUI. MAUI tolerated setting a bound
/// property from a background thread — the binding engine marshalled it for you. WPF does
/// not: touching a DispatcherObject off its owning thread throws InvalidOperationException,
/// and mutating a bound ObservableCollection off the UI thread throws
/// NotSupportedException on the CollectionView. Both are hard crashes, not glitches.
///
/// So every callback that starts on a socket, timer or HTTP continuation and ends at a
/// bound property has to come through here.
/// </summary>
public static class UiThread
{
    private static Dispatcher? Dispatcher => Application.Current?.Dispatcher;

    /// <summary>True when the caller is already on the UI thread.</summary>
    public static bool IsMain => Dispatcher?.CheckAccess() ?? true;

    /// <summary>
    /// Run <paramref name="action"/> on the UI thread, inline if already there.
    ///
    /// Fire-and-forget: the caller does not wait. Use this for state updates from a poll
    /// loop, where blocking the loop on the dispatcher would let a busy UI throttle polling.
    /// </summary>
    public static void Post(Action action)
    {
        var dispatcher = Dispatcher;

        // No Application means unit tests or a shutdown in flight — run inline and let
        // any real threading violation surface there rather than being silently swallowed.
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// Run <paramref name="action"/> on the UI thread and wait for it.
    /// For the rare case where the caller needs the update applied before continuing.
    /// </summary>
    public static void Send(Action action)
    {
        var dispatcher = Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    /// <summary>Awaitable form, for use inside async view-model methods.</summary>
    public static Task RunAsync(Action action)
    {
        var dispatcher = Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
