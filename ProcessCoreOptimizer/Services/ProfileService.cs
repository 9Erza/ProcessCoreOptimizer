using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System.Text.Json.Serialization;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for the persistence of process optimization profiles
    /// using JSON serialization for local storage.
    /// </summary>
    public class ProfileService
    {
        private static readonly ILogger _logger = LoggerService.Instance;

        #region Fields
        /// <summary>
        /// The full system path to the profiles configuration file.
        /// </summary>
        private readonly string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");
        #endregion

        #region Public Methods
        /// <summary>
        /// Reads the profiles from the local JSON file. 
        /// Returns an empty list if the file does not exist or is corrupted.
        /// </summary>
        /// <returns>A collection of saved ProcessProfile objects.</returns>
        public List<ProcessProfile> LoadProfiles()
        {
            _logger.Info("Loading profiles from JSON file");

            if (!File.Exists(_filePath))
            {
                _logger.Debug("Profiles file not found - returning empty list");
                return new List<ProcessProfile>();
            }

            try
            {
                string jsonContent = File.ReadAllText(_filePath);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new JsonStringEnumConverter()); // POPRAWKA

                var profiles = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, options);
                _logger.Debug($"Profiles loaded successfully: {profiles?.Count ?? 0} profiles");
                return profiles ?? new List<ProcessProfile>();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to deserialize profiles - returning empty list", ex);
                return new List<ProcessProfile>();
            }
        }

        /// <summary>
        /// Serializes and saves the current list of profiles to the local JSON file.
        /// </summary>
        /// <param name="profiles">The list of profiles to persist.</param>
        public void SaveProfiles(List<ProcessProfile> profiles)
        {
            _logger.Info("Saving profiles to JSON file");

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter()); // POPRAWKA

                string jsonContent = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_filePath, jsonContent);
                _logger.Debug($"Profiles saved successfully: {profiles?.Count ?? 0} profiles");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save profiles", ex);
            }
        }
        #endregion
    }
}