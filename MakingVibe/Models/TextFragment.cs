// MakingVibe/Models/TextFragment.cs
using System.Text.Json.Serialization;

namespace MakingVibe.Models
{
    public class TextFragment
    {
        // Title for display and identification in the list
        public required string Title { get; set; }

        // The actual text content of the fragment
        public required string Text { get; set; }

        // Update ToString to show the title for easier debugging
        public override string ToString()
        {
            return Title ?? "[No Title]";
        }
    }
}