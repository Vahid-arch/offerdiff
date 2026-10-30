using OfferDiff.Cli;

namespace OfferDiff.Tests;

// Argument parsing only: no files, no network.
public class CliOptionsTests
{
    [Theory]
    [InlineData(new string[0], "missing feed CSV path")]
    [InlineData(new[] { "feed.csv", "--pages" }, "--pages needs a value")]
    [InlineData(new[] { "feed.csv", "--live", "--delay" }, "--delay needs a value")]
    [InlineData(new[] { "feed.csv", "--live", "--delay", "abc" }, "--delay must be a number of seconds from 0.5 to 3600, got 'abc'")]
    [InlineData(new[] { "feed.csv", "--live", "--delay", "0.4" }, "--delay must be a number of seconds from 0.5 to 3600, got '0.4'")]
    [InlineData(new[] { "feed.csv", "--live", "--delay", "-1" }, "--delay must be a number of seconds from 0.5 to 3600, got '-1'")]
    [InlineData(new[] { "feed.csv", "--live", "--delay", "NaN" }, "--delay must be a number of seconds from 0.5 to 3600, got 'NaN'")]
    [InlineData(new[] { "feed.csv", "--live", "--delay", "1,5" }, "--delay must be a number of seconds from 0.5 to 3600, got '1,5'")]
    [InlineData(new[] { "feed.csv", "--allow-private-hosts" }, "--allow-private-hosts only works with --live")]
    [InlineData(new[] { "feed.csv", "--delay", "2" }, "--delay only works with --live")]
    [InlineData(new[] { "feed.csv", "--live", "--pages", "pages" }, "--pages is for fixture mode and cannot be used with --live")]
    [InlineData(new[] { "feed.csv", "--nope" }, "unexpected argument '--nope'")]
    [InlineData(new[] { "feed.csv", "other.csv" }, "unexpected argument 'other.csv'")]
    public void Bad_arguments_give_a_clear_error(string[] args, string error)
    {
        var (options, message) = CliOptions.Parse(args);

        Assert.Null(options);
        Assert.Equal(error, message);
    }

    [Fact]
    public void Fixture_mode_is_the_default()
    {
        Assert.Equal(new CliOptions("feed.csv", null, false, false, TimeSpan.FromSeconds(1)), CliOptions.Parse(["feed.csv"]).Options);
    }

    [Fact]
    public void Pages_option_in_fixture_mode()
    {
        Assert.Equal("my-pages", CliOptions.Parse(["feed.csv", "--pages", "my-pages"]).Options!.Pages);
    }

    [Fact]
    public void Live_is_accepted_with_default_delay()
    {
        Assert.Equal(new CliOptions("feed.csv", null, true, false, TimeSpan.FromSeconds(1)), CliOptions.Parse(["feed.csv", "--live"]).Options);
    }

    [Fact]
    public void Live_options_in_any_order()
    {
        var options = CliOptions.Parse(["--delay", "2.5", "--allow-private-hosts", "feed.csv", "--live"]).Options;

        Assert.Equal(new CliOptions("feed.csv", null, true, true, TimeSpan.FromSeconds(2.5)), options);
    }

    [Fact]
    public void Minimum_delay_is_accepted()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.5), CliOptions.Parse(["feed.csv", "--live", "--delay", "0.5"]).Options!.Delay);
    }
}
