using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace MakingVibe.Services
{
    /// <summary>
    /// Provides Git-related functionalities.
    /// </summary>
    public class GitService
    {
        /// <summary>
        /// Attempts to find the root of the Git repository containing the given path.
        /// </summary>
        /// <param name="startingPath">The path to start searching upwards from.</param>
        /// <returns>The full path to the repository root, or null if not found.</returns>
        public string? FindGitRepositoryRoot(string? startingPath)
        {
            if (string.IsNullOrEmpty(startingPath)) return null;

            try
            {
                DirectoryInfo? currentDir = new DirectoryInfo(startingPath);
                while (currentDir != null)
                {
                    if (Directory.Exists(Path.Combine(currentDir.FullName, ".git")))
                    {
                        return currentDir.FullName;
                    }
                    currentDir = currentDir.Parent;
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error searching for git repository root: {ex.Message}");
            }
            return null; // Not found or error occurred
        }

        /// <summary>
        /// Gets the subject line of the last Git commit in the repository containing the application.
        /// </summary>
        /// <returns>The commit subject, or null if unavailable.</returns>
        public string? GetLastCommitSubject()
        {
            // Find repo root relative to the application's location
            string? repoPath = FindGitRepositoryRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (repoPath == null)
            {
                Debug.WriteLine("Git repository root not found relative to application.");
                return null;
            }

            try
            {
                // Assumes git is in PATH
                var startInfo = new ProcessStartInfo("git", "log -1 --pretty=%s")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repoPath // Execute in the repo root
                };

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    Debug.WriteLine("Failed to start git process.");
                    return null;
                }

                string output = process.StandardOutput.ReadToEnd(); // Read output
                process.WaitForExit(); // Wait for completion

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
                else
                {
                    Debug.WriteLine($"Git command failed or returned empty. Exit code: {process.ExitCode}");
                    return null;
                }
            }
            catch (Win32Exception) // Catch if 'git' command is not found
            {
                Debug.WriteLine("Error running git command. Is git installed and in PATH?");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting git commit info: {ex.Message}");
                return null;
            }
        }
    }
}