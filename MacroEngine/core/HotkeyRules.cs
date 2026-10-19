namespace MacroEngine.Core;

/// <summary>Shared validation and normalization for configured shortcuts.</summary>
internal static class HotkeyRules
{
    private static readonly string[] ModifierOrder = ["ctrl", "alt", "shift"];

    public static string Normalize(string combo)
    {
        string[] parts = combo.Split(
            '+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>();

        foreach (string rawPart in parts)
        {
            string part = NormalizeModifier(rawPart);
            if (ModifierOrder.Contains(part, StringComparer.OrdinalIgnoreCase))
                modifiers.Add(part);
            else
                keys.Add(part.ToLowerInvariant());
        }

        return string.Join("+", ModifierOrder.Where(modifiers.Contains).Concat(keys));
    }

    public static bool TryValidateShortcut(string combo, out string error)
    {
        Parse(combo, out var modifiers, out var keys, out bool containsUnsupportedModifier);

        if (containsUnsupportedModifier)
        {
            error = "Поддерживаются только модификаторы Ctrl, Alt и Shift.";
            return false;
        }

        if (keys.Count != 1)
        {
            error = "Сочетание должно содержать ровно одну основную клавишу.";
            return false;
        }

        bool isFunctionKey = IsFunctionKey(keys[0]);
        if (!isFunctionKey
            && !modifiers.Contains("ctrl")
            && !modifiers.Contains("alt"))
        {
            error = "Для обычной клавиши требуется Ctrl или Alt; без модификаторов доступны F1–F24.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateLeader(string combo, out string error)
    {
        Parse(combo, out var modifiers, out var keys, out bool containsUnsupportedModifier);

        if (containsUnsupportedModifier || keys.Count != 0)
        {
            error = "Лидер должен состоять только из Ctrl, Alt и Shift.";
            return false;
        }

        if (modifiers.Count is < 2 or > 3)
        {
            error = "Лидер должен содержать два или три разных модификатора.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void Parse(
        string combo,
        out HashSet<string> modifiers,
        out List<string> keys,
        out bool containsUnsupportedModifier)
    {
        modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        keys = new List<string>();
        containsUnsupportedModifier = false;

        foreach (string rawPart in combo.Split(
                     '+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = NormalizeModifier(rawPart);
            if (ModifierOrder.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                modifiers.Add(part);
            }
            else if (part.Equals("win", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                containsUnsupportedModifier = true;
            }
            else
            {
                keys.Add(part);
            }
        }
    }

    private static string NormalizeModifier(string part) => part.ToLowerInvariant() switch
    {
        "control" => "ctrl",
        _ => part.ToLowerInvariant()
    };

    private static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3
        && (key[0] is 'f' or 'F')
        && int.TryParse(key[1..], out int number)
        && number is >= 1 and <= 24;
}
