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
    [InlineData(new[] { "feed.csv", "--allow-private-hosts" }, "--allow-private-hosts only works with --live or --render")]
    [InlineData(new[] { "feed.csv", "--delay", "2" }, "--delay only works with --live or --render")]
    [InlineData(new[] { "feed.csv", "--live", "--pages", "pages" }, "--pages is for fixture mode and cannot be used with --live or --render")]
    [InlineData(new[] { "feed.csv", "--render", "--pages", "pages" }, "--pages is for fixture mode and cannot be used with --live or --render")]
    [InlineData(new[] { "feed.csv", "--browser-channel", "msedge" }, "--browser-channel only works with --render")]
    [InlineData(new[] { "feed.csv", "--live", "--browser-channel", "msedge" }, "--browser-channel only works with --render")]
    [InlineData(new[] { "feed.csv", "--render", "--browser-channel" }, "--browser-channel needs a value")]
    [InlineData(new[] { "feed.csv", "--render", "--browser-channel", "firefox" },
        "--browser-channel must be one of chrome, chrome-beta, chrome-dev, chrome-canary, msedge, msedge-beta, msedge-dev, msedge-canary, got 'firefox'")]
    [InlineData(new[] { "--install-browser", "feed.csv" }, "--install-browser is used on its own, without a feed or other options")]
    [InlineData(new[] { "--install-browser", "--render" }, "--install-browser is used on its own, without a feed or other options")]
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

    [Fact]
    public void Render_implies_live_and_keeps_live_options()
    {
        var options = CliOptions.Parse(["feed.csv", "--render", "--delay", "2", "--allow-private-hosts"]).Options;

        Assert.Equal(new CliOptions("feed.csv", null, true, true, TimeSpan.FromSeconds(2), Render: true), options);
    }

    [Fact]
    public void Live_and_render_together_are_render()
    {
        Assert.True(CliOptions.Parse(["feed.csv", "--live", "--render"]).Options!.Render);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("msedge-beta")]
    public void Browser_channel_is_accepted_with_render(string channel)
    {
        Assert.Equal(channel, CliOptions.Parse(["feed.csv", "--render", "--browser-channel", channel]).Options!.BrowserChannel);
    }

    [Fact]
    public void Install_browser_alone_is_accepted()
    {
        var options = CliOptions.Parse(["--install-browser"]).Options!;

        Assert.True(options.InstallBrowser);
        Assert.False(options.Live);
        Assert.False(options.Render);
    }

    [Theory]
    [InlineData(OfferDiff.Render.MissingPart.Chromium, "Chromium is not installed. Fix: run \"offerdiff --install-browser\" once (or use --browser-channel chrome or msedge)")]
    [InlineData(OfferDiff.Render.MissingPart.Driver, "this download has no browser support. Fix: get offerdiff-render-<version>-<platform>.zip from https://github.com/Vahid-arch/offerdiff/releases")]
    [InlineData(OfferDiff.Render.MissingPart.Channel, "Chromium distribution 'msedge' is not found. Fix: install that browser, or leave out --browser-channel to use OfferDiff's own Chromium")]
    public void Each_missing_browser_part_gets_a_one_line_fix(OfferDiff.Render.MissingPart part, string fix)
    {
        var missing = new OfferDiff.Render.BrowserMissingException(part, "Chromium distribution 'msedge' is not found");

        var line = CliOptions.FixFor(missing);

        Assert.Equal(fix, line);
        Assert.DoesNotContain('\n', line);
    }
}
