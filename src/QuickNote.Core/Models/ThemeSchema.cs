using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickNote.Core.Models
{
    public class ThemeSchema
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("baseVariant")]
        public string BaseVariant { get; set; } = "Dark"; // "Dark" or "Light"

        [JsonPropertyName("isBuiltIn")]
        public bool IsBuiltIn { get; set; } = false;

        [JsonPropertyName("accentColor")]
        public string AccentColor { get; set; } = "#3390ec";

        [JsonPropertyName("previewBackground")]
        public string PreviewBackground { get; set; } = "#0b0e14";

        [JsonPropertyName("colors")]
        public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string GetColor(string tokenName, string defaultColor = "")
        {
            if (Colors != null && Colors.TryGetValue(tokenName, out var color) && !string.IsNullOrWhiteSpace(color))
            {
                return color.Trim();
            }
            if (tokenName.Equals("AppAccent", StringComparison.OrdinalIgnoreCase) && Colors != null && Colors.TryGetValue("Accent", out var acc) && !string.IsNullOrWhiteSpace(acc))
            {
                return acc.Trim();
            }
            if (tokenName.Equals("Accent", StringComparison.OrdinalIgnoreCase) && Colors != null && Colors.TryGetValue("AppAccent", out var appAcc) && !string.IsNullOrWhiteSpace(appAcc))
            {
                return appAcc.Trim();
            }
            if ((tokenName.Equals("AppAccent", StringComparison.OrdinalIgnoreCase) || tokenName.Equals("Accent", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(AccentColor))
            {
                return AccentColor.Trim();
            }
            return defaultColor;
        }

        public void SetColor(string tokenName, string colorHex)
        {
            Colors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Colors[tokenName] = colorHex;
        }

        public ThemeSchema Clone()
        {
            return new ThemeSchema
            {
                Id = Id,
                Name = Name,
                Description = Description,
                BaseVariant = BaseVariant,
                IsBuiltIn = IsBuiltIn,
                AccentColor = AccentColor,
                PreviewBackground = PreviewBackground,
                Colors = new Dictionary<string, string>(Colors, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
