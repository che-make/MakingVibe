using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MakingVibe.Models;

namespace MakingVibe.Services
{
    /// <summary>
    /// Manages application settings persistence.
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private readonly string _savedPathsFilePath;

        public SettingsService()
        {
            // Store settings file in a user-specific location or next to the executable
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.settings");
            _savedPathsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.paths.json");
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

        /// <summary>
        /// Carga las rutas guardadas desde el archivo de configuración.
        /// </summary>
        public List<SavedPath> LoadSavedPaths()
        {
            try
            {
                if (File.Exists(_savedPathsFilePath))
                {
                    string json = File.ReadAllText(_savedPathsFilePath);
                    var savedPaths = JsonSerializer.Deserialize<List<SavedPath>>(json);
                    return savedPaths ?? new List<SavedPath>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading saved paths from {_savedPathsFilePath}: {ex.Message}");
            }
            return new List<SavedPath>();
        }

        /// <summary>
        /// Guarda la lista completa de rutas en el archivo de configuración.
        /// </summary>
        private void SavePathsList(List<SavedPath> paths)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(paths, options);
                File.WriteAllText(_savedPathsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving paths to {_savedPathsFilePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Añade o actualiza una ruta en la lista de rutas guardadas.
        /// </summary>
        public void AddSavedPath(string path, string? displayName = null)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var paths = LoadSavedPaths();
                
                // Check if path already exists
                var existingPath = paths.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (existingPath != null)
                {
                    // Update existing entry
                    existingPath.SavedDate = DateTime.Now;
                    if (!string.IsNullOrEmpty(displayName))
                        existingPath.DisplayName = displayName;
                }
                else
                {
                    // Add new entry
                    paths.Add(new SavedPath 
                    { 
                        Path = path,
                        DisplayName = displayName ?? Path.GetFileName(path) ?? path,
                        SavedDate = DateTime.Now
                    });
                }

                // Sort by most recently used
                paths = paths.OrderByDescending(p => p.SavedDate).ToList();
                
                // Save the updated list
                SavePathsList(paths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding saved path: {ex.Message}");
            }
        }

        /// <summary>
        /// Elimina una ruta de la lista de rutas guardadas.
        /// </summary>
        public void RemoveSavedPath(string path)
        {
            try
            {
                var paths = LoadSavedPaths();
                paths.RemoveAll(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                SavePathsList(paths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing saved path: {ex.Message}");
            }
        }
    }
}