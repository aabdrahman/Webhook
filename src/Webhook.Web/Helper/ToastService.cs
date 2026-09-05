namespace Webhook.Web.Services;

/// <summary>
/// Defines the visual type of a toast notification.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

/// <summary>
/// Represents a single toast notification.
/// </summary>
public sealed class ToastMessage
{
    public Guid      Id      { get; init; } = Guid.NewGuid();
    public ToastType Type    { get; init; } = ToastType.Info;
    public string    Title   { get; init; } = string.Empty;
    public string    Message { get; init; } = string.Empty;

    /// <summary>
    /// How long the toast stays visible before auto-dismissing.
    /// Defaults to 4 seconds.
    /// </summary>
    public int DurationMs { get; init; } = 4000;
}

/// <summary>
/// Singleton service that manages the toast notification queue.
/// Inject this anywhere and call Show(...) to display a toast.
/// ToastContainer subscribes to OnToastsChanged and renders the queue.
/// </summary>
public sealed class ToastService
{
    private readonly List<ToastMessage> _toasts = [];

    /// <summary>
    /// Raised whenever the toast queue changes.
    /// ToastContainer subscribes to this to re-render.
    /// </summary>
    public event Action? OnToastsChanged;

    /// <summary>
    /// Current snapshot of the toast queue — read-only.
    /// </summary>
    public IReadOnlyList<ToastMessage> Toasts => _toasts.AsReadOnly();

    // -------------------------------------------------------------------------
    // Show helpers
    // -------------------------------------------------------------------------

    public void ShowSuccess(string message, string title = "Success",    int durationMs = 4000)
        => Show(ToastType.Success, title, message, durationMs);

    public void ShowError(string message,   string title = "Error",      int durationMs = 5000)
        => Show(ToastType.Error,   title, message, durationMs);

    public void ShowWarning(string message, string title = "Warning",    int durationMs = 5000)
        => Show(ToastType.Warning, title, message, durationMs);

    public void ShowInfo(string message,    string title = "Info",       int durationMs = 4000)
        => Show(ToastType.Info,    title, message, durationMs);

    /// <summary>
    /// Adds a toast to the queue and schedules its automatic removal.
    /// </summary>
    public void Show(
        ToastType type,
        string    title,
        string    message,
        int       durationMs = 4000)
    {
        var toast = new ToastMessage
        {
            Type       = type,
            Title      = title,
            Message    = message,
            DurationMs = durationMs
        };

        _toasts.Add(toast);
        NotifyChanged();

        // Schedule auto-dismiss
        _ = AutoDismissAsync(toast.Id, durationMs);
    }

    /// <summary>
    /// Manually removes a toast by its ID — called by the dismiss button.
    /// </summary>
    public void Remove(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast is null) return;

        _toasts.Remove(toast);
        NotifyChanged();
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private async Task AutoDismissAsync(Guid id, int durationMs)
    {
        await Task.Delay(durationMs);
        Remove(id);
    }

    private void NotifyChanged()
        => OnToastsChanged?.Invoke();
}
