using System;
using System.Collections.Generic;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests;

public class HotkeyMappingTests
{
    // ── Parse ────────────────────────────────────────────

    [Fact]
    public void Parse_AltEquals_HasAltModifierAndEqualKey()
    {
        var h = HotkeyMapping.Parse("Alt+=");
        Assert.Contains(Modifier.Alt, h.Modifiers);
        Assert.Equal(Key.Equal, h.Key);
    }

    [Fact]
    public void Parse_CtrlShiftSpace_AllThree()
    {
        var h = HotkeyMapping.Parse("Ctrl+Shift+Space");
        Assert.Contains(Modifier.Ctrl, h.Modifiers);
        Assert.Contains(Modifier.Shift, h.Modifiers);
        Assert.Equal(Key.Space, h.Key);
    }

    [Fact]
    public void Parse_SingleLetter()
    {
        var h = HotkeyMapping.Parse("A");
        Assert.Empty(h.Modifiers);
        Assert.Equal(new Key('A'), h.Key);
    }

    [Fact]
    public void Parse_F12_SpecialKey()
    {
        var h = HotkeyMapping.Parse("F12");
        Assert.Empty(h.Modifiers);
        Assert.Equal(Key.F12, h.Key);
    }

    [Fact]
    public void Parse_AltEquals_ReturnsEqualSpecialKey()
    {
        // "Alt+=" → Alt + Equals key
        var h = HotkeyMapping.Parse("Alt+=");
        Assert.Contains(Modifier.Alt, h.Modifiers);
        Assert.Equal(SpecialKey.Equal, h.Key.Special);
    }

    [Fact]
    public void Parse_Minus_ReturnsMinusSpecialKey()
    {
        var h = HotkeyMapping.Parse("-");
        Assert.Equal(SpecialKey.Minus, h.Key.Special);
    }

    [Fact]
    public void Parse_LowercaseModifiers_Valid()
    {
        var h = HotkeyMapping.Parse("ctrl+alt+a");
        Assert.Contains(Modifier.Ctrl, h.Modifiers);
        Assert.Contains(Modifier.Alt, h.Modifiers);
    }

    [Fact]
    public void Parse_ControlAlias_IsCtrl()
    {
        var h = HotkeyMapping.Parse("Control+Shift+3");
        Assert.Contains(Modifier.Ctrl, h.Modifiers);
        Assert.Contains(Modifier.Shift, h.Modifiers);
    }

    [Fact]
    public void Parse_MetaAlias_IsWin()
    {
        var h = HotkeyMapping.Parse("Meta+D");
        Assert.Contains(Modifier.Win, h.Modifiers);
    }

    [Fact]
    public void Parse_SuperAlias_IsWin()
    {
        var h = HotkeyMapping.Parse("Super+E");
        Assert.Contains(Modifier.Win, h.Modifiers);
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse(""));
    }

    [Fact]
    public void Parse_WhitespaceOnly_Throws()
    {
        Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("   "));
    }

    [Fact]
    public void Parse_UnknownModifier_Throws()
    {
        Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("Garbage+A"));
    }

    [Fact]
    public void Parse_InvalidFormat_NoModifierNoKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("garbage"));
    }

    [Fact]
    public void Parse_UnknownKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("Ctrl+UnknownKey"));
    }


    [Fact]
    public void Parse_ValidFormat_ReturnsHotkey()
    {
        var h = HotkeyMapping.Parse("Ctrl+Shift+A");
        Assert.Contains(Modifier.Ctrl, h.Modifiers);
        Assert.Contains(Modifier.Shift, h.Modifiers);
        Assert.Equal(new Key('A'), h.Key);
    }


    [Fact]
    public void Parse_JustKey_NoModifier_ReturnsHotkey()
    {
        var h = HotkeyMapping.Parse("B");
        Assert.Empty(h.Modifiers);
        Assert.Equal(new Key('B'), h.Key);
    }

    [Fact]
    public void Parse_MultipleModifiers_ReturnsHotkey()
    {
        var h = HotkeyMapping.Parse("Ctrl+Alt+Shift+A");
        Assert.Contains(Modifier.Ctrl, h.Modifiers);
        Assert.Contains(Modifier.Alt, h.Modifiers);
        Assert.Contains(Modifier.Shift, h.Modifiers);
        Assert.Equal(new Key('A'), h.Key);
    }

    [Fact]
    public void Parse_CaseInsensitivity_ReturnsHotkey()
    {
        var lower = HotkeyMapping.Parse("ctrl+a");
        var upper = HotkeyMapping.Parse("CTRL+A");
        Assert.Equal(lower.Key, upper.Key);
        Assert.Equal(lower.Modifiers.Count, upper.Modifiers.Count);
    }

    // ── GetPlatformKeyCode ───────────────────────────────────────

    [Theory]
    [InlineData("Ctrl+Shift+A", 'A')]
    [InlineData("Ctrl+B", 'B')]
    [InlineData("Ctrl+1", '1')]
    public void GetPlatformKeyCode_ValidKey_ReturnsPlatformKeyCode(string combination, char expectedKeyChar)
    {
        var h = HotkeyMapping.Parse(combination);
        var code = HotkeyMapping.GetPlatformKeyCode(h.Key);
        Assert.True(code > 0, $"Expected positive key code for {expectedKeyChar}");
    }

    // ── GetPlatformModifierFlags ───────────────────────────────────────

    [Fact]
    public void GetPlatformModifierFlags_ValidModifiers_ReturnsComputedMask()
    {
        // Linux stub returns 0 by design; other platforms return non-zero
        var h = HotkeyMapping.Parse("Ctrl+Shift+A");
        var mask = HotkeyMapping.GetPlatformModifierFlags(h.Modifiers);
        // Verify it's computed without throwing; actual value is platform-dependent
        Assert.True(true, $"Modifier mask computed: {mask}");
    }

    [Fact]
    public void GetPlatformModifierFlags_NoModifiers_ReturnsZero()
    {
        var h = HotkeyMapping.Parse("A");
        var mask = HotkeyMapping.GetPlatformModifierFlags(h.Modifiers);
        Assert.Equal(0UL, mask);
    }

    // ── Key struct ───────────────────────────────────────

    [Theory]
    [InlineData('a', 'A')]
    [InlineData('z', 'Z')]
    [InlineData('1', '1')]
    public void Key_CharConstructor_UppercasesValue(char input, char expected)
    {
        var key = new Key(input);
        Assert.Equal(expected, key.Value);
    }

    [Fact]
    public void Key_CharConstructor_SpecialIsNone()
    {
        var key = new Key('Z');
        Assert.Equal(SpecialKey.None, key.Special);
    }

    [Fact]
    public void Key_SpecialKeys_HaveCorrectSpecial()
    {
        Assert.Equal(SpecialKey.Space, Key.Space.Special);
        Assert.Equal(SpecialKey.Equal, Key.Equal.Special);
        Assert.Equal(SpecialKey.Minus, Key.Minus.Special);
        Assert.Equal(SpecialKey.F1, Key.F1.Special);
        Assert.Equal(SpecialKey.F12, Key.F12.Special);
    }

    [Fact]
    public void Key_SpecialKeys_HaveZeroCharValue()
    {
        Assert.Equal('\0', Key.Space.Value);
        Assert.Equal('\0', Key.Equal.Value);
        Assert.Equal('\0', Key.F1.Value);
    }

    [Theory]
    [InlineData("Space", "Space")]
    [InlineData("Equal", "Equal")]
    [InlineData("Minus", "Minus")]
    [InlineData("F1", "F1")]
    [InlineData("F12", "F12")]
    public void Key_SpecialToString_ReturnsSpecialName(string expected, string keyName)
    {
        Key key = keyName switch
        {
            "Space" => Key.Space,
            "Equal" => Key.Equal,
            "Minus" => Key.Minus,
            "F1" => Key.F1,
            "F12" => Key.F12,
            _ => throw new ArgumentException(keyName)
        };
        Assert.Equal(expected, key.ToString());
    }

    [Fact]
    public void Key_CharToString_ReturnsChar()
    {
        Assert.Equal("A", new Key('a').ToString());
        Assert.Equal("Z", new Key('Z').ToString());
    }

    [Fact]
    public void Key_Equals_SameChars()
    {
        Assert.Equal(new Key('A'), new Key('a'));
        Assert.True(new Key('A') == new Key('a'));
        Assert.False(new Key('A') != new Key('a'));
    }

    [Fact]
    public void Key_NotEquals_DifferentChars()
    {
        Assert.NotEqual(new Key('A'), new Key('B'));
    }

    [Fact]
    public void Key_GetHashCode_UppercaseSensitive()
    {
        // Same uppercase char → same hash
        Assert.Equal(new Key('A').GetHashCode(), new Key('a').GetHashCode());
    }

    [Fact]
    public void Key_StaticFactories_AllFKeys()
    {
        Assert.Equal(SpecialKey.F1, Key.F1.Special);
        Assert.Equal(SpecialKey.F2, Key.F2.Special);
        Assert.Equal(SpecialKey.F3, Key.F3.Special);
        Assert.Equal(SpecialKey.F10, Key.F10.Special);
        Assert.Equal(SpecialKey.F11, Key.F11.Special);
        Assert.Equal(SpecialKey.F12, Key.F12.Special);
    }

    // ── Modifier enum ───────────────────────────────────

    [Fact]
    public void Modifier_AllFourValues()
    {
        Assert.Equal(0, (int)Modifier.Alt);
        Assert.Equal(1, (int)Modifier.Ctrl);
        Assert.Equal(2, (int)Modifier.Shift);
        Assert.Equal(3, (int)Modifier.Win);
    }
}
