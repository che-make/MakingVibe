using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MakingVibe.Services
{
    /// <summary>
    /// Handles interactions with the file system.
    /// </summary>
    public class FileSystemService
    {
        // List of folders to ignore
        private readonly HashSet<string> _ignoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", ".idea",
            "bin", "obj",
            "node_modules", "__pycache__",
            "target", // Common for Rust/Java
            "build"   // Common for C++/CMake etc.
            // Add any other folders you commonly want to ignore for selection purposes
        };

        // Consider making ignored folders configurable if needed
        // public FileSystemService(IEnumerable<string> ignoredFolders)
        // {
        //     _ignoredFolderNames = new HashSet<string>(ignoredFolders, StringComparer.OrdinalIgnoreCase);
        // }

        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
        public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
        public string CombinePath(string path1, string path2) => Path.Combine(path1, path2);
        public string GetFileName(string path) => Path.GetFileName(path);

        /// <summary>
        /// Gets the direct children (files and directories) of a given directory path, filtering ignored items.
        /// </summary>
        /// <param name="directoryPath">The path of the directory to load.</param>
        /// <returns>An enumerable of FileSystemItem representing the children, or null if access is denied.</returns>
        public IEnumerable<FileSystemItem>? GetDirectoryChildren(string directoryPath)
        {
            try
            {
                var children = new List<FileSystemItem>();
                var dirInfoRoot = new DirectoryInfo(directoryPath); // Use DirectoryInfo for access checks

                // Get directories, filter ignored ones, and sort
                var directories = dirInfoRoot.GetDirectories() // Use DirectoryInfo method
                                          .Where(d => !_ignoredFolderNames.Contains(d.Name))
                                          .OrderBy(d => d.Name);

                foreach (var dirInfo in directories)
                {
                    children.Add(new FileSystemItem { Name = dirInfo.Name, Path = dirInfo.FullName, Type = "Directorio", IsDirectory = true });
                }

                // Get files, sort, and add
                var files = dirInfoRoot.GetFiles() // Use DirectoryInfo method
                                    .OrderBy(f => f.Name);

                foreach (var fileInfo in files)
                {
                    children.Add(new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = fileInfo.Extension, IsDirectory = false });
                }
                return children;
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied loading children for: {directoryPath}");
                return null; // Indicate access denied
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading children for {directoryPath}: {ex.Message}");
                // Depending on requirements, could return empty list or throw a custom exception
                return Enumerable.Empty<FileSystemItem>(); // Return empty on other errors
            }
        }

        /// <summary>
        /// Reads the content of a text file asynchronously.
        /// </summary>
        /// <param name="filePath">Path to the file.</param>
        /// <param name="maxSizeMB">Maximum file size in MB to attempt reading for preview.</param>
        /// <returns>A tuple containing the content (or error message) and a boolean indicating success.</returns>
        public async Task<(string Content, bool Success)> ReadTextFileContentAsync(string filePath, double maxSizeMB = 5.0)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > maxSizeMB * 1024 * 1024)
                {
                    return ($"--- El archivo es demasiado grande para la vista previa ({fileInfo.Length / 1024.0 / 1024.0:F2} MB) ---", false);
                }

                string content = await File.ReadAllTextAsync(filePath);
                return (content, true);
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"IO Error reading {filePath}: {ioEx}");
                return ($"Error de E/S al leer el archivo:\n{ioEx.Message}\n\nAsegúrese de que el archivo no esté en uso.", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading {filePath}: {ex}");
                return ($"Error inesperado al leer el archivo:\n{ex.Message}", false);
            }
        }

        /// <summary>
        /// Deletes a file or directory.
        /// </summary>
        /// <param name="item">The item to delete.</param>
        public void DeleteItem(FileSystemItem item)
        {
            if (item.IsDirectory)
            {
                if (Directory.Exists(item.Path))
                    Directory.Delete(item.Path, true); // Recursive delete
            }
            else
            {
                if (File.Exists(item.Path))
                    File.Delete(item.Path);
            }
        }

        /// <summary>
        /// Renames/Moves a file or directory.
        /// </summary>
        /// <param name="oldPath">The current path.</param>
        /// <param name="newPath">The desired new path.</param>
        /// <param name="isDirectory">True if the item is a directory.</param>
        public void RenameItem(string oldPath, string newPath, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);
            }
            else
            {
                if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
            }
        }

        /// <summary>
        /// Copies a file or directory.
        /// </summary>
        public void CopyItem(string sourcePath, string destPath, bool isDirectory)
        {
             if (isDirectory)
             {
                 CopyDirectoryRecursive(sourcePath, destPath);
             }
             else
             {
                 // Overwrite is handled by the caller logic (checking existence first)
                 File.Copy(sourcePath, destPath, true);
             }
        }

        /// <summary>
        /// Moves a file or directory. Essentially the same as RenameItem.
        /// </summary>
        public void MoveItem(string sourcePath, string destPath, bool isDirectory)
        {
            RenameItem(sourcePath, destPath, isDirectory);
        }


        /// <summary>
        /// Recursively copies a directory, skipping ignored folders.
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            // Copy files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true); // Overwrite true (existence check should happen before calling CopyItem)
            }

            // Recursively copy subdirectories, skipping ignored ones
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                if (!_ignoredFolderNames.Contains(dirName))
                {
                    string destSubDir = Path.Combine(destDir, dirName);
                    CopyDirectoryRecursive(subDir, destSubDir);
                }
            }
        }

        /// <summary>
        /// Recursively finds all text files starting from a given item, respecting ignored folders.
        /// Used by AiCopyService.
        /// </summary>
        public void FindTextFilesRecursive(FileSystemItem startItem, List<FileSystemItem> collectedFiles, string? rootPath)
        {
            ArgumentNullException.ThrowIfNull(rootPath); // Keep this check

            if (!startItem.IsDirectory)
            {
                // Check if it's a text file and not already added
                if (FileHelper.IsTextFile(startItem.Path) && !collectedFiles.Any(f => f.Path.Equals(startItem.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    collectedFiles.Add(startItem);
                }
                return; // Stop recursion if it's a file
            }

            // It's a directory, explore its contents
            try
            {
                var dirInfo = new DirectoryInfo(startItem.Path);

                // Process files in the current directory
                foreach (var fileInfo in dirInfo.GetFiles().Where(f => FileHelper.IsTextFile(f.FullName)))
                {
                    var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = fileInfo.Extension, IsDirectory = false };
                    // Check if not already added
                    if (!collectedFiles.Any(f => f.Path.Equals(fileFsi.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        collectedFiles.Add(fileFsi);
                    }
                }

                // Process subdirectories recursively, respecting ignored list
                foreach (var subDirInfo in dirInfo.GetDirectories())
                {
                    if (!_ignoredFolderNames.Contains(subDirInfo.Name))
                    {
                        var dirFsi = new FileSystemItem { Name = subDirInfo.Name, Path = subDirInfo.FullName, Type = "Directorio", IsDirectory = true };
                        FindTextFilesRecursive(dirFsi, collectedFiles, rootPath); // Pass rootPath down
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied during recursive text file search in: {startItem.Path}");
                // Skip this directory silently
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during recursive text file search in {startItem.Path}: {ex.Message}");
                // Log and continue if possible
            }
        }

        // --- NEW METHOD ---
        /// <summary>
        /// Recursively finds all files (not directories) within a given directory structure,
        /// respecting ignored folders.
        /// </summary>
        /// <param name="startDirectoryItem">The FileSystemItem representing the starting directory.</param>
        /// <param name="collectedFiles">A list to which found file FileSystemItems will be added.</param>
        public void FindAllFilesRecursive(FileSystemItem startDirectoryItem, List<FileSystemItem> collectedFiles)
        {
            // Ensure it's actually a directory we're starting with
            if (!startDirectoryItem.IsDirectory || !Directory.Exists(startDirectoryItem.Path))
            {
                return;
            }

            try
            {
                var dirInfo = new DirectoryInfo(startDirectoryItem.Path);

                // Add files in the current directory
                foreach (var fileInfo in dirInfo.GetFiles())
                {
                    var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = fileInfo.Extension, IsDirectory = false };
                    // Use Path equality for uniqueness check within this specific operation run
                    if (!collectedFiles.Any(f => f.Path.Equals(fileFsi.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        collectedFiles.Add(fileFsi);
                    }
                }

                // Process subdirectories recursively, respecting ignored list
                foreach (var subDirInfo in dirInfo.GetDirectories())
                {
                    if (!_ignoredFolderNames.Contains(subDirInfo.Name))
                    {
                        // Create a FileSystemItem for the subdirectory to pass down
                        var subDirFsi = new FileSystemItem { Name = subDirInfo.Name, Path = subDirInfo.FullName, Type = "Directorio", IsDirectory = true };
                        FindAllFilesRecursive(subDirFsi, collectedFiles); // Recursive call
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied during recursive file search in: {startDirectoryItem.Path}");
                // Skip this directory silently
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during recursive file search in {startDirectoryItem.Path}: {ex.Message}");
                // Log and potentially skip or throw depending on desired behavior
            }
        }
        // --- END NEW METHOD ---

    } // End class FileSystemService
} // End namespace MakingVibe.Services