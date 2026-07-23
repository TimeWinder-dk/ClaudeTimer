using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class MainViewModelTests
{
    [Theory]
    [InlineData(" token ", "token")]
    [InlineData("Bearer token", "token")]
    [InlineData("bearer    token-with-space-padding ", "token-with-space-padding")]
    public void NormalizeToken_RemovesWhitespaceAndOptionalScheme(
        string input,
        string expected)
    {
        Assert.Equal(expected, MainViewModel.NormalizeToken(input));
    }
}
