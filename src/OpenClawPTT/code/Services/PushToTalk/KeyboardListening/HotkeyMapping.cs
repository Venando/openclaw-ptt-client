using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenClawPTT;

/// <summary>
/// Parses hotkey combination strings like "Alt+Shift+Space" and provides
/// platform-specific key codes and modifier flags.
/// </summary>
public static class HotkeyMapping
{
    public static Hotkey Parse(string combination)
    {
        if (string.IsNullOrWhiteSpace(combination))
            throw new ArgumentException("Hotkey combination cannot be empty", nameof(combination));

        var parts = combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Invalid hotkey format", nameof(combination));

        var keyPart = parts[^1]; // last part is the key
        var modifierParts = parts[..^1];

        var modifiers = ParseModifiers(modifierParts);
        var key = ParseKey(keyPart);

        return new Hotkey(key, modifiers);
    }

    private static HashSet<Modifier> ParseModifiers(string[] modifierParts)
    {
        var set = new HashSet<Modifier>();
        foreach (var part in modifierParts)
        {
            var mod = part.ToUpperInvariant() switch
            {
                "ALT" => Modifier.Alt,
                "CTRL" or "CONTROL" => Modifier.Ctrl,
                "SHIFT" => Modifier.Shift,
                "WIN" or "META" or "SUPER" => Modifier.Win,
                _ => throw new ArgumentException($"Unknown modifier: {part}")
            };
            set.Add(mod);
        }
        return set;
    }

    private static Key ParseKey(string keyPart)
    {
        // Normalize: uppercase for letters, otherwise as-is
        var normalized = keyPart.ToUpperInvariant();
        return normalized switch
        {
            "SPACE" => Key.Space,
            "EQUALS" or "=" => Key.Equal,
            "PLUS" or "+" => Key.Equal, // same as equals on US keyboard
            "MINUS" or "-" => Key.Minus,
            "F1" => Key.F1,
            "F2" => Key.F2,
            "F3" => Key.F3,
            "F4" => Key.F4,
            "F5" => Key.F5,
            "F6" => Key.F6,
            "F7" => Key.F7,
            "F8" => Key.F8,
            "F9" => Key.F9,
            "F10" => Key.F10,
            "F11" => Key.F11,
            "F12" => Key.F12,
            _ when normalized.Length == 1 && char.IsLetter(normalized[0]) => new Key(normalized[0]),
            _ when normalized.Length == 1 && char.IsDigit(normalized[0]) => new Key(normalized[0]),
            _ => throw new ArgumentException($"Unsupported key: {keyPart}")
        };
    }

