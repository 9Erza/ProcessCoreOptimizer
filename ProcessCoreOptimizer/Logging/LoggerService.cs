using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ProcessCoreOptimizer.WPF.Logging
{
    /// <summary>
    /// Konfiguracja loggera.
    /// </summary>
    public class LoggerConfiguration
    {
        /// <summary>
        /// Domyślny poziom logowania.
        /// </summary>
        public ILogLevel LogLevel { get; set; } = null!;

        /// <summary>
        /// Ścieżka do pliku logów.
        /// </summary>
        public string LogFilePath { get; set; } = "ProcessCoreOptimizer.log";

        /// <summary>
        /// Czy logować do konsoli.
        /// </summary>
        public bool EnableConsoleOutput { get; set; } = true;

        /// <summary>
        /// Format daty w logach (np. "yyyy-MM-dd HH:mm:ss").
        /// </summary>
        public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Nazwa źródła logów (prefix dla każdego logu).
        /// </summary>
        public string SourceName { get; set; } = "ProcessCoreOptimizer";
    }

    /// <summary>
    /// Writer odpowiedzialny za zapisywanie logów do konsoli.
    /// </summary>
    public class ConsoleWriter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly LoggerConfiguration _config;
        private bool _disposed;

        public ConsoleWriter(ILogger logger, LoggerConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public void Write(ILogLevel level, string message, Exception? ex = null)
        {
            if (_disposed) return;

            var timestamp = DateTime.Now.ToString(_config.DateFormat);
            var prefix = $"{_config.SourceName} [{level.Name}]";
            var formattedMessage = $"{timestamp} - {prefix} - {message}";

            if (ex != null)
            {
                Console.WriteLine($"{formattedMessage}\n{ex}");
            }
            else
            {
                Console.WriteLine(formattedMessage);
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Writer odpowiedzialny za zapisywanie logów do pliku.
    /// </summary>
    public class FileWriter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly LoggerConfiguration _config;
        private StreamWriter? _streamWriter;
        private bool _disposed;

        public FileWriter(ILogger logger, LoggerConfiguration config)
        {
            _logger = logger;
            _config = config;

            EnsureDirectoryExists();
            OpenFile();
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_config.LogFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private void OpenFile()
        {
            _streamWriter ??= new StreamWriter(_config.LogFilePath, true, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public void Write(ILogLevel level, string message, Exception? ex = null)
        {
            if (_disposed || _streamWriter == null) return;

            var timestamp = DateTime.Now.ToString(_config.DateFormat);
            var prefix = $"{_config.SourceName} [{level.Name}]";
            var formattedMessage = $"{timestamp} - {prefix} - {message}";

            try
            {
                if (ex != null)
                {
                    _streamWriter.WriteLine(formattedMessage);
                    _streamWriter.WriteLine(ex.ToString());
                }
                else
                {
                    _streamWriter.WriteLine(formattedMessage);
                }
            }
            catch (IOException ioEx)
            {
                // Loguj błąd zapisu do konsoli, ale nie przerywaj aplikacji
                Console.WriteLine($"[FATAL] Błąd zapisu logów do pliku: {ioEx.Message}");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _streamWriter?.Dispose();
        }
    }

    /// <summary>
    /// Główna klasa loggera - singleton odpowiedzialny za logowanie w całej aplikacji.
    /// </summary>
    public class LoggerService : ILogger
    {
        private static readonly Lazy<LoggerService> _instance =
            new(() => new LoggerService());

        private readonly ConsoleWriter _consoleWriter;
        private readonly FileWriter _fileWriter;
        private ILogLevel _currentLogLevel;
        private readonly LoggerConfiguration _config;
        private bool _disposed;

        /// <summary>
        /// Pobiera jedyny instancję loggera (singleton pattern).
        /// </summary>
        public static ILogger Instance => _instance.Value;

        /// <summary>
        /// Konfiguracja loggera.
        /// </summary>
        public LoggerConfiguration Configuration { get; set; }

        /// <summary>
        /// Aktualny poziom logowania.
        /// </summary>
        public ILogLevel LogLevel
        {
            get => _currentLogLevel;
            set => _currentLogLevel = value;
        }

        private LoggerService()
        {
            _config = new LoggerConfiguration();
            _currentLogLevel = ProcessCoreOptimizer.WPF.Logging.LogLevel.Info;
            Configuration = _config;
            _consoleWriter = new ConsoleWriter(this, _config);
            _fileWriter = new FileWriter(this, _config);
        }

        /// <summary>
        /// Tworzy instancję LoggerService z niestandardową konfiguracją.
        /// </summary>
        public static ILogger Create(LoggerConfiguration config)
        {
            var logger = new LoggerService();
            logger.Configuration = config;
            return logger;
        }

        #region Log Methods

        public void Debug(string message) => Log(_currentLogLevel, $"[{_config.SourceName}] {message}");
        public void Info(string message) => Log(_currentLogLevel, $"[{_config.SourceName}] {message}");
        public void Warn(string message) => Log(_currentLogLevel, $"[{_config.SourceName}] {message}");
        public void Error(string message, Exception? ex = null) => Log(_currentLogLevel, $"[{_config.SourceName}] {message}", ex);
        public void Fatal(string message, Exception? ex = null) => Log(_currentLogLevel, $"[{_config.SourceName}] {message}", ex);

        public void Log(ILogLevel level, string message, Exception? ex = null)
        {
            // Sprawdź czy poziom jest aktywny
            if (_currentLogLevel.IsEnabled(level))
            {
                _consoleWriter.Write(level, message, ex);
                _fileWriter.Write(level, message, ex);
            }
        }

        public void Log<TLevel>(string message, Exception? ex = null) where TLevel : ILogLevel
        {
            // Sprawdź czy poziom jest aktywny
            if (_currentLogLevel.IsEnabled((TLevel)(object)Activator.CreateInstance(typeof(TLevel))!))
            {
                _consoleWriter.Write((TLevel)(object)Activator.CreateInstance(typeof(TLevel))!, message, ex);
                _fileWriter.Write((TLevel)(object)Activator.CreateInstance(typeof(TLevel))!, message, ex);
            }
        }

        public void LogException(Exception ex)
        {
            var message = $"Uncaught exception: {ex.Message}";
            Error(message, ex);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            
            _consoleWriter.Dispose();
            _fileWriter.Dispose();
            _disposed = true;
        }

        #endregion
    }
}
