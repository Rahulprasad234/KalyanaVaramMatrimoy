using System.Globalization;
using System.Text.RegularExpressions;

namespace KalyanaVaran.Api.Common;

/// <summary>Converts the UI's label values into proper typed columns.</summary>
public static partial class ProfileParsing
{
    private static readonly string[] Months = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12];

    private static readonly string[] TimeFormats =
        ["hh:mm tt", "h:mm tt", "hh:mmtt", "h:mmtt", "HH:mm", "H:mm", "HH:mm:ss"];

    /// <summary>"14", "August", "1997" -> 1997-08-14. Month may also be "8".</summary>
    public static bool TryBuildDob(string? day, string? month, string? year, out DateOnly dob)
    {
        dob = default;
        if (!int.TryParse(day, out var d) || !int.TryParse(year, out var y)) return false;

        var m = Array.FindIndex(Months, x => x.Equals(month?.Trim(), StringComparison.OrdinalIgnoreCase)) + 1;
        if (m == 0 && !int.TryParse(month, out m)) return false;

        if (m is < 1 or > 12 || y < 1900 || y > 2100) return false;
        if (d < 1 || d > DateTime.DaysInMonth(y, m)) return false;

        dob = new DateOnly(y, m, d);
        return true;
    }

    public static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return age;
    }

    /// <summary>"5'5\" (165 cm)" -> 165. Plain "165" also accepted.</summary>
    public static bool TryParseHeightCm(string? label, out short cm)
    {
        cm = 0;
        if (string.IsNullOrWhiteSpace(label)) return false;
        var match = CmRegex().Match(label);
        var raw = match.Success ? match.Groups[1].Value : label.Trim();
        return short.TryParse(raw, out cm) && cm is >= 100 and <= 250;
    }

    /// <summary>Accepts "06:45 AM" and "06:45".</summary>
    public static bool TryParseBirthTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim().ToUpperInvariant(), TimeFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string? FormatBirthTime(TimeOnly? t) =>
        t?.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static (string First, string Last) SplitName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? ("", "") : (parts[0], string.Join(' ', parts.Skip(1)));
    }

    [GeneratedRegex(@"(\d{2,3})\s*cm", RegexOptions.IgnoreCase)]
    private static partial Regex CmRegex();
}
