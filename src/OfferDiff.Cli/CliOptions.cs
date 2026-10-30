using System.Globalization;

namespace OfferDiff.Cli;

// Command-line arguments, parsed without touching files or the network so every error case is easy to test.
public record CliOptions(string Csv, string? Pages, bool Live, bool AllowPrivateHosts, TimeSpan Delay)
{
    public const string Usage = """
        usage: offerdiff <feed.csv> [--pages <folder>]
               offerdiff <feed.csv> --live [--delay <seconds>] [--allow-private-hosts]

          --pages <folder>         fixture mode: folder with local pages (default: "pages" next to the CSV)
          --live                   fetch the real product pages from the feed's links
          --delay <seconds>        live mode: minimum gap between requests to one site (default 1, at least 0.5)
          --allow-private-hosts    live mode: allow local network addresses, for your own staging server
        """;

    // Returns the options, or an error message (the CLI prints it with the usage and exits 2).
    public static (CliOptions? Options, string? Error) Parse(string[] args)
    {
        string? csv = null, pages = null;
        bool live = false, allowPrivateHosts = false;
        double? delay = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live":
                    live = true;
                    break;
                case "--allow-private-hosts":
                    allowPrivateHosts = true;
                    break;
                case "--pages" or "--delay" when i + 1 == args.Length:
                    return (null, $"{args[i]} needs a value");
                case "--pages":
                    pages = args[++i];
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

        if (csv == null) return (null, "missing feed CSV path");
        if (allowPrivateHosts && !live) return (null, "--allow-private-hosts only works with --live");
        if (delay != null && !live) return (null, "--delay only works with --live");
        if (pages != null && live) return (null, "--pages is for fixture mode and cannot be used with --live");

        return (new CliOptions(csv, pages, live, allowPrivateHosts, TimeSpan.FromSeconds(delay ?? 1)), null);
    }
}
