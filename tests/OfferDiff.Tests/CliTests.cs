using System.Reflection;

namespace OfferDiff.Tests;

// Calls the CLI's Main directly and captures what it prints. No other test writes to Console.
public class CliTests : IDisposable
{
    readonly StringWriter output = new(), errors = new();
    readonly TextWriter originalOut = Console.Out, originalError = Console.Error;

    public CliTests()
    {
        Console.SetOut(output);
        Console.SetError(errors);
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
    }

    // Top-level statements compile to an internal Program class, so reach Main through the assembly's entry point.
    static int Run(params string[] args) => (int)Assembly.Load("OfferDiff.Cli").EntryPoint!.Invoke(null, [args])!;

    [Fact]
    public void No_argument_exits_2()
    {
        Assert.Equal(2, Run());
        Assert.Contains("missing feed CSV path", errors.ToString());
    }

    [Fact]
    public void Missing_feed_file_exits_2()
    {
        Assert.Equal(2, Run(Path.Combine(Samples.Folder, "nope.csv")));
        Assert.Contains("feed file not found", errors.ToString());
    }

    [Fact]
    public void Missing_pages_folder_exits_2()
    {
        Assert.Equal(2, Run(Samples.Feed, "--pages", Path.Combine(Samples.Folder, "nope")));
        Assert.Contains("pages folder not found", errors.ToString());
    }

    [Fact]
    public void Unreadable_feed_exits_2()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"offerdiff-{Guid.NewGuid():N}.csv");
        File.Copy(Samples.Feed, csv);
        try
        {
            // an exclusive lock, like Excel holds on an open CSV
            using (File.Open(csv, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(2, Run(csv, "--pages", Samples.Pages));
            Assert.Contains("cannot read feed file", errors.ToString());
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Sample_feed_prints_table_and_exits_1()
    {
        Assert.Equal(1, Run(Samples.Feed));

        var text = output.ToString();
        Assert.Contains("KETTLE-RED     mismatch  url           price: feed 34.50, page 39.00", text);
        Assert.EndsWith("8 rows: 4 match, 2 mismatch, 2 unknown" + Environment.NewLine, text);
        Assert.Empty(errors.ToString()); // fixture mode prints no progress
    }

    [Fact]
    public void Bad_option_exits_2_with_message_and_usage()
    {
        Assert.Equal(2, Run(Samples.Feed, "--delay", "2"));

        var text = errors.ToString();
        Assert.Contains("error: --delay only works with --live", text);
        Assert.Contains("--allow-private-hosts", text); // full usage listing all options
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Live_run_uses_http_with_progress_on_stderr()
    {
        // 127.0.0.1 is stopped by the private-address guard before any connection, so this never touches the network
        var csv = Path.Combine(Path.GetTempPath(), $"offerdiff-{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv, "id,title,link,price,sale_price,availability\nA1,Mug,http://127.0.0.1:9/products/a,12.99 USD,,in_stock\n");
        try
        {
            Assert.Equal(0, Run(csv, "--live"));

            Assert.Equal("checking 1/1: A1" + Environment.NewLine, errors.ToString());
            var table = output.ToString();
            Assert.Contains("A1  unknown   -             page could not be loaded: blocked private address 127.0.0.1", table);
            Assert.DoesNotContain("checking", table);
        }
        finally
        {
            File.Delete(csv);
        }
    }
}
