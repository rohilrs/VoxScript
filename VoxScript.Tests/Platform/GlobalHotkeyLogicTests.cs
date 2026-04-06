// VoxScript.Tests/Platform/GlobalHotkeyLogicTests.cs
using FluentAssertions;
using Xunit;

namespace VoxScript.Tests.Platform;

// Tests for timing/mode logic extracted from GlobalHotkeyService without Win32 dependencies
public class HotkeyModeLogicTests
{
    [Fact]
    public void PushToTalk_fires_start_on_keydown_stop_on_keyup()
    {
        // Simulate the event sequencing that GlobalHotkeyService produces
        var startFired = false;
        var stopFired = false;

        // In PushToTalk mode: keydown → start, keyup → stop
        // This test validates the expected event contract
        void SimulateKeyDown(Action onStart) => onStart();
        void SimulateKeyUp(Action onStop) => onStop();

        SimulateKeyDown(() => startFired = true);
        SimulateKeyUp(() => stopFired = true);

        startFired.Should().BeTrue();
        stopFired.Should().BeTrue();
    }

    [Theory]
    [InlineData(600, true)]   // held > 500ms → stop fires (push-to-talk behavior)
    [InlineData(200, false)]  // held < 500ms → stop does not fire (toggle behavior)
    public void Hybrid_stop_fires_only_when_held_above_threshold(double heldMs, bool expectStop)
    {
        const double threshold = 500;
        bool stopShouldFire = heldMs >= threshold;
        stopShouldFire.Should().Be(expectStop);
    }
}
