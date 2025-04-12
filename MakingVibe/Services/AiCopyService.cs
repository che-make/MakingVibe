using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MakingVibe.Services
{
    /// <summary>
    /// Handles the logic for generating the AI-formatted clipboard content.
    /// </summary>
    public class AiCopyService
    {
        private readonly FileSystemService _fileSystemService;

        public AiCopyService(FileSystemService fileSystemService)
        {
            _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        }

        /// <summary>
        /// Generates the file map and file contents string for the AI prompt.
        /// </summary>
        /// <param name="selectedItems">The initial list of selected files and directories.</param>
        /// <param name="rootPath">The absolute root path of the project.</param>
        /// <returns>A tuple containing the combined output string and the count of text files included, or null if no text files are found.</returns>
        public async Task<(string Output, int TextFileCount)?> GenerateAiClipboardContentAsync(IEnumerable<FileSystemItem> selectedItems, string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !selectedItems.Any())
            {
                return null;
            }

            // --- 1. Collect all unique text files ---
            var allTextFiles = new List<FileSystemItem>();
            foreach (var selectedItem in selectedItems)
            {
                // Use FileSystemService to find files recursively
                _fileSystemService.FindTextFilesRecursive(selectedItem, allTextFiles, rootPath);
            }

            // Ensure uniqueness and sort for consistent output
            var uniqueTextFiles = allTextFiles
                .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f.Path)
                .ToList();

            if (uniqueTextFiles.Count == 0)
            {
                return null; // No text files found
            }

            // --- 2. Build the minimal file map ---
            var fileMapBuilder = BuildMinimalFileMapForFiles(uniqueTextFiles, rootPath);

            // --- 3. Build the file contents section ---
            var fileContentBuilder = new StringBuilder();
            foreach (var textFile in uniqueTextFiles)
            {
                var (content, success) = await _fileSystemService.ReadTextFileContentAsync(textFile.Path);
                string relativePath = Path.GetRelativePath(rootPath, textFile.Path).Replace('\\', '/'); // Use forward slashes

                fileContentBuilder.AppendLine($"File: {relativePath}");
                fileContentBuilder.AppendLine(); // Empty line after path
                if (success)
                {
                    fileContentBuilder.AppendLine(content);
                }
                else
                {
                    // Include the error message from ReadTextFileContentAsync
                    fileContentBuilder.AppendLine($"### Error reading file: {content} ###");
                }
                fileContentBuilder.AppendLine(); // Empty line after content/error
                fileContentBuilder.AppendLine(); // Extra newline for separation
            }

            // --- 4. Combine map and content ---
            var resultBuilder = new StringBuilder();
            resultBuilder.AppendLine("<file_map>");
            resultBuilder.Append(fileMapBuilder); // Append generated map
            resultBuilder.AppendLine("</file_map>");
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("<file_contents>");
            resultBuilder.Append(fileContentBuilder); // Append generated content
            resultBuilder.AppendLine("</file_contents>");

            return (resultBuilder.ToString(), uniqueTextFiles.Count);
        }


        // Builds the <file_map> structure based ONLY on the paths of the provided text files.
        private StringBuilder BuildMinimalFileMapForFiles(List<FileSystemItem> textFiles, string rootPath)
        {
            var mapBuilder = new StringBuilder();
            if (textFiles.Count == 0) return mapBuilder;

            // Create a set of all directory paths required to reach the text files
            var requiredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in textFiles)
            {
                string? dir = Path.GetDirectoryName(file.Path);
                while (dir != null && dir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && dir.Length >= rootPath.Length)
                {
                    requiredDirs.Add(dir);
                    if (dir.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) break; // Stop at root
                    dir = Path.GetDirectoryName(dir);
                }
            }
            requiredDirs.Add(rootPath); // Ensure root is always included if files are present

            // Group files by their directory
            var dirToFileMap = textFiles
                .GroupBy(f => Path.GetDirectoryName(f.Path) ?? rootPath) // Group by directory name, fallback to root
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name).ToList(), StringComparer.OrdinalIgnoreCase);


            // Build the tree structure recursively
            BuildMapRecursive(rootPath, 0, mapBuilder, requiredDirs, dirToFileMap);

            return mapBuilder;
        }

        // Recursive helper for building the file map string
        private void BuildMapRecursive(string currentDirPath, int level, StringBuilder builder, HashSet<string> requiredDirs, Dictionary<string, List<FileSystemItem>> dirToFileMap)
        {
            // Get display name
            string displayName = level == 0 ? currentDirPath : Path.GetFileName(currentDirPath);
            string indent = new string(' ', level * 2);
            string prefix = level == 0 ? "" : "└── "; // Simple prefix for now

            builder.AppendLine($"{indent}{prefix}{displayName}");
            string childIndent = new string(' ', (level + 1) * 2); // Indent for children

            try
            {
                // Add relevant subdirectories first, sorted alphabetically
                 var subDirs = _fileSystemService.DirectoryExists(currentDirPath)
                    ? Directory.GetDirectories(currentDirPath)
                       .Where(d => requiredDirs.Contains(d)) // Only include dirs that are required
                       .OrderBy(d => d)
                       .ToList()
                     : new List<string>(); // Handle case where dir might not exist (though unlikely here)


                for(int i=0; i< subDirs.Count; i++)
                {
                    string subDir = subDirs[i];
                    BuildMapRecursive(subDir, level + 1, builder, requiredDirs, dirToFileMap);
                }

                // Add files in the current directory that were selected, sorted alphabetically
                if (dirToFileMap.TryGetValue(currentDirPath, out var filesInDir))
                {
                    string filePrefix = "    ├── "; // Simple file prefix

                    for(int i=0; i< filesInDir.Count; i++)
                    {
                       var file = filesInDir[i];
                        builder.AppendLine($"{childIndent}{filePrefix}{file.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine($"{childIndent}  [Error accessing: {ex.Message}]");
                Debug.WriteLine($"Error accessing directory {currentDirPath} during map build: {ex.Message}");
            }
        }
    }
}