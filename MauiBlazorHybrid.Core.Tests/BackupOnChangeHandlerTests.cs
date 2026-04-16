using MauiBlazorHybrid.Core;

namespace MauiBlazorHybrid.Core.Tests;

public class BackupOnChangeHandlerTests
{
    #region OnDataChanged – enabled flag

    /// <summary>
    /// Test: OnDataChanged when backup-on-change is disabled.
    /// Assumptions: isEnabled returns false.
    /// Expectation: Neither testConnection nor runBackup is called.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Disabled_DoesNotTriggerBackup()
    {
        bool backupCalled = false;
        bool connectionTested = false;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => false,
            testConnection: () => { connectionTested = true; return Task.FromResult(true); },
            runBackup: () => { backupCalled = true; return Task.FromResult(true); });

        var result = await handler.HandleAsync();

        Assert.False(result);
        Assert.False(connectionTested);
        Assert.False(backupCalled);
    }

    /// <summary>
    /// Test: OnDataChanged when backup-on-change is enabled and server is reachable.
    /// Assumptions: isEnabled returns true, testConnection returns true.
    /// Expectation: runBackup is called and result is true.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EnabledAndConnected_TriggersBackup()
    {
        bool backupCalled = false;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: () => { backupCalled = true; return Task.FromResult(true); });

        var result = await handler.HandleAsync();

        Assert.True(result);
        Assert.True(backupCalled);
    }

    #endregion

    #region OnDataChanged – connection check

    /// <summary>
    /// Test: HandleAsync when enabled but server is not reachable.
    /// Assumptions: isEnabled returns true, testConnection returns false.
    /// Expectation: runBackup is never called, result is false.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EnabledButNotConnected_SkipsBackup()
    {
        bool backupCalled = false;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(false),
            runBackup: () => { backupCalled = true; return Task.FromResult(true); });

        var result = await handler.HandleAsync();

        Assert.False(result);
        Assert.False(backupCalled);
    }

    #endregion

    #region OnDataChanged – backup failure

    /// <summary>
    /// Test: HandleAsync when backup returns false (e.g. server error).
    /// Assumptions: isEnabled true, connected true, runBackup returns false.
    /// Expectation: Result is false.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BackupReturnsFalse_ReturnsFalse()
    {
        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: () => Task.FromResult(false));

        var result = await handler.HandleAsync();

        Assert.False(result);
    }

    /// <summary>
    /// Test: HandleAsync when testConnection throws an exception.
    /// Assumptions: isEnabled true, testConnection throws.
    /// Expectation: Exception is caught, result is false, log message contains exception text.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ConnectionThrows_ReturnsFalseAndLogs()
    {
        string? loggedMessage = null;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => throw new InvalidOperationException("network down"),
            runBackup: () => Task.FromResult(true),
            log: msg => loggedMessage = msg);

        var result = await handler.HandleAsync();

        Assert.False(result);
        Assert.NotNull(loggedMessage);
        Assert.Contains("network down", loggedMessage);
    }

    /// <summary>
    /// Test: HandleAsync when runBackup throws an exception.
    /// Assumptions: isEnabled true, connected true, runBackup throws.
    /// Expectation: Exception is caught, result is false, log message contains exception text.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BackupThrows_ReturnsFalseAndLogs()
    {
        string? loggedMessage = null;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: () => throw new IOException("disk full"),
            log: msg => loggedMessage = msg);

        var result = await handler.HandleAsync();

        Assert.False(result);
        Assert.NotNull(loggedMessage);
        Assert.Contains("disk full", loggedMessage);
    }

    #endregion

    #region OnDataChanged – synchronous entry point

    /// <summary>
    /// Test: OnDataChanged (sync entry point) does not call anything when disabled.
    /// Assumptions: isEnabled returns false.
    /// Expectation: No connection test, no backup.
    /// </summary>
    [Fact]
    public void OnDataChanged_Disabled_DoesNothing()
    {
        bool anythingCalled = false;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => false,
            testConnection: () => { anythingCalled = true; return Task.FromResult(true); },
            runBackup: () => { anythingCalled = true; return Task.FromResult(true); });

        handler.OnDataChanged();

        Assert.False(anythingCalled);
    }

    #endregion

    #region Logging

    /// <summary>
    /// Test: HandleAsync logs "triggered" message when enabled and starting.
    /// Assumptions: isEnabled true, connected true, backup succeeds.
    /// Expectation: At least one log message contains "triggered".
    /// </summary>
    [Fact]
    public async Task HandleAsync_Enabled_LogsTriggeredMessage()
    {
        var logs = new List<string>();

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: () => Task.FromResult(true),
            log: msg => logs.Add(msg));

        await handler.HandleAsync();

        Assert.Contains(logs, l => l.Contains("triggered"));
    }

    /// <summary>
    /// Test: HandleAsync logs "not reachable" when connection fails.
    /// Assumptions: isEnabled true, testConnection returns false.
    /// Expectation: Log message contains "not reachable".
    /// </summary>
    [Fact]
    public async Task HandleAsync_NotConnected_LogsSkippedMessage()
    {
        var logs = new List<string>();

        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(false),
            runBackup: () => Task.FromResult(true),
            log: msg => logs.Add(msg));

        await handler.HandleAsync();

        Assert.Contains(logs, l => l.Contains("not reachable"));
    }

    #endregion

    #region Constructor validation

    /// <summary>
    /// Test: Constructor throws when isEnabled is null.
    /// Expectation: ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_NullIsEnabled_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BackupOnChangeHandler(
            isEnabled: null!,
            testConnection: () => Task.FromResult(true),
            runBackup: () => Task.FromResult(true)));
    }

    /// <summary>
    /// Test: Constructor throws when testConnection is null.
    /// Expectation: ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_NullTestConnection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: null!,
            runBackup: () => Task.FromResult(true)));
    }

    /// <summary>
    /// Test: Constructor throws when runBackup is null.
    /// Expectation: ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_NullRunBackup_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: null!));
    }

    /// <summary>
    /// Test: Constructor accepts null log callback without throwing.
    /// Expectation: No exception.
    /// </summary>
    [Fact]
    public void Constructor_NullLog_DoesNotThrow()
    {
        var handler = new BackupOnChangeHandler(
            isEnabled: () => true,
            testConnection: () => Task.FromResult(true),
            runBackup: () => Task.FromResult(true),
            log: null);

        Assert.NotNull(handler);
    }

    #endregion

    #region Dynamic enabled flag

    /// <summary>
    /// Test: HandleAsync respects the enabled flag changing between calls.
    /// Assumptions: First call enabled=true, second call enabled=false.
    /// Expectation: First call triggers backup, second does not.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EnabledTogglesBetweenCalls_RespectsCurrentValue()
    {
        bool enabled = true;
        int backupCount = 0;

        var handler = new BackupOnChangeHandler(
            isEnabled: () => enabled,
            testConnection: () => Task.FromResult(true),
            runBackup: () => { backupCount++; return Task.FromResult(true); });

        var result1 = await handler.HandleAsync();
        Assert.True(result1);
        Assert.Equal(1, backupCount);

        enabled = false;

        var result2 = await handler.HandleAsync();
        Assert.False(result2);
        Assert.Equal(1, backupCount);
    }

    #endregion
}
