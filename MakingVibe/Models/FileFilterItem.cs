/*
 * MakingVibe/Models/FileFilterItem.cs
 * New file to represent a filter item in the UI.
 */
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MakingVibe.Models
{
    public class FileFilterItem : INotifyPropertyChanged
    {
        private bool _isEnabled = true; // Default to enabled
        private string _extension = string.Empty;
        private int _count;

        public string Extension
        {
            get => _extension;
            set => SetField(ref _extension, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetField(ref _isEnabled, value))
                {
                    // Optionally raise another event or trigger logic when enabled state changes
                    OnPropertyChanged(nameof(DisplayMember)); // Update display if it depends on IsEnabled
                }
            }
        }

        public int Count
        {
            get => _count;
            set => SetField(ref _count, value);
        }

        // Property for display in the ListView, showing extension and count
        public string DisplayMember => $"{Extension} ({Count})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            // Special handling to update DisplayMember when Count or Extension changes
            if (propertyName == nameof(Count) || propertyName == nameof(Extension))
            {
                OnPropertyChanged(nameof(DisplayMember));
            }
            return true;
        }

        // Override Equals and GetHashCode for potential use in collections/Distinct
        public override bool Equals(object? obj)
        {
            return obj is FileFilterItem item &&
                   Extension == item.Extension;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Extension);
        }
    }
}