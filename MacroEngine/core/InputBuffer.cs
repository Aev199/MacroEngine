using System.Text;

namespace MacroEngine.Core;

/// <summary>
/// Stores a single keystroke: virtual key code, shift state, and the
/// actual Unicode character produced (for debug/logging).
/// </summary>
internal readonly struct KeyStroke
{
    public int VkCode { get; }
    public bool Shift { get; }
    public char Character { get; }

    public KeyStroke(int vkCode, bool shift, char character)
    {
        VkCode = vkCode;
        Shift = shift;
        Character = character;
    }

    public override string ToString() => $"VK=0x{VkCode:X2} Shift={Shift} '{Character}'";
}

/// <summary>
/// Tracks the last N keystrokes as characters (layout-aware via ToUnicodeEx)
/// and detects configured triggers by comparing buffer suffix against trigger strings.
/// </summary>
internal sealed class InputBuffer
{
    private readonly List<KeyStroke> _buffer = new();
    private readonly int _maxLength;
    private readonly object _lock = new();

    /// <summary>
    /// Maps (triggerString, windowContext) → replacementText.
    /// windowContext of "*" means global (matches any window).
    /// </summary>
    private readonly List<TriggerEntry> _triggers = new();

    /// <summary>Fired when a trigger is matched. Provides the full trigger entry.</summary>
    public event Action<TriggerEntry>? TriggerMatched;

    /// <summary>Quick lookup: normalized hotkey string → TriggerEntry.</summary>
    private readonly Dictionary<string, TriggerEntry> _hotkeyMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quick lookup: leader hotkey → list of (triggerText, TriggerEntry) pairs.</summary>
    private Dictionary<string, List<(string Trigger, TriggerEntry Entry)>>? _leaderMap;

    /// <summary>Currently active leader (hotkey was pressed, waiting for text).</summary>
    private string? _activeLeader;
    private DateTime _leaderExpiry;

    public InputBuffer(int maxLength = 64)
    {
        _maxLength = maxLength;
    }

    /// <summary>Load triggers from configuration.</summary>
    public void LoadTriggers(IEnumerable<TriggerEntry> triggers)
    {
        lock (_lock)
        {
            _triggers.Clear();
            _triggers.AddRange(triggers);

            _hotkeyMap.Clear();
            _leaderMap = null;
            foreach (var t in _triggers)
            {
                if (!string.IsNullOrWhiteSpace(t.Hotkey))
                    _hotkeyMap[NormalizeHotkey(t.Hotkey)] = t;
                if (!string.IsNullOrWhiteSpace(t.Leader) && !string.IsNullOrEmpty(t.Trigger))
                {
                    _leaderMap ??= new(StringComparer.OrdinalIgnoreCase);
                    string norm = NormalizeHotkey(t.Leader);
                    if (!_leaderMap.ContainsKey(norm))
                        _leaderMap[norm] = new();
                    _leaderMap[norm].Add((t.Trigger, t));
                }
            }
        }
    }

    /// <summary>Check if a key combo matches any hotkey trigger. Returns the entry or null.</summary>
    public TriggerEntry? MatchHotkey(string combo)
    {
        lock (_lock)
        {
            string norm = NormalizeHotkey(combo);
            _hotkeyMap.TryGetValue(norm, out var entry);
            return entry;
        }
    }

    /// <summary>Activate leader mode for N seconds. Called when leader hotkey is pressed.</summary>
    public void ActivateLeader(string hotkeyCombo)
    {
        string norm = NormalizeHotkey(hotkeyCombo);
        if (_leaderMap != null && _leaderMap.ContainsKey(norm))
        {
            _activeLeader = norm;
            _leaderExpiry = DateTime.UtcNow.AddSeconds(3);
        }
    }

