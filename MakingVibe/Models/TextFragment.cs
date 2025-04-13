// MakingVibe/Models/TextFragment.cs
using System.Text.Json.Serialization; // Required for ignoring properties during serialization if needed in future

namespace MakingVibe.Models
{
    public class TextFragment
    {
        public required string Text { get; set; }

        // Optional: Override ToString for easier debugging or simple display if needed
        public override string ToString()
        {
            return Text ?? string.Empty;
        }
    }
}