/*
 * MakingVibe/Models/TextFragment.cs
 * No changes needed in this file.
 */
// MakingVibe/Models/TextFragment.cs
using System.ComponentModel; // Required for INotifyPropertyChanged
using System.Runtime.CompilerServices; // Required for CallerMemberName

namespace MakingVibe.Models
{
    // MODIFIED: Implement INotifyPropertyChanged for UI updates
    public class TextFragment : INotifyPropertyChanged
    {
        private string _title = string.Empty; // Initialize with default
        private string _text = string.Empty;  // Initialize with default

        // Title for display and identification in the list
        public required string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        // The actual text content of the fragment
        public required string Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        // Update ToString to show the title for easier debugging
        public override string ToString()
        {
            return Title ?? "[No Title]";
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Helper method for setting properties and raising PropertyChanged
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}