    /// <summary>
    /// Check buffer suffix against leader triggers for the currently active leader.
    /// Fires TriggerMatched and returns true if a leader trigger matched.
    /// </summary>
    public bool CheckLeaderTriggers(string windowFingerprint)
    {
        if (_activeLeader == null || _leaderMap == null) return false;
        if (DateTime.UtcNow > _leaderExpiry) { _activeLeader = null; return false; }

        List<KeyStroke> bufferSnapshot;
        List<(string Trigger, TriggerEntry Entry)> leaderTriggers;
        lock (_lock)
        {
            bufferSnapshot = new List<KeyStroke>(_buffer);
            if (!_leaderMap.TryGetValue(_activeLeader, out var list)) return false;
            leaderTriggers = new List<(string Trigger, TriggerEntry Entry)>(list);
        }

        if (bufferSnapshot.Count == 0) return false;

        foreach (var (trigger, entry) in leaderTriggers)
        {
            if (!WindowContext.MatchesContext(entry.Context, windowFingerprint))
                continue;

            int tLen = trigger.Length;
            if (tLen == 0 || tLen > bufferSnapshot.Count)
                continue;

            bool matched = true;
            int bufStart = bufferSnapshot.Count - tLen;
            for (int i = 0; i < tLen; i++)
            {
                if (bufferSnapshot[bufStart + i].Character != trigger[i])
                { matched = false; break; }
            }

            if (matched)
            {
                _activeLeader = null; // consumed
                lock (_lock) { _buffer.Clear(); }
                TriggerMatched?.Invoke(entry);
                return true;
            }
        }
        return false;
    }

    /// <summary>Normalize hotkey strings for case-insensitive matching.</summary>
    private static string NormalizeHotkey(string hk) =>
        hk.Replace(" ", "").ToLowerInvariant();

    /// <summary>Feed a keystroke into the buffer and check for triggers.</summary>
    public void Feed(KeyEventData key, string? windowFingerprint = null)
    {
        // Handle special keys
        if (key.VirtualKeyCode == 0x08) // Backspace
        {
            lock (_lock)
            {
                if (_buffer.Count > 0)
                    _buffer.RemoveAt(_buffer.Count - 1);
            }
            return;
        }

        if (key.VirtualKeyCode is 0x0D or 0x1B) // Enter or Escape
        {
            lock (_lock) { _buffer.Clear(); }
            return;
        }

        // Only process keys that produce a printable character.
        char c = key.ToChar();
        if (c == '\0') return;

        // Add keystroke to buffer
        var stroke = new KeyStroke(key.VirtualKeyCode, key.Shift, c);
        lock (_lock)
        {
            _buffer.Add(stroke);
            while (_buffer.Count > _maxLength)
                _buffer.RemoveAt(0);
        }

        // Check for trigger matches
        CheckTriggers(windowFingerprint ?? "*");
    }

    /// <summary>Clear the entire buffer.</summary>
    public void Clear()
    {
        lock (_lock) { _buffer.Clear(); }
    }

    /// <summary>Get current buffer as debug string.</summary>
    public string GetBufferDebug()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var s in _buffer)
                sb.Append(s.Character);
            return sb.ToString();
        }
    }

    private void CheckTriggers(string windowFingerprint)
    {
        // Take a snapshot of the buffer under lock
        List<KeyStroke> bufferSnapshot;
        List<TriggerEntry> triggersCopy;
        lock (_lock)
        {
            bufferSnapshot = new List<KeyStroke>(_buffer);
            triggersCopy = new List<TriggerEntry>(_triggers);
        }

        if (bufferSnapshot.Count == 0) return;

        foreach (var trigger in triggersCopy)
        {
            // Skip leader-only triggers — they fire via CheckLeaderTriggers()
            if (!string.IsNullOrWhiteSpace(trigger.Leader))
                continue;

            // Check context match (supports "*", comma-separated, exclusion with "!")
            if (!WindowContext.MatchesContext(trigger.Context, windowFingerprint))
                continue;

            int tLen = trigger.Trigger.Length;
            if (tLen == 0 || tLen > bufferSnapshot.Count)
                continue;

            // Compare buffer suffix characters against trigger string.
            bool matched = true;
            int bufStart = bufferSnapshot.Count - tLen;
            for (int i = 0; i < tLen; i++)
            {
                if (bufferSnapshot[bufStart + i].Character != trigger.Trigger[i])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                // Clear buffer immediately to prevent residual trigger chars
                // from causing spurious matches on subsequent keystrokes.
                lock (_lock) { _buffer.Clear(); }
                TriggerMatched?.Invoke(trigger);
                return; // Only fire the first matching trigger
            }
        }
    }
}

/// <summary>A single trigger configuration entry.</summary>
internal sealed class TriggerEntry
{
    public string Trigger { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Context { get; set; } = "*";
    public string Action { get; set; } = "text";
    public string? Hotkey { get; set; }
    public string? Leader { get; set; }  // Hotkey that must be pressed first, e.g. "Alt+Space"
}
