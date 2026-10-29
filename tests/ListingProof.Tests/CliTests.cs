using System.Reflection;

namespace ListingProof.Tests;

// Calls the CLI's Main directly and captures what it prints. No other test writes to Console.
public class CliTests : IDisposable
{
    static readonly string Samples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));
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
    static int Run(params string[] args) => (int)Assembly.Load("ListingProof.Cli").EntryPoint!.Invoke(null, [args])!;

    [Fact]
    public void No_argument_exits_2()
    {
        Assert.Equal(2, Run());
        Assert.Contains("missing feed CSV path", errors.ToString());
    }

    [Fact]
    public void Missing_feed_file_exits_2()
    {
        Assert.Equal(2, Run(Path.Combine(Samples, "nope.csv")));
        Assert.Contains("feed file not found", errors.ToString());
    }

    [Fact]
    public void Missing_pages_folder_exits_2()
    {
        Assert.Equal(2, Run(Path.Combine(Samples, "feed.csv"), "--pages", Path.Combine(Samples, "nope")));
        Assert.Contains("pages folder not found", errors.ToString());
    }

    [Fact]
    public void Unreadable_feed_exits_2()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"listingproof-{Guid.NewGuid():N}.csv");
        File.Copy(Path.Combine(Samples, "feed.csv"), csv);
        try
        {
            // an exclusive lock, like Excel holds on an open CSV
            using (File.Open(csv, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(2, Run(csv, "--pages", Path.Combine(Samples, "pages")));
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
        Assert.Equal(1, Run(Path.Combine(Samples, "feed.csv")));

        var text = output.ToString();
        Assert.Contains("KETTLE-RED     mismatch  url           price: feed 34.50, page 39.00", text);
        Assert.EndsWith("8 rows: 4 match, 2 mismatch, 2 unknown" + Environment.NewLine, text);
        Assert.Empty(errors.ToString());
    }
}
