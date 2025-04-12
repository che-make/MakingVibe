using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MakingVibe.Services
{
    /// <summary>
    /// Provides helper methods for file type detection.
    /// </summary>
    public static class FileHelper
    {
        // List of file extensions considered as text for the AI copy feature and preview
        private static readonly HashSet<string> TextFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Basic text files
            ".txt", ".log", ".md", ".csv", ".tsv", ".rtf",
            // Web development
            ".html", ".htm", ".css", ".js", ".jsx", ".ts", ".tsx", ".json", ".xml", ".yaml", ".yml", ".svg", ".vue", ".svelte",
            // Configuration files
            ".config", ".ini", ".toml", ".conf", ".properties", ".env", ".editorconfig", ".csproj", ".sln", ".xaml", ".gradle", ".settings", ".props",
            // Scripts and shell
            ".bat", ".cmd", ".ps1", ".sh", ".bash", ".zsh", ".fish", ".py", ".rb", ".php", ".pl", ".lua", ".tcl",
            // Programming languages
            ".cs", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".swift", ".kt", ".scala", ".dart", ".groovy", ".m", ".r", ".sql", ".vb", ".fs", ".pas",
            // Other common text formats
            ".gitignore", ".dockerignore", ".gitattributes", ".sql", ".readme", ".inf", ".tex"
            // Add more as needed
        };

        // List of common image file extensions
        private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".webp", ".svg" // Added webp, svg
        };

        /// <summary>
        /// Checks if a file path likely points to a text file based on its extension or common name.
        /// </summary>
        public static bool IsTextFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string extension = Path.GetExtension(filePath); // Includes the dot "."

            // Handle files with no extension but common names like 'Dockerfile', 'LICENSE'
            if (string.IsNullOrEmpty(extension))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("README", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("rc", StringComparison.OrdinalIgnoreCase) || // like .bashrc
                    fileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false; // No extension and not a known common name
            }

            return TextFileExtensions.Contains(extension);
        }

        /// <summary>
        /// Checks if a file path likely points to an image file based on its extension.
        /// </summary>
        public static bool IsImageFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension)) return false;

            return ImageFileExtensions.Contains(extension);
        }
    }
}