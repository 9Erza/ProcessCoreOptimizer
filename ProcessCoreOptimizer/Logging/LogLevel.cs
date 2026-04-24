namespace ProcessCoreOptimizer.WPF.Logging
{
    /// <summary>
    /// Klasa reprezentująca poziomy logowania z dwujęzycznymi nazwami (PL/EN).
    /// </summary>
    public class LogLevel : ILogLevel
    {
        // Liczbowe wartości poziomów (niższa wartość = wyższy priorytet)
        private const int DebugLevel = 0;
        private const int InfoLevel = 1;
        private const int WarnLevel = 2;
        private const int ErrorLevel = 3;
        private const int FatalLevel = 4;

        // Nazwy poziomów w języku polskim
        private static readonly string[] PolishNames = { "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };
        // Nazwy poziomów w języku angielskim
        private static readonly string[] EnglishNames = { "Debug", "Info", "Warn", "Error", "Fatal" };

        /// <summary>
        /// Poziom Debug - najbardziej szczegółowe logi dla celów debugowania.
        /// </summary>
        public static readonly ILogLevel Debug = new LogLevel(DebugLevel, PolishNames[0], EnglishNames[0]);

        /// <summary>
        /// Poziom Info - informacyjne komunikaty o działaniu aplikacji.
        /// </summary>
        public static readonly ILogLevel Info = new LogLevel(InfoLevel, PolishNames[1], EnglishNames[1]);

        /// <summary>
        /// Poziom Warn - ostrzeżenia, aplikacja działa ale może wystąpić problem.
        /// </summary>
        public static readonly ILogLevel Warn = new LogLevel(WarnLevel, PolishNames[2], EnglishNames[2]);

        /// <summary>
        /// Poziom Error - błędy, funkcjonalność uszkodzona.
        /// </summary>
        public static readonly ILogLevel Error = new LogLevel(ErrorLevel, PolishNames[3], EnglishNames[3]);

        /// <summary>
        /// Poziom Fatal - krytyczne błędy, aplikacja nie może działać dalej.
        /// </summary>
        public static readonly ILogLevel Fatal = new LogLevel(FatalLevel, PolishNames[4], EnglishNames[4]);

        private readonly int _level;
        private readonly string _name;

        private LogLevel(int level, string name)
        {
            _level = level;
            _name = name;
        }

        private LogLevel(int level, string polishName, string englishName)
        {
            _level = level;
            _name = System.Threading.Thread.CurrentThread.CurrentCulture.Name.StartsWith("pl") ? polishName : englishName;
        }

        /// <summary>
        /// Nazwa poziomu logowania.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Liczbowy poziom logowania.
        /// </summary>
        public int Level => _level;

        /// <summary>
        /// Sprawdza, czy ten poziom logowania jest ważniejszy lub równy innemu poziomowi.
        /// </summary>
        public bool IsEnabled(ILogLevel other)
        {
            return _level <= other.Level;
        }

        /// <summary>
        /// Pobiera nazwę poziomu w języku angielskim.
        /// </summary>
        public static string GetEnglishName(int level)
        {
            return EnglishNames[level];
        }

        /// <summary>
        /// Pobiera nazwę poziomu w języku polskim.
        /// </summary>
        public static string GetPolishName(int level)
        {
            return PolishNames[level];
        }
    }
}
