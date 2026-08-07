using System.Text;

namespace MacroEngine.Core;

/// <summary>One layout-aware keystroke stored in the text trigger buffer.</summary>
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
/// Tracks recent printable input, direct shortcuts and held leader sequences.
/// </summary>
internal sealed class InputBuffer
{
    private readonly List<KeyStroke> _buffer = new();
    private readonly int _maxLength;
    private readonly object _lock = new();
    private readonly List<TriggerEntry> _triggers = new();
    private readonly Dictionary<string, TriggerEntry> _hotkeyMap =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, List<(string Seq, TriggerEntry Entry)>>? _leaderMap;
    private string? _leaderMods;
    private readonly StringBuilder _leaderSeq = new();

    public event Action<TriggerEntry>? TriggerMatched;

    public string? CurrentLeaderMods => _leaderMods;
    public string CurrentLeaderSeq => _leaderSeq.ToString();
    public string LastMatchedLeaderSeq { get; private set; } = "";

    public InputBuffer(int maxLength = 64)
    {
        _maxLength = maxLength;
    }

    public void LoadTriggers(IEnumerable<TriggerEntry> triggers)
    {
        lock (_lock)
        {
            _triggers.Clear();
            _triggers.AddRange(triggers);

            _hotkeyMap.Clear();
            _leaderMap = null;
            ResetLeaderUnsafe();

            foreach (var trigger in _triggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger.Hotkey))
                    _hotkeyMap[NormalizeHotkey(trigger.Hotkey)] = trigger;

                if (!string.IsNullOrWhiteSpace(trigger.Leader)
                    && !string.IsNullOrEmpty(trigger.Trigger))
                {
                    _leaderMap ??= new(StringComparer.OrdinalIgnoreCase);
                    string normalizedLeader = NormalizeHotkey(trigger.Leader);
                    if (!_leaderMap.TryGetValue(normalizedLeader, out var entries))
                    {
                        entries = new List<(string Seq, TriggerEntry Entry)>();
                        _leaderMap[normalizedLeader] = entries;
                    }

                    entries.Add((trigger.Trigger.ToLowerInvariant(), trigger));
                }
            }
        }
    }

    /// <summary>
    /// Match a direct shortcut. The returned entry has an empty Trigger so the
    /// execution layer never backspaces a legacy trigger label attached to a hotkey.
    /// </summary>
    public TriggerEntry? MatchHotkey(string combo)
    {
        lock (_lock)
        {
            if (!_hotkeyMap.TryGetValue(NormalizeHotkey(combo), out var entry))
                return null;

            return new TriggerEntry
            {
                Trigger = "",
                Value = entry.Value,
                Context = entry.Context,
                Action = entry.Action,
                Hotkey = entry.Hotkey,
                Leader = null
            };
        }
    }

    /// <summary>
    /// Feed a non-modifier key pressed while a leader chord is held.
    /// </summary>
    public bool FeedLeaderKey(
        string modPrefix,
        string keyName,
        string windowFingerprint,
        out bool swallow)
    {
        swallow = false;

        string key = keyName.ToLowerInvariant();
        if (key.Length == 0) return false;

        string normalizedMods = NormalizeHotkey(modPrefix);
        List<(string Seq, TriggerEntry Entry)> candidates;

        lock (_lock)
        {
            if (_leaderMap == null
                || !_leaderMap.TryGetValue(normalizedMods, out var configured))
            {
                ResetLeaderUnsafe();
                return false;
            }

            candidates = new List<(string Seq, TriggerEntry Entry)>(configured);
        }

        if (_leaderMods != normalizedMods)
        {
            _leaderMods = normalizedMods;
            _leaderSeq.Clear();
        }

        foreach (string attempt in new[] { _leaderSeq.ToString() + key, key })
        {
            if (!TryMatchLeader(
                    candidates,
                    attempt,
                    windowFingerprint,
                    out TriggerEntry? exact,
                    out bool isPrefix))
            {
                continue;
            }

            if (exact != null)
            {
                LastMatchedLeaderSeq = attempt;
                ResetLeader();
                swallow = true;
                TriggerMatched?.Invoke(exact);
                return true;
            }

            if (isPrefix)
            {
                _leaderSeq.Clear();
                _leaderSeq.Append(attempt);
                swallow = true;
                return false;
            }
        }

        ResetLeader();
        return false;
    }

    public void ResetLeader()
    {
        lock (_lock)
            ResetLeaderUnsafe();
    }

    private void ResetLeaderUnsafe()
    {
        _leaderMods = null;
        _leaderSeq.Clear();
    }

    private static bool TryMatchLeader(
        IEnumerable<(string Seq, TriggerEntry Entry)> candidates,
        string attempt,
        string windowFingerprint,
        out TriggerEntry? exact,
        out bool isPrefix)
    {
        exact = null;
        isPrefix = false;

        foreach (var (sequence, entry) in candidates)
        {
            if (!WindowContext.MatchesContext(entry.Context, windowFingerprint))
                continue;

            if (string.Equals(sequence, attempt, StringComparison.OrdinalIgnoreCase))
            {
                exact = entry;
                return true;
            }

            if (sequence.StartsWith(attempt, StringComparison.OrdinalIgnoreCase))
                isPrefix = true;
        }

        return isPrefix;
    }

    private static string NormalizeHotkey(string hotkey) => HotkeyRules.Normalize(hotkey);

    public void Feed(KeyEventData key, string? windowFingerprint = null)
    {
        if (key.VirtualKeyCode == 0x08)
        {
            lock (_lock)
            {
                if (_buffer.Count > 0)
                    _buffer.RemoveAt(_buffer.Count - 1);
            }
            return;
        }

        if (key.VirtualKeyCode is 0x0D or 0x1B)
        {
            Clear();
            return;
        }

        char character = key.ToChar();
        if (character == '\0') return;

        lock (_lock)
        {
            _buffer.Add(new KeyStroke(key.VirtualKeyCode, key.Shift, character));
            while (_buffer.Count > _maxLength)
                _buffer.RemoveAt(0);
        }

        CheckTriggers(windowFingerprint ?? "*");
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
            ResetLeaderUnsafe();
        }
    }

    public string GetBufferDebug()
    {
        lock (_lock)
        {
            var result = new StringBuilder();
            foreach (var stroke in _buffer)
                result.Append(stroke.Character);
            return result.ToString();
        }
    }

    private void CheckTriggers(string windowFingerprint)
    {
        List<KeyStroke> bufferSnapshot;
        List<TriggerEntry> triggersSnapshot;
        lock (_lock)
        {
            bufferSnapshot = new List<KeyStroke>(_buffer);
            triggersSnapshot = new List<TriggerEntry>(_triggers);
        }

        if (bufferSnapshot.Count == 0) return;

        foreach (var trigger in triggersSnapshot)
        {
            // A shortcut/leader is not also a typed trigger, even if an older
            // configuration still contains a descriptive Trigger value.
            if (!string.IsNullOrWhiteSpace(trigger.Hotkey)
                || !string.IsNullOrWhiteSpace(trigger.Leader))
            {
                continue;
            }

            if (!WindowContext.MatchesContext(trigger.Context, windowFingerprint))
                continue;

            int triggerLength = trigger.Trigger.Length;
            if (triggerLength == 0 || triggerLength > bufferSnapshot.Count)
                continue;

            int start = bufferSnapshot.Count - triggerLength;
            bool matches = true;
            for (int i = 0; i < triggerLength; i++)
            {
                if (bufferSnapshot[start + i].Character == trigger.Trigger[i])
                    continue;

                matches = false;
                break;
            }

            if (!matches) continue;

            lock (_lock)
                _buffer.Clear();
            TriggerMatched?.Invoke(trigger);
            return;
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
    public string? Leader { get; set; }
}
