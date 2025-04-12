using System;
using System.Diagnostics;
using System.IO;

namespace MakingVibe.Services
{
    /// <summary>
    /// Manages application settings persistence.
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService()
        {
            // Store settings file in a user-specific location or next to the executable
             // Using BaseDirectory for simplicity here
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.settings");
        }

        /// <summary>
        /// Loads the last used root path from the settings file.
        /// </summary>
        /// <returns>The last root path, or null if not found or invalid.</returns>
        public string? LoadLastRootPath()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string? path = File.ReadAllText(_settingsFilePath)?.Trim();
                    // Optional: Add validation if the directory still exists
                    // if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    // {
                    //     return path;
                    // }
                    return string.IsNullOrEmpty(path) ? null : path;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings from {_settingsFilePath}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Saves the specified root path to the settings file.
        /// </summary>
        /// <param name="rootPath">The root path to save, or null to clear the setting.</param>
        public void SaveLastRootPath(string? rootPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(rootPath))
                {
                    File.WriteAllText(_settingsFilePath, rootPath);
                }
                else if (File.Exists(_settingsFilePath))
                {
                    File.Delete(_settingsFilePath); // Clear setting if no path
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings to {_settingsFilePath}: {ex.Message}");
            }
        }
    }
}