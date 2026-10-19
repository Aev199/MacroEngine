namespace MacroEngine.Core;

/// <summary>
/// Minimal Semantic Versioning 2.0 value: MAJOR.MINOR.PATCH[-prerelease][+build].
/// Precedence follows the spec: build metadata is ignored, a pre-release sorts
/// below its release, identifiers compare numerically when both are numeric.
/// </summary>
internal sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    private SemVersion(int major, int minor, int patch, string prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>Parse "1.2.3", "v1.2.3-beta.1" or "1.2.3+sha". Returns null when malformed.</summary>
    public static SemVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(id => id.Length == 0)) return null;
        }

        string[] parts = s.Split('.');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out int major) || major < 0) return null;
        if (!int.TryParse(parts[1], out int minor) || minor < 0) return null;
        if (!int.TryParse(parts[2], out int patch) || patch < 0) return null;
        return new SemVersion(major, minor, patch, pre);
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A release outranks any of its pre-releases.
        if (!IsPrerelease) return other.IsPrerelease ? 1 : 0;
        if (!other.IsPrerelease) return -1;

        string[] a = Prerelease.Split('.');
        string[] b = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = long.TryParse(a[i], out long ai);
            bool bn = long.TryParse(b[i], out long bi);
            c = an && bn ? ai.CompareTo(bi)
              : an ? -1          // numeric identifiers sort below alphanumeric ones
              : bn ? 1
              : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return Math.Sign(c);
        }
        return a.Length.CompareTo(b.Length);
    }

    public bool Equals(SemVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}
