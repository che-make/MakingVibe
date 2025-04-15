using System;

namespace MakingVibe.Models
{
    /// <summary>
    /// Representa una ruta guardada por el usuario para acceso rápido.
    /// </summary>
    public class SavedPath
    {
        public required string Path { get; set; }
        public string DisplayName { get; set; }
        public DateTime SavedDate { get; set; }

        public SavedPath()
        {
            DisplayName = string.Empty;
            SavedDate = DateTime.Now;
        }

        public override string ToString()
        {
            return !string.IsNullOrEmpty(DisplayName) ? DisplayName : System.IO.Path.GetFileName(Path) ?? Path;
        }
    }
}