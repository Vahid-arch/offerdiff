using System.Globalization;
using OfferDiff.Render;

namespace OfferDiff.Cli;

// Command-line arguments, parsed without touching files or the network so every error case is easy to test.
// Render implies Live: render mode fetches the real pages too, only through a browser.
public record CliOptions(string Csv, string? Pages, bool Live, bool AllowPrivateHosts, TimeSpan Delay,
    bool Render = false, string? BrowserChannel = null, bool InstallBrowser = false)
{
    public const string Usage = """
        usage: offerdiff <feed.csv> [--pages <folder>]
               offerdiff <feed.csv> --live [--delay <seconds>] [--allow-private-hosts]
               offerdiff <feed.csv> --render [--browser-channel <name>] [--delay <seconds>] [--allow-private-hosts]
               offerdiff --install-browser

          --pages <folder>          fixture mode: folder with local pages (default: "pages" next to the CSV)
          --live                    fetch the real product pages from the feed's links
          --render                  like --live, but open each page in headless Chromium so its JavaScript runs first
          --browser-channel <name>  render mode: use an installed browser instead (chrome, msedge, or their -beta, -dev, -canary)
          --delay <seconds>         live/render mode: minimum gap between requests to one site (default 1, at least 0.5)
          --allow-private-hosts     live/render mode: allow local network addresses, for your own staging server
          --install-browser         download the headless Chromium that --render uses (once)
        """;

    // Returns the options, or an error message (the CLI prints it with the usage and exits 2).
    public static (CliOptions? Options, string? Error) Parse(string[] args)
    {
        string? csv = null, pages = null, channel = null;
        bool live = false, render = false, allowPrivateHosts = false, installBrowser = false;
        double? delay = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live":
                    live = true;
                    break;
                case "--render":
                    render = true;
                    break;
                case "--install-browser":
                    installBrowser = true;
                    break;
                case "--allow-private-hosts":
                    allowPrivateHosts = true;
                    break;
                case "--pages" or "--delay" or "--browser-channel" when i + 1 == args.Length:
                    return (null, $"{args[i]} needs a value");
                case "--pages":
                    pages = args[++i];
                    break;
                case "--browser-channel":
                    channel = args[++i];
                    if (!BrowserPageSource.Channels.Contains(channel))
                        return (null, $"--browser-channel must be one of {string.Join(", ", BrowserPageSource.Channels)}, got '{channel}'");
                    break;
                case "--delay":
                    // written as !(in range) so NaN is rejected too; the upper bound keeps TimeSpan from overflowing
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                        || !(seconds >= 0.5 && seconds <= 3600))
                        return (null, $"--delay must be a number of seconds from 0.5 to 3600, got '{args[i]}'");
                    delay = seconds;
                    break;
                default:
                    if (csv != null || args[i].StartsWith("--")) return (null, $"unexpected argument '{args[i]}'");
                    csv = args[i];
                    break;
            }
        }

        if (installBrowser)
            return args.Length == 1
                ? (new CliOptions("", null, false, false, TimeSpan.FromSeconds(1), InstallBrowser: true), null)
                : (null, "--install-browser is used on its own, without a feed or other options");

        live |= render;
        if (csv == null) return (null, "missing feed CSV path");
        if (allowPrivateHosts && !live) return (null, "--allow-private-hosts only works with --live or --render");
        if (delay != null && !live) return (null, "--delay only works with --live or --render");
        if (channel != null && !render) return (null, "--browser-channel only works with --render");
        if (pages != null && live) return (null, "--pages is for fixture mode and cannot be used with --live or --render");

        return (new CliOptions(csv, pages, live, allowPrivateHosts, TimeSpan.FromSeconds(delay ?? 1), render, channel), null);
    }

    // One line telling the user exactly how to fix a browser that cannot start.
    public static string FixFor(BrowserMissingException missing) => missing.Part switch
    {
        MissingPart.Chromium => "Chromium is not installed. Fix: run \"offerdiff --install-browser\" once (or use --browser-channel chrome or msedge)",
        MissingPart.Driver => "this download has no browser support. Fix: get offerdiff-render-<version>-<platform>.zip from https://github.com/Vahid-arch/offerdiff/releases",
        _ => $"{missing.Message}. Fix: install that browser, or leave out --browser-channel to use OfferDiff's own Chromium",
    };
}
