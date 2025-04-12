using System;
using System.Diagnostics.CodeAnalysis; // For MaybeNullWhen

namespace MakingVibe.Models
{
    /// <summary>
    /// Represents a file or directory in the file system.
    /// </summary>
    public class FileSystemItem : IEquatable<FileSystemItem>
    {
        public required string Name { get; set; }
        public required string Path { get; set; }
        public required string Type { get; set; } // e.g., "Directorio", ".txt", ".cs"
        public bool IsDirectory { get; set; }

        // Implement IEquatable for correct functioning in collections like HashSet or Distinct()
        public bool Equals(FileSystemItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            // Compare by Path for uniqueness
            return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FileSystemItem);
        }

        public override int GetHashCode()
        {
            // Use Path's hash code (case-insensitive)
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty);
        }

        public static bool operator ==(FileSystemItem? left, FileSystemItem? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(FileSystemItem? left, FileSystemItem? right)
        {
            return !Equals(left, right);
        }
    }
}