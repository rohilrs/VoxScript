// VoxScript.Tests/PowerMode/PowerModeManagerTests.cs
using FluentAssertions;
using VoxScript.Core.PowerMode;
using Xunit;

namespace VoxScript.Tests.PowerMode;

public class PowerModeManagerTests
{
    [Fact]
    public void Resolve_returns_null_when_no_configs()
    {
        var mgr = new PowerModeManager();
        mgr.Resolve("chrome", "GitHub", "https://github.com").Should().BeNull();
    }

    [Fact]
    public void Resolve_matches_by_process_name()
    {
        var mgr = new PowerModeManager();
        mgr.Add(new PowerModeConfig { Id = 1, Name = "VS Code", ProcessNameFilter = "code", Priority = 1 });
        var result = mgr.Resolve("Code", null, null);
        result!.Name.Should().Be("VS Code");
    }

    [Fact]
    public void Resolve_matches_url_by_regex()
    {
        var mgr = new PowerModeManager();
        mgr.Add(new PowerModeConfig
        {
            Id = 1, Name = "GitHub", ProcessNameFilter = "chrome",
            UrlPatternFilter = @"github\.com", Priority = 1
        });
        var result = mgr.Resolve("chrome", null, "https://github.com/org/repo");
        result!.Name.Should().Be("GitHub");
    }

    [Fact]
    public void Resolve_returns_higher_priority_when_multiple_match()
    {
        var mgr = new PowerModeManager();
        mgr.Add(new PowerModeConfig { Id = 1, Name = "Low",  ProcessNameFilter = "chrome", Priority = 1 });
        mgr.Add(new PowerModeConfig { Id = 2, Name = "High", ProcessNameFilter = "chrome", Priority = 10 });
        mgr.Resolve("chrome", null, null)!.Name.Should().Be("High");
    }

    [Fact]
    public void Resolve_skips_disabled_configs()
    {
        var mgr = new PowerModeManager();
        mgr.Add(new PowerModeConfig { Id = 1, Name = "Disabled",
            ProcessNameFilter = "chrome", IsEnabled = false, Priority = 10 });
        mgr.Add(new PowerModeConfig { Id = 2, Name = "Enabled",
            ProcessNameFilter = "chrome", IsEnabled = true, Priority = 1 });
        mgr.Resolve("chrome", null, null)!.Name.Should().Be("Enabled");
    }
}
