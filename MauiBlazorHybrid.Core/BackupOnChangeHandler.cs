namespace MauiBlazorHybrid.Core;

/// <summary>
/// Pure coordination logic for backup-on-change.
/// Decides whether a backup should run when product data changes
/// and delegates to the provided callback. Lives in Core so the
/// decision logic is unit-testable without MAUI dependencies.
/// </summary>
public class BackupOnChangeHandler
{
    private readonly Func<bool> _isEnabled;
    private readonly Func<Task<bool>> _testConnection;
    private readonly Func<Task<bool>> _runBackup;
    private readonly Action<string>? _log;

    /// <summary>
    /// Creates a new handler.
    /// </summary>
    /// <param name="isEnabled">Returns true when backup-on-change is enabled in settings.</param>
    /// <param name="testConnection">Tests whether the backup server is reachable.</param>
    /// <param name="runBackup">Executes the actual backup. Returns true on success.</param>
    /// <param name="log">Optional logging callback.</param>
    public BackupOnChangeHandler(
        Func<bool> isEnabled,
        Func<Task<bool>> testConnection,
        Func<Task<bool>> runBackup,
        Action<string>? log = null)
    {
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _testConnection = testConnection ?? throw new ArgumentNullException(nameof(testConnection));
        _runBackup = runBackup ?? throw new ArgumentNullException(nameof(runBackup));
        _log = log;
    }

    /// <summary>
    /// Synchronous entry point intended for event subscription (fire-and-forget).
    /// Checks the enabled flag and, if set, kicks off <see cref="HandleAsync"/> on a background task.
    /// </summary>
    public void OnDataChanged()
    {
        if (!_isEnabled()) return;
        _ = HandleAsync();
    }

    /// <summary>
    /// Awaitable entry point that performs the full backup-on-change flow:
    /// check enabled → test connection → run backup.
    /// Returns true only when a backup was actually executed successfully.
    /// </summary>
    public async Task<bool> HandleAsync()
    {
        if (!_isEnabled())
        {
            return false;
        }

        try
        {
            _log?.Invoke("Backup-on-change triggered");

            var connected = await _testConnection();
            if (!connected)
            {
                _log?.Invoke("Backup-on-change skipped: server not reachable");
                return false;
            }

            var result = await _runBackup();
            return result;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Backup-on-change failed: {ex.Message}");
            return false;
        }
    }
}
