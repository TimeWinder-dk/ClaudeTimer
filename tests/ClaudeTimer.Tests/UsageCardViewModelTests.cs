using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class UsageCardViewModelTests
{
    [Fact]
    public void Tick_FormatsSubDayCountdownPrecisely()
    {
        var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var viewModel = new UsageCardViewModel("5 timer", "SESSION");
        viewModel.Update(42.5, now.AddHours(4).AddMinutes(3).AddSeconds(2));

        viewModel.Tick(now);

        Assert.Equal("04:03:02", viewModel.Countdown);
        Assert.Equal("42,5 %", viewModel.Percentage);
        Assert.Equal(42.5, viewModel.Utilization);
    }

    [Fact]
    public void Tick_FormatsMultiDayCountdown()
    {
        var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var viewModel = new UsageCardViewModel("7 døgn", "UGE");
        viewModel.Update(7, now.AddDays(5).AddHours(8).AddMinutes(9).AddSeconds(10));

        viewModel.Tick(now);

        Assert.Equal("5d 08:09:10", viewModel.Countdown);
    }

    [Fact]
    public void Tick_StopsAtZeroAfterReset()
    {
        var now = DateTimeOffset.UtcNow;
        var viewModel = new UsageCardViewModel("5 timer", "SESSION");
        viewModel.Update(100, now.AddSeconds(-1));

        viewModel.Tick(now);

        Assert.Equal("00:00:00", viewModel.Countdown);
    }
}
