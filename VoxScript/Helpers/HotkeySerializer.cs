using VoxScript.Native.Platform;

namespace VoxScript.Helpers;

public static class HotkeySerializer
{
    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Enter"] = 0x0D,
        ["Tab"] = 0x09,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Up"] = 0x26,
        ["Down"] = 0x28,
        ["Left"] = 0x25,
        ["Right"] = 0x27,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    private static readonly Dictionary<int, string> VkToName =
        NameToVk.GroupBy(kv => kv.Value)
                 .ToDictionary(g => g.Key, g => g.First().Key);

    public static HotkeyCombo? Parse(string? hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString) || hotkeyString == "Not set")
            return null;

        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        int? triggerKey = null;

        foreach (var part in parts)
        {
            if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Ctrl;
            else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Win;
            else if (NameToVk.TryGetValue(part, out var vk))
                triggerKey = vk;
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                triggerKey = char.ToUpper(part[0]);
        }

        return new HotkeyCombo(modifiers, triggerKey);
    }

    public static string Serialize(HotkeyCombo? combo)
    {
        if (combo is null) return "Not set";

        var parts = new List<string>();
        if (combo.Modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (combo.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (combo.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (combo.Modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");

        if (combo.TriggerKey is int vk)
        {
            if (VkToName.TryGetValue(vk, out var name))
                parts.Add(name);
            else if (vk is >= 0x30 and <= 0x5A)
                parts.Add(((char)vk).ToString());
            else
                parts.Add($"0x{vk:X2}");
        }

        return parts.Count > 0 ? string.Join("+", parts) : "Not set";
    }

    public static HotkeyCombo FromVirtualKeys(ModifierKeys modifiers, int? triggerVk)
    {
        return new HotkeyCombo(modifiers, triggerVk);
    }

    public static bool IsModifierKey(int vkCode)
    {
        return vkCode is
            0xA0 or 0xA1 or 0x10 or  // Shift (L, R, generic)
            0xA2 or 0xA3 or 0x11 or  // Ctrl (L, R, generic)
            0xA4 or 0xA5 or 0x12 or  // Alt (L, R, generic)
            0x5B or 0x5C;             // Win (L, R)
    }

    public static ModifierKeys VkToModifier(int vkCode)
    {
        return vkCode switch
        {
            0xA0 or 0xA1 or 0x10 => ModifierKeys.Shift,
            0xA2 or 0xA3 or 0x11 => ModifierKeys.Ctrl,
            0xA4 or 0xA5 or 0x12 => ModifierKeys.Alt,
            0x5B or 0x5C => ModifierKeys.Win,
            _ => ModifierKeys.None,
        };
    }
}
