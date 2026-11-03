using System;
using System.Globalization;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Pure validation for the Profile Studio editor. No UI, no IO — testable.
    /// Error messages come in both UI languages (caller picks by language).
    /// </summary>
    public static class ProfileStudioValidator
    {
        public sealed class ValidationResult
        {
            public bool Ok { get; set; }
            public string ErrorEn { get; set; } = string.Empty;
            public string ErrorPl { get; set; } = string.Empty;
            public long Mask { get; set; }
        }

        public static ValidationResult Validate(
            string? processName,
            string? maskText,
            bool applyCoreOptimization,
            bool applyPriority)
        {
            if (string.IsNullOrWhiteSpace(ProfileService.NormalizeProcessName(processName ?? string.Empty)))
            {
                return new ValidationResult
                {
                    Ok = false,
                    ErrorEn = "Process name is required.",
                    ErrorPl = "Nazwa procesu jest wymagana."
                };
            }

            if (!applyCoreOptimization && !applyPriority)
            {
                return new ValidationResult
                {
                    Ok = false,
                    ErrorEn = "Enable at least one action: CPU cores or priority.",
                    ErrorPl = "Włącz co najmniej jedną akcję: rdzenie CPU lub priorytet."
                };
            }

            long mask = 0;
            if (applyCoreOptimization)
            {
                if (!TryParseMask(maskText, out mask) || mask == 0)
                {
                    return new ValidationResult
                    {
                        Ok = false,
                        ErrorEn = "Affinity mask must be a non-zero hex value (e.g. FF).",
                        ErrorPl = "Maska affinity musi być niezerową wartością hex (np. FF)."
                    };
                }
            }

            return new ValidationResult { Ok = true, Mask = mask };
        }

        public static bool TryParseMask(string? text, out long mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(2);
            return long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask);
        }

        public static string FormatMaskHex(long mask)
        {
            return $"0x{mask:X}";
        }
    }
}
