using System;
using System.Windows.Input;

namespace ProcessCoreOptimizer.WPF.Helpers
{
    /// <summary>
    /// A basic implementation of the ICommand interface, 
    /// used to bind UI actions to ViewModel methods.
    /// </summary>
    public class RelayCommand : ICommand
    {
        #region Fields
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of RelayCommand that can always execute.
        /// </summary>
        /// <param name="execute">The logic to be executed.</param>
        public RelayCommand(Action<object?> execute)
            : this(execute, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of RelayCommand with custom execution logic.
        /// </summary>
        /// <param name="execute">The logic to be executed.</param>
        /// <param name="canExecute">The logic that determines if the command can execute.</param>
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        #endregion

        #region ICommand Members
        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// </summary>
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
        #endregion
    }
}