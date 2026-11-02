using ProcessCoreOptimizer.WPF.Models;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Pure, testable evaluation of desired vs actual startup registration.
    /// Operation order is safe by construction: the target channel is created
    /// before the stale channel is removed, so a failed create never leaves
    /// the user without any startup registration.
    /// </summary>
    public static class StartupConfigurationPlanner
    {
        public static StartupConfigurationEvaluation Evaluate(DesiredStartupConfiguration desired, ActualStartupConfiguration actual)
        {
            var reasons = new List<StartupReason>();
            var operations = new List<StartupOperation>();

            if (!actual.Registry.ReadSucceeded || !actual.Task.ReadSucceeded)
            {
                return new StartupConfigurationEvaluation(
                    StartupState.Broken, new[] { StartupReason.ReadFailed }, operations);
            }

            if (!desired.StartWithWindows)
            {
                if (actual.Registry.Exists)
                {
                    reasons.Add(StartupReason.UnexpectedRegistryEntry);
                    operations.Add(StartupOperation.RemoveRegistry);
                }

                if (actual.Task.Exists)
                {
                    reasons.Add(StartupReason.UnexpectedScheduledTask);
                    operations.Add(StartupOperation.RemoveScheduledTask);
                }

                var state = operations.Count == 0 ? StartupState.Disabled
                    : actual.Registry.Exists && actual.Task.Exists ? StartupState.Conflict
                    : StartupState.Broken;
                return new StartupConfigurationEvaluation(state, reasons, operations);
            }

            if (actual.Registry.Exists && actual.Task.Exists)
            {
                operations = desired.RunElevated
                    ? (!IsValidTask(actual.Task)
                        ? new List<StartupOperation> { StartupOperation.CreateOrUpdateScheduledTask, StartupOperation.RemoveRegistry }
                        : new List<StartupOperation> { StartupOperation.RemoveRegistry })
                    : (!IsValidRegistry(actual.Registry)
                        ? new List<StartupOperation> { StartupOperation.CreateOrUpdateRegistry, StartupOperation.RemoveScheduledTask }
                        : new List<StartupOperation> { StartupOperation.RemoveScheduledTask });
                return new StartupConfigurationEvaluation(StartupState.Conflict, reasons, operations);
            }

            if (desired.RunElevated)
            {
                if (actual.Registry.Exists) reasons.Add(StartupReason.UnexpectedRegistryEntry);
                if (!IsValidTask(actual.Task)) reasons.AddRange(TaskReasons(actual.Task));
                if (reasons.Count == 0)
                    return new StartupConfigurationEvaluation(StartupState.ElevatedTask, reasons, operations);
                operations.Add(StartupOperation.CreateOrUpdateScheduledTask);
                if (actual.Registry.Exists) operations.Add(StartupOperation.RemoveRegistry);
                return new StartupConfigurationEvaluation(StartupState.Broken, reasons, operations);
            }

            if (actual.Task.Exists) reasons.Add(StartupReason.UnexpectedScheduledTask);
            if (!IsValidRegistry(actual.Registry)) reasons.AddRange(RegistryReasons(actual.Registry));
            if (reasons.Count == 0)
                return new StartupConfigurationEvaluation(StartupState.Registry, reasons, operations);
            operations.Add(StartupOperation.CreateOrUpdateRegistry);
            if (actual.Task.Exists) operations.Add(StartupOperation.RemoveScheduledTask);
            return new StartupConfigurationEvaluation(StartupState.Broken, reasons, operations);
        }

        private static bool IsValidRegistry(RegistryStartupSnapshot item) =>
            item.Exists && item.ExecutableExists && item.IsExpectedExecutable && item.IsExpectedArguments;

        private static bool IsValidTask(ScheduledTaskStartupSnapshot item) =>
            item.Exists && item.Enabled && item.ExecutableExists && item.HasLogonTrigger
            && item.IsElevated && item.IsExpectedExecutable && item.IsExpectedArguments;

        private static IEnumerable<StartupReason> RegistryReasons(RegistryStartupSnapshot item)
        {
            if (!item.Exists)
            {
                yield return StartupReason.MissingRegistry;
                yield break;
            }

            if (!item.ExecutableExists) yield return StartupReason.ExecutableMissing;
            if (!item.IsExpectedExecutable) yield return StartupReason.WrongExecutable;
            if (!item.IsExpectedArguments) yield return StartupReason.WrongArguments;
        }

        private static IEnumerable<StartupReason> TaskReasons(ScheduledTaskStartupSnapshot item)
        {
            if (!item.Exists)
            {
                yield return StartupReason.MissingScheduledTask;
                yield break;
            }

            if (!item.ExecutableExists) yield return StartupReason.ExecutableMissing;
            if (!item.Enabled) yield return StartupReason.TaskDisabled;
            if (!item.HasLogonTrigger) yield return StartupReason.MissingLogonTrigger;
            if (!item.IsElevated) yield return StartupReason.MissingElevatedRunLevel;
            if (!item.IsExpectedExecutable) yield return StartupReason.WrongExecutable;
            if (!item.IsExpectedArguments) yield return StartupReason.WrongArguments;
        }
    }
}