    /// <summary>
    /// Returns the platform-specific virtual key code for the given key.
    /// </summary>
    public static int GetPlatformKeyCode(Key key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsKeyCode(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxKeyCode(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetMacKeyCode(key);
        
        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Returns a mask representing the modifier flags for the given modifiers on the current platform.
    /// </summary>
    public static ulong GetPlatformModifierFlags(HashSet<Modifier> modifiers)
    {
        ulong mask = 0;
        foreach (var mod in modifiers)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                mask |= GetWindowsModifierFlag(mod);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                mask |= GetLinuxModifierFlag(mod);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                mask |= GetMacModifierFlag(mod);
        }
        return mask;
    }

    // Windows virtual key codes (partial)
    private static int GetWindowsKeyCode(Key key)
    {
        return key.Value switch
        {
            'A' => 0x41, // VK_A
            'B' => 0x42,
            'C' => 0x43,
            'D' => 0x44,
            'E' => 0x45,
            'F' => 0x46,
            'G' => 0x47,
            'H' => 0x48,
            'I' => 0x49,
            'J' => 0x4A,
            'K' => 0x4B,
            'L' => 0x4C,
            'M' => 0x4D,
            'N' => 0x4E,
            'O' => 0x4F,
            'P' => 0x50,
            'Q' => 0x51,
            'R' => 0x52,
            'S' => 0x53,
            'T' => 0x54,
            'U' => 0x55,
            'V' => 0x56,
            'W' => 0x57,
            'X' => 0x58,
            'Y' => 0x59,
            'Z' => 0x5A,
            '0' => 0x30,
            '1' => 0x31,
            '2' => 0x32,
            '3' => 0x33,
            '4' => 0x34,
            '5' => 0x35,
            '6' => 0x36,
            '7' => 0x37,
            '8' => 0x38,
            '9' => 0x39,
            _ when key == Key.Space => 0x20, // VK_SPACE
            _ when key == Key.Equal => 0xBB, // VK_OEM_PLUS
            _ when key == Key.Minus => 0xBD, // VK_OEM_MINUS
            _ when key == Key.F1 => 0x70,
            _ when key == Key.F2 => 0x71,
            _ when key == Key.F3 => 0x72,
            _ when key == Key.F4 => 0x73,
            _ when key == Key.F5 => 0x74,
            _ when key == Key.F6 => 0x75,
            _ when key == Key.F7 => 0x76,
            _ when key == Key.F8 => 0x77,
            _ when key == Key.F9 => 0x78,
            _ when key == Key.F10 => 0x79,
            _ when key == Key.F11 => 0x7A,
            _ when key == Key.F12 => 0x7B,
            _ => throw new NotSupportedException($"Key {key} not mapped for Windows")
        };
    }

    private static ulong GetWindowsModifierFlag(Modifier mod)
    {
        return mod switch
        {
            Modifier.Alt => 0x0001, // Not a real flag, we'll handle via VK_MENU detection
            Modifier.Ctrl => 0x0002,
            Modifier.Shift => 0x0004,
            Modifier.Win => 0x0008,
            _ => 0
        };
    }

    // Linux evdev key codes (from input-event-codes.h)
    private static int GetLinuxKeyCode(Key key)
    {
        return key.Value switch
        {
            'A' => 30, // KEY_A
            'B' => 48,
            'C' => 46,
            'D' => 32,
            'E' => 18,
            'F' => 33,
            'G' => 34,
            'H' => 35,
            'I' => 23,
            'J' => 36,
            'K' => 37,
            'L' => 38,
            'M' => 50,
            'N' => 49,
            'O' => 24,
            'P' => 25,
            'Q' => 16,
            'R' => 19,
            'S' => 31,
            'T' => 20,
            'U' => 22,
            'V' => 47,
            'W' => 17,
            'X' => 45,
            'Y' => 21,
            'Z' => 44,
            '0' => 11,
            '1' => 2,
            '2' => 3,
            '3' => 4,
            '4' => 5,
            '5' => 6,
            '6' => 7,
            '7' => 8,
            '8' => 9,
            '9' => 10,
            _ when key == Key.Space => 57, // KEY_SPACE
            _ when key == Key.Equal => 13, // KEY_EQUAL
            _ when key == Key.Minus => 12, // KEY_MINUS
            _ when key == Key.F1 => 59,
            _ when key == Key.F2 => 60,
            _ when key == Key.F3 => 61,
            _ when key == Key.F4 => 62,
            _ when key == Key.F5 => 63,
            _ when key == Key.F6 => 64,
            _ when key == Key.F7 => 65,
            _ when key == Key.F8 => 66,
            _ when key == Key.F9 => 67,
            _ when key == Key.F10 => 68,
            _ when key == Key.F11 => 87,
            _ when key == Key.F12 => 88,
            _ => throw new NotSupportedException($"Key {key} not mapped for Linux")
        };
    }

    private static ulong GetLinuxModifierFlag(Modifier mod)
    {
        // Linux evdev doesn't use modifier flags in same way; we'll track each modifier key individually
        return 0;
    }

    // macOS virtual key codes (from Events.h)
    private static int GetMacKeyCode(Key key)
    {
        return key.Value switch
        {
            'A' => 0x00,
            'B' => 0x0B,
            'C' => 0x08,
            'D' => 0x02,
            'E' => 0x0E,
            'F' => 0x03,
            'G' => 0x05,
            'H' => 0x04,
            'I' => 0x22,
            'J' => 0x26,
            'K' => 0x28,
            'L' => 0x25,
            'M' => 0x2E,
            'N' => 0x2D,
            'O' => 0x1F,
            'P' => 0x23,
            'Q' => 0x0C,
            'R' => 0x0F,
            'S' => 0x01,
            'T' => 0x11,
            'U' => 0x20,
            'V' => 0x09,
            'W' => 0x0D,
            'X' => 0x07,
            'Y' => 0x10,
            'Z' => 0x06,
            '0' => 0x1D,
            '1' => 0x12,
            '2' => 0x13,
            '3' => 0x14,
            '4' => 0x15,
            '5' => 0x17,
            '6' => 0x16,
            '7' => 0x1A,
            '8' => 0x1C,
            '9' => 0x19,
            _ when key == Key.Space => 0x31, // kVK_Space
            _ when key == Key.Equal => 0x18, // kVK_Equal
            _ when key == Key.Minus => 0x1B, // kVK_Minus
            _ when key == Key.F1 => 0x7A,
            _ when key == Key.F2 => 0x78,
            _ when key == Key.F3 => 0x63,
            _ when key == Key.F4 => 0x76,
            _ when key == Key.F5 => 0x60,
            _ when key == Key.F6 => 0x61,
            _ when key == Key.F7 => 0x62,
            _ when key == Key.F8 => 0x64,
            _ when key == Key.F9 => 0x65,
            _ when key == Key.F10 => 0x6D,
            _ when key == Key.F11 => 0x67,
            _ when key == Key.F12 => 0x6F,
            _ => throw new NotSupportedException($"Key {key} not mapped for macOS")
        };
    }

    private static ulong GetMacModifierFlag(Modifier mod)
    {
        return mod switch
        {
            Modifier.Alt => 0x00080000, // kCGEventFlagMaskAlternate
            Modifier.Ctrl => 0x00040000, // kCGEventFlagMaskControl
            Modifier.Shift => 0x00020000, // kCGEventFlagMaskShift
            Modifier.Win => 0x00100000, // kCGEventFlagMaskCommand
            _ => 0
        };
    }
}

/// <summary>
/// Represents a physical key (letter, digit, or special key).
/// </summary>
public readonly struct Key : IEquatable<Key>
{
    public readonly char Value;
    public readonly SpecialKey Special;

    public Key(char c)
    {
        Value = char.ToUpperInvariant(c);
        Special = SpecialKey.None;
    }

    private Key(SpecialKey special)
    {
        Value = '\0';
        Special = special;
    }

    public static Key Space => new(SpecialKey.Space);
    public static Key Equal => new(SpecialKey.Equal);
    public static Key Minus => new(SpecialKey.Minus);
    public static Key F1 => new(SpecialKey.F1);
    public static Key F2 => new(SpecialKey.F2);
    public static Key F3 => new(SpecialKey.F3);
    public static Key F4 => new(SpecialKey.F4);
    public static Key F5 => new(SpecialKey.F5);
    public static Key F6 => new(SpecialKey.F6);
    public static Key F7 => new(SpecialKey.F7);
    public static Key F8 => new(SpecialKey.F8);
    public static Key F9 => new(SpecialKey.F9);
    public static Key F10 => new(SpecialKey.F10);
    public static Key F11 => new(SpecialKey.F11);
    public static Key F12 => new(SpecialKey.F12);

    public override string ToString()
    {
        if (Special != SpecialKey.None)
            return Special.ToString();
        return Value.ToString();
    }

    public bool Equals(Key other) => Value == other.Value && Special == other.Special;
    public override bool Equals(object? obj) => obj is Key other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, Special);

    public static bool operator ==(Key left, Key right) => left.Equals(right);
    public static bool operator !=(Key left, Key right) => !left.Equals(right);
}

public enum SpecialKey
{
    None,
    Space,
    Equal,
    Minus,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
}

public enum Modifier
{
    Alt,
    Ctrl,
    Shift,
    Win
}

public record Hotkey(Key Key, HashSet<Modifier> Modifiers);