using ClaudeTimer.Models;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class PreferencesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00+02:00");
    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);

    [Fact]
    public void Pace_ProjectsUsageAtReset_WhenOnTrack()
    {
        // Halvvejs gennem vinduet med 20 % brugt → 40 % ved nulstilling.
        var (text, warning) = UsageCardViewModel.Pace(20, Now + TimeSpan.FromMinutes(150), FiveHours, Now);

        Assert.Equal("Prognose ved nulstilling: 40 %", text);
        Assert.False(warning);
    }

    [Fact]
    public void Pace_WarnsWithTime_WhenLimitIsHitBeforeReset()
    {
        // 1 time inde med 50 % brugt → 100 % efter yderligere 1 time.
        var (text, warning) = UsageCardViewModel.Pace(50, Now + TimeSpan.FromHours(4), FiveHours, Now);

        Assert.Equal($"Med dette tempo: 100 % kl. {Now.AddHours(1).ToLocalTime():HH:mm}", text);
        Assert.True(warning);
    }

    [Fact]
    public void Pace_IsSilent_EarlyInWindow_AndWithoutLength()
    {
        Assert.Equal(string.Empty, UsageCardViewModel.Pace(30, Now + TimeSpan.FromMinutes(295), FiveHours, Now).Text);
        Assert.Equal(string.Empty, UsageCardViewModel.Pace(30, Now + TimeSpan.FromHours(2), null, Now).Text);
        Assert.Equal("Grænsen er nået", UsageCardViewModel.Pace(100, Now + TimeSpan.FromHours(2), FiveHours, Now).Text);
    }

    [Fact]
    public void DescribeChange_ReportsHighestCrossedThreshold()
    {
        var options = new NotificationOptions(true, [80, 95], true);
        var reset = Now + TimeSpan.FromHours(2);

        Assert.Equal("5 timer har nået 95 %.",
            AccountViewModel.DescribeChange("5 timer", 70, reset, Window(96, reset), options, Now));
        Assert.Null(AccountViewModel.DescribeChange("5 timer", 81, reset, Window(90, reset), options, Now));
    }

    [Fact]
    public void DescribeChange_ReportsReset_AndRespectsSettings()
    {
        var options = new NotificationOptions(true, [80], true);
        var oldReset = Now - TimeSpan.FromSeconds(5);

        Assert.Equal("5 timer er nulstillet – fuld kvote igen.",
            AccountViewModel.DescribeChange("5 timer", 60, oldReset, Window(0, Now + FiveHours), options, Now));
        Assert.Null(AccountViewModel.DescribeChange("5 timer", 60, oldReset, Window(0, Now + FiveHours), options with { OnReset = false }, Now));
        Assert.Null(AccountViewModel.DescribeChange("5 timer", 70, Now, Window(90, Now), NotificationOptions.None, Now));
    }

    [Theory]
    [InlineData("80, 95", new[] { 80, 95 })]
    [InlineData("95;80 80%", new[] { 80, 95 })]
    [InlineData("", new int[0])]
    public void ParseThresholds_AcceptsCommonFormats(string text, int[] expected) =>
        Assert.Equal(expected, MainViewModel.ParseThresholds(text));

    [Theory]
    [InlineData("80, abc")]
    [InlineData("0")]
    [InlineData("101")]
    public void ParseThresholds_RejectsInvalid(string text) =>
        Assert.Null(MainViewModel.ParseThresholds(text));

    [Fact]
    public void AccountNames_UseAlias_AndKeepEmailInSubtitle()
    {
        var account = new AccountViewModel("a|o", string.Empty)
        {
            Profile = new ClaudeAccount("a", "thha@timewinder.dk", null, "o", "Timewinder"),
            Credentials = [new AccountCredential(TokenSource.ClaudeCode, "token")],
            Alias = "Arbejde"
        };

        MainViewModel.ApplyAccountNames([account]);

        Assert.Equal("Arbejde", account.DisplayName);
        Assert.Equal("Arbejde", account.ShortName);
        Assert.Equal("thha@timewinder.dk · Timewinder · Claude Code", account.Subtitle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(100)]
    public void TrayIcon_RendersAllLevels(int percent)
    {
        using var icon = TrayIconRenderer.Render(percent, lightTaskbar: false);
        Assert.Equal(32, icon.Width);
    }

    [Theory]
    [InlineData(10, UsageLevel.Good)]
    [InlineData(50, UsageLevel.Warning)]
    [InlineData(80, UsageLevel.Critical)]
    public void UsageLevels_UseSharedThresholds(double utilization, UsageLevel expected) =>
        Assert.Equal(expected, UsageLevels.For(utilization));

    private static UsageWindow Window(double utilization, DateTimeOffset resetsAt) =>
        new("session", "session", utilization, resetsAt, null);
}
