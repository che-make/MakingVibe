/*
 * MakingVibe/Services/FileSystemService.cs
 * Modified GetDirectoryChildren to accept and use filter.
 */
using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // Required for Encoding
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

        // List of file names to ignore (case-insensitive)
        private readonly HashSet<string> _ignoredFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
             ".DS_Store" // Common macOS file
             // Add other specific files if needed
        };


        // Maximum file size in bytes to attempt line counting to avoid performance issues
        private const long MaxFileSizeForLineCount = 10 * 1024 * 1024; // 10 MB

        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
        public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
        public string CombinePath(string path1, string path2) => Path.Combine(path1, path2);
        public string GetFileName(string path) => Path.GetFileName(path);

        /// <summary>
        /// Gets the direct children (files and directories) of a given directory path,
        /// filtering ignored items and applying file extension filters if provided.
        /// Calculates line counts for text files within the size limit.
        /// </summary>
        /// <param name="directoryPath">The path of the directory to load.</param>
        /// <param name="allowedExtensions">Optional set of allowed file extensions (including the dot, e.g., ".cs"). If null or empty, all non-ignored files are included.</param>
        /// <returns>An enumerable of FileSystemItem representing the children, or null if access is denied.</returns>
        public IEnumerable<FileSystemItem>? GetDirectoryChildren(string directoryPath, HashSet<string>? allowedExtensions = null)
        {
            try
            {
                var children = new List<FileSystemItem>();
                var dirInfoRoot = new DirectoryInfo(directoryPath); // Use DirectoryInfo for access checks

                // --- Get Directories ---
                var directories = dirInfoRoot.GetDirectories() // Use DirectoryInfo method
                                          .Where(d => !_ignoredFolderNames.Contains(d.Name))
                                          .OrderBy(d => d.Name);

                foreach (var dirInfo in directories)
                {
                    children.Add(new FileSystemItem { Name = dirInfo.Name, Path = dirInfo.FullName, Type = "Directorio", IsDirectory = true, LineCount = null }); // Directories have null LineCount
                }

                // --- Get Files ---
                var files = dirInfoRoot.GetFiles() // Use DirectoryInfo method
                                    .Where(f => !_ignoredFileNames.Contains(f.Name)) // Filter ignored file names
                                    .OrderBy(f => f.Name);

                // Determine if filtering by extension is active
                bool filterByExtension = allowedExtensions != null && allowedExtensions.Any();

                foreach (var fileInfo in files)
                {
                    string fileExtension = fileInfo.Extension; // Includes the dot, or empty string if no extension
                    string typeDisplay = string.IsNullOrEmpty(fileExtension) ? "[Sin extensión]" : fileExtension;

                    // Apply extension filter if active
                    if (filterByExtension && !allowedExtensions!.Contains(typeDisplay)) // Use the display type for matching "[Sin extensión]"
                    {
                        continue; // Skip file if its extension is not in the allowed list
                    }

                    // Calculate line count only for text files within the size limit
                    int? lineCount = null;
                    if (FileHelper.IsTextFile(fileInfo.FullName) && fileInfo.Length <= MaxFileSizeForLineCount)
                    {
                        lineCount = GetLineCount(fileInfo.FullName);
                    }
                     // else: lineCount remains null for non-text files or large files

                    children.Add(new FileSystemItem {
                        Name = fileInfo.Name,
                        Path = fileInfo.FullName,
                        Type = typeDisplay, // Use the potentially modified type display
                        IsDirectory = false,
                        LineCount = lineCount // Set the calculated or null line count
                    });
                }
                return children;
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied loading children for: {directoryPath}");
                return null; // Indicate access denied
            }
            catch (DirectoryNotFoundException)
            {
                 Debug.WriteLine($"Directory not found: {directoryPath}");
                 return Enumerable.Empty<FileSystemItem>(); // Directory disappeared? Return empty.
            }
            catch (IOException ioEx) // Catch other potential IO errors
            {
                 Debug.WriteLine($"IO Error loading children for {directoryPath}: {ioEx.Message}");
                 return Enumerable.Empty<FileSystemItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error loading children for {directoryPath}: {ex.Message}");
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
                if (!fileInfo.Exists) return ("--- Archivo no encontrado ---", false); // Check existence
                if (fileInfo.Length > maxSizeMB * 1024 * 1024)
                {
                    return ($"--- El archivo es demasiado grande para la vista previa ({fileInfo.Length / 1024.0 / 1024.0:F2} MB) ---", false);
                }

                // Use StreamReader to detect encoding
                using var reader = new StreamReader(filePath, Encoding.UTF8, true); // Detect encoding from BOM, default UTF8
                string content = await reader.ReadToEndAsync();
                return (content, true);
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"IO Error reading {filePath}: {ioEx}");
                return ($"Error de E/S al leer el archivo:\n{ioEx.Message}\n\nAsegúrese de que el archivo no esté en uso.", false);
            }
             catch (UnauthorizedAccessException uaEx)
             {
                 Debug.WriteLine($"Access Denied reading {filePath}: {uaEx}");
                 return ($"Acceso denegado al leer el archivo:\n{uaEx.Message}", false);
             }
             catch (System.Security.SecurityException secEx) // Catch security exceptions
             {
                 Debug.WriteLine($"Security Error reading {filePath}: {secEx}");
                 return ($"Error de seguridad al leer el archivo:\n{secEx.Message}", false);
             }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading {filePath}: {ex}");
                return ($"Error inesperado al leer el archivo:\n{ex.Message}", false);
            }
        }

         /// <summary>
        /// Counts the number of lines in a file efficiently.
        /// </summary>
        /// <param name="filePath">The path to the file.</param>
        /// <returns>The number of lines, or null if an error occurs or file is empty.</returns>
        private int? GetLineCount(string filePath)
        {
            int count = 0;
            try
            {
                 // Check file existence before attempting to read
                 if (!File.Exists(filePath))
                 {
                     Debug.WriteLine($"File not found for line count: {filePath}");
                     return null;
                 }

                 // Use StreamReader for efficient line reading and encoding detection
                 using var reader = new StreamReader(filePath, Encoding.UTF8, true); // Detect BOM, default UTF8
                 while (reader.ReadLine() != null)
                 {
                     count++;
                 }
                 // Return count > 0 ? count : null; // Return null if file is empty (0 lines)? Or return 0? Let's return 0 for empty files.
                 return count;
            }
            catch (IOException ioEx)
            {
                 Debug.WriteLine($"IO Error counting lines in {filePath}: {ioEx.Message}");
                 return null; // Indicate error
            }
            catch (UnauthorizedAccessException uaEx)
            {
                 Debug.WriteLine($"Access Denied counting lines in {filePath}: {uaEx.Message}");
                 return null; // Indicate error
            }
            catch (System.Security.SecurityException secEx) // Catch security exceptions
             {
                 Debug.WriteLine($"Security Error counting lines in {filePath}: {secEx}");
                 return null;
             }
            catch (Exception ex) // Catch other potential errors
            {
                Debug.WriteLine($"Error counting lines in {filePath}: {ex.Message}");
                return null; // Indicate error
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
                // Check existence before attempting delete
                if (Directory.Exists(item.Path))
                    Directory.Delete(item.Path, true); // Recursive delete
            }
            else
            {
                 // Check existence before attempting delete
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
                // Check existence before attempting move
                if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);
                else
                    throw new DirectoryNotFoundException($"Source directory not found: {oldPath}");
            }
            else
            {
                 // Check existence before attempting move
                if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
                else
                    throw new FileNotFoundException($"Source file not found: {oldPath}");
            }
        }

        /// <summary>
        /// Copies a file or directory. Overwrites if destination exists.
        /// </summary>
        public void CopyItem(string sourcePath, string destPath, bool isDirectory)
        {
             if (isDirectory)
             {
                  // Check existence before attempting copy
                 if (!Directory.Exists(sourcePath))
                     throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
                 CopyDirectoryRecursive(sourcePath, destPath);
             }
             else
             {
                  // Check existence before attempting copy
                 if (!File.Exists(sourcePath))
                     throw new FileNotFoundException($"Source file not found: {sourcePath}");
                 // Overwrite is handled by the caller logic (checking existence first, then deleting before calling CopyItem)
                 // File.Copy will throw if destPath exists, so the caller MUST delete it first if overwrite is intended.
                 // Let's stick to the File.Copy overwrite parameter for simplicity here, assuming the caller handles conflict resolution UI.
                 File.Copy(sourcePath, destPath, true); // Use overwrite=true
             }
        }

        /// <summary>
        /// Moves a file or directory. Essentially the same as RenameItem.
        /// </summary>
        public void MoveItem(string sourcePath, string destPath, bool isDirectory)
        {
            // RenameItem already includes existence checks
            RenameItem(sourcePath, destPath, isDirectory);
        }


        /// <summary>
        /// Recursively copies a directory, skipping ignored folders and files.
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            // Get source directory info
            var dir = new DirectoryInfo(sourceDir);
             if (!dir.Exists)
             {
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDir}");
             }

             // Create destination directory if it doesn't exist
            Directory.CreateDirectory(destDir);

            // Copy files, skipping ignored ones
            foreach (FileInfo file in dir.GetFiles())
            {
                 if (!_ignoredFileNames.Contains(file.Name))
                 {
                     string targetFilePath = Path.Combine(destDir, file.Name);
                     file.CopyTo(targetFilePath, true); // Overwrite existing files
                 }
            }

            // Recursively copy subdirectories, skipping ignored ones
            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                if (!_ignoredFolderNames.Contains(subDir.Name))
                {
                    string newDestinationDir = Path.Combine(destDir, subDir.Name);
                    CopyDirectoryRecursive(subDir.FullName, newDestinationDir);
                }
            }
        }


        /// <summary>
        /// Recursively finds all text files starting from a given item, respecting ignored folders and files.
        /// Used by AiCopyService.
        /// </summary>
        public void FindTextFilesRecursive(FileSystemItem startItem, List<FileSystemItem> collectedFiles, string? rootPath)
        {
            ArgumentNullException.ThrowIfNull(rootPath); // Keep this check

            if (!startItem.IsDirectory)
            {
                // Check if it's a text file, not ignored, and not already added
                if (FileHelper.IsTextFile(startItem.Path) &&
                    !_ignoredFileNames.Contains(startItem.Name) &&
                    !collectedFiles.Any(f => f.Path.Equals(startItem.Path, StringComparison.OrdinalIgnoreCase)))
                {
                     // Note: We don't recalculate line count here as it was done when the item was initially loaded.
                     // We trust the LineCount property of the passed 'startItem'.
                    collectedFiles.Add(startItem);
                }
                return; // Stop recursion if it's a file
            }

            // It's a directory, explore its contents
            try
            {
                 if (!Directory.Exists(startItem.Path)) return; // Skip if directory disappeared
                var dirInfo = new DirectoryInfo(startItem.Path);

                // Process files in the current directory
                foreach (var fileInfo in dirInfo.GetFiles())
                {
                    // Skip ignored files
                    if (_ignoredFileNames.Contains(fileInfo.Name)) continue;

                    if (FileHelper.IsTextFile(fileInfo.FullName))
                    {
                        // Check if not already added before creating FSI and potentially counting lines
                        if (!collectedFiles.Any(f => f.Path.Equals(fileInfo.FullName, StringComparison.OrdinalIgnoreCase)))
                        {
                             int? lineCount = null;
                             if (fileInfo.Exists && fileInfo.Length <= MaxFileSizeForLineCount) // Check size limit again
                             {
                                 lineCount = GetLineCount(fileInfo.FullName);
                             }
                             string typeDisplay = string.IsNullOrEmpty(fileInfo.Extension) ? "[Sin extensión]" : fileInfo.Extension;
                             var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = typeDisplay, IsDirectory = false, LineCount = lineCount };
                             collectedFiles.Add(fileFsi);
                        }
                    }
                }

                // Process subdirectories recursively, respecting ignored list
                foreach (var subDirInfo in dirInfo.GetDirectories())
                {
                    if (!_ignoredFolderNames.Contains(subDirInfo.Name))
                    {
                        // Pass LineCount = null for directories
                        var dirFsi = new FileSystemItem { Name = subDirInfo.Name, Path = subDirInfo.FullName, Type = "Directorio", IsDirectory = true, LineCount = null };
                        FindTextFilesRecursive(dirFsi, collectedFiles, rootPath); // Pass rootPath down
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied during recursive text file search in: {startItem.Path}");
                // Skip this directory silently
            }
             catch (DirectoryNotFoundException)
             {
                 Debug.WriteLine($"Directory not found during recursive search: {startItem.Path}");
                 // Skip silently
             }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during recursive text file search in {startItem.Path}: {ex.Message}");
                // Log and continue if possible
            }
        }

        /// <summary>
        /// Recursively finds all files (not directories) within a given directory structure,
        /// respecting ignored folders and files.
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

                // Add files in the current directory, skipping ignored ones
                foreach (var fileInfo in dirInfo.GetFiles())
                {
                     if (_ignoredFileNames.Contains(fileInfo.Name)) continue;

                    // Get line count if applicable
                     int? lineCount = null;
                    if (FileHelper.IsTextFile(fileInfo.FullName) && fileInfo.Exists && fileInfo.Length <= MaxFileSizeForLineCount)
                    {
                        lineCount = GetLineCount(fileInfo.FullName);
                    }
                    string typeDisplay = string.IsNullOrEmpty(fileInfo.Extension) ? "[Sin extensión]" : fileInfo.Extension;
                    var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = typeDisplay, IsDirectory = false, LineCount = lineCount };
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
                        var subDirFsi = new FileSystemItem { Name = subDirInfo.Name, Path = subDirInfo.FullName, Type = "Directorio", IsDirectory = true, LineCount = null };
                        FindAllFilesRecursive(subDirFsi, collectedFiles); // Recursive call
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied during recursive file search in: {startDirectoryItem.Path}");
                // Skip this directory silently
            }
             catch (DirectoryNotFoundException)
             {
                 Debug.WriteLine($"Directory not found during recursive search: {startDirectoryItem.Path}");
                 // Skip silently
             }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during recursive file search in {startDirectoryItem.Path}: {ex.Message}");
                // Log and potentially skip or throw depending on desired behavior
            }
        }

        // --- NEW METHOD ---
        /// <summary>
        /// Recursively finds all descendant files AND directories starting from a given directory,
        /// respecting ignored folders and files. The starting directory itself is NOT included.
        /// </summary>
        /// <param name="startDirectoryItem">The FileSystemItem representing the starting directory.</param>
        /// <param name="collectedItems">A list to which found descendant FileSystemItems (files and directories) will be added.</param>
        public void FindAllDescendantsRecursive(FileSystemItem startDirectoryItem, List<FileSystemItem> collectedItems)
        {
             // Ensure it's actually a directory we're starting with
            if (!startDirectoryItem.IsDirectory || !Directory.Exists(startDirectoryItem.Path))
            {
                return;
            }

            try
            {
                var dirInfo = new DirectoryInfo(startDirectoryItem.Path);

                // Add files in the current directory, skipping ignored ones
                foreach (var fileInfo in dirInfo.GetFiles())
                {
                      if (_ignoredFileNames.Contains(fileInfo.Name)) continue;

                     // Get line count if applicable
                     int? lineCount = null;
                    if (FileHelper.IsTextFile(fileInfo.FullName) && fileInfo.Exists && fileInfo.Length <= MaxFileSizeForLineCount)
                    {
                        lineCount = GetLineCount(fileInfo.FullName);
                    }
                    string typeDisplay = string.IsNullOrEmpty(fileInfo.Extension) ? "[Sin extensión]" : fileInfo.Extension;
                    var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = typeDisplay, IsDirectory = false, LineCount = lineCount };
                    // Use Path equality for uniqueness check
                    if (!collectedItems.Any(f => f.Path.Equals(fileFsi.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        collectedItems.Add(fileFsi);
                    }
                }

                // Process subdirectories recursively, respecting ignored list
                foreach (var subDirInfo in dirInfo.GetDirectories())
                {
                    if (!_ignoredFolderNames.Contains(subDirInfo.Name))
                    {
                        // Create a FileSystemItem for the subdirectory
                        var subDirFsi = new FileSystemItem { Name = subDirInfo.Name, Path = subDirInfo.FullName, Type = "Directorio", IsDirectory = true, LineCount = null };

                        // Add the subdirectory ITSELF to the list
                         if (!collectedItems.Any(f => f.Path.Equals(subDirFsi.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            collectedItems.Add(subDirFsi);
                        }

                        // Recursively find descendants *within* this subdirectory
                        FindAllDescendantsRecursive(subDirFsi, collectedItems);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"Access denied during recursive descendant search in: {startDirectoryItem.Path}");
                // Skip this directory silently
            }
             catch (DirectoryNotFoundException)
             {
                 Debug.WriteLine($"Directory not found during recursive search: {startDirectoryItem.Path}");
                 // Skip silently
             }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during recursive descendant search in {startDirectoryItem.Path}: {ex.Message}");
                // Log and potentially skip or throw depending on desired behavior
            }
        }
        // --- END NEW METHOD ---


    } // End class FileSystemService
} // End namespace MakingVibe.Services