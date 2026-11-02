using System;
using System.Collections.Generic;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// How the main window appears on Windows logon.
    /// </summary>
    public enum StartupWindowMode
    {
        Normal,
        Minimized,
        Tray
    }

    /// <summary>
    /// Authoritative startup registration state (desired vs actual).
    /// </summary>
    public enum StartupState
    {
        Disabled,
        Registry,
        ElevatedTask,
        Conflict,
        Broken
    }

    public enum StartupReason
    {
        MissingRegistry,
        MissingScheduledTask,
        WrongExecutable,
        WrongArguments,
        ExecutableMissing,
        TaskDisabled,
        MissingLogonTrigger,
        MissingElevatedRunLevel,
        UnexpectedRegistryEntry,
        UnexpectedScheduledTask,
        ReadFailed
    }

    public enum StartupOperation
    {
        RemoveRegistry,
        RemoveScheduledTask,
        CreateOrUpdateRegistry,
        CreateOrUpdateScheduledTask
    }

    public sealed record DesiredStartupConfiguration(bool StartWithWindows, StartupWindowMode WindowMode, bool RunElevated)
    {
        public string Arguments => WindowMode switch
        {
            StartupWindowMode.Minimized => "--minimized",
            StartupWindowMode.Tray => "--tray",
            _ => string.Empty
        };
    }

    public sealed record RegistryStartupSnapshot(
        bool Exists,
        string? ExecutablePath,
        string Arguments,
        bool ExecutableExists,
        bool IsExpectedExecutable,
        bool IsExpectedArguments,
        bool ReadSucceeded = true,
        string? Error = null);

    public sealed record ScheduledTaskStartupSnapshot(
        bool Exists,
        bool Enabled,
        string? ExecutablePath,
        string Arguments,
        bool ExecutableExists,
        bool HasLogonTrigger,
        bool IsElevated,
        bool IsExpectedExecutable,
        bool IsExpectedArguments,
        bool ReadSucceeded = true,
        string? Error = null);

    public sealed record ActualStartupConfiguration(
        RegistryStartupSnapshot Registry,
        ScheduledTaskStartupSnapshot Task);

    public sealed record StartupConfigurationEvaluation(
        StartupState State,
        IReadOnlyList<StartupReason> Reasons,
        IReadOnlyList<StartupOperation> RequiredOperations);

    public sealed record StartupOperationResult(
        bool Success,
        bool ElevationRequired = false,
        bool ElevationCancelled = false,
        string? Error = null);

    public sealed record StartupApplyResult(
        bool Success,
        bool WasElevationRequired,
        bool WasElevationCancelled,
        IReadOnlyList<StartupOperation> OperationsAttempted,
        StartupConfigurationEvaluation FinalEvaluation,
        string? Error = null);
}
