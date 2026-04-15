using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for the persistence of process optimization profiles 
    /// using JSON serialization for local storage.
    /// </summary>
    public class ProfileService
    {
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
            if (!File.Exists(_filePath))
            {
                return new List<ProcessProfile>();
            }

            try
            {
                string jsonContent = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent) ?? new List<ProcessProfile>();
            }
            catch
            {
                // Return an empty list on deserialization failure to prevent application crashes
                return new List<ProcessProfile>();
            }
        }

        /// <summary>
        /// Serializes and saves the current list of profiles to the local JSON file.
        /// </summary>
        /// <param name="profiles">The list of profiles to persist.</param>
        public void SaveProfiles(List<ProcessProfile> profiles)
        {
            try
            {
                string jsonContent = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, jsonContent);
            }
            catch
            {
                // Silent catch for I/O errors; consider adding logging if persistence is critical
            }
        }
        #endregion
    }
}