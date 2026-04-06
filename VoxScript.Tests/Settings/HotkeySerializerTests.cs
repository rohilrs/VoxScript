using FluentAssertions;
using VoxScript.Helpers;
using VoxScript.Native.Platform;
using Xunit;

namespace VoxScript.Tests.Settings;

public class HotkeySerializerTests
{
    [Theory]
    [InlineData("Ctrl+Win+Space", ModifierKeys.Ctrl | ModifierKeys.Win, 0x20)]
    [InlineData("Ctrl+Win", ModifierKeys.Ctrl | ModifierKeys.Win, null)]
    [InlineData("Alt+Shift+Z", ModifierKeys.Alt | ModifierKeys.Shift, (int)'Z')]
    [InlineData("Esc", ModifierKeys.None, 0x1B)]
    public void Parse_produces_correct_combo(string input, ModifierKeys expectedMods, int? expectedTrigger)
    {
        var combo = HotkeySerializer.Parse(input);
        combo.Should().NotBeNull();
        combo!.Modifiers.Should().Be(expectedMods);
        combo.TriggerKey.Should().Be(expectedTrigger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not set")]
    public void Parse_returns_null_for_empty_or_notset(string? input)
    {
        HotkeySerializer.Parse(input).Should().BeNull();
    }

    [Fact]
    public void Serialize_roundtrips_CtrlWinSpace()
    {
        var combo = new HotkeyCombo(ModifierKeys.Ctrl | ModifierKeys.Win, 0x20);
        var str = HotkeySerializer.Serialize(combo);
        str.Should().Be("Ctrl+Win+Space");
    }

    [Fact]
    public void Serialize_null_returns_NotSet()
    {
        HotkeySerializer.Serialize(null).Should().Be("Not set");
    }

    [Fact]
    public void Serialize_modifierOnly_combo()
    {
        var combo = new HotkeyCombo(ModifierKeys.Ctrl | ModifierKeys.Win, null);
        HotkeySerializer.Serialize(combo).Should().Be("Ctrl+Win");
    }

    [Theory]
    [InlineData(0xA2, true)]   // Left Ctrl
    [InlineData(0x5B, true)]   // Left Win
    [InlineData(0x20, false)]  // Space
    [InlineData(0x41, false)]  // A
    public void IsModifierKey_classifies_correctly(int vk, bool expected)
    {
        HotkeySerializer.IsModifierKey(vk).Should().Be(expected);
    }
}
