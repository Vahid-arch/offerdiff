using OfferDiff.Cli;
using OfferDiff.Core;

var (options, error) = CliOptions.Parse(args);
if (options == null) return Fail(error!);
if (!File.Exists(options.Csv)) return Fail($"feed file not found: {options.Csv}");

Func<string, string> getHtml;
Action<int, int, string>? progress = null;
if (options.Live)
{
    getHtml = HttpPageSource.Create(options.AllowPrivateHosts, options.Delay).GetHtml;
    // progress goes to stderr, so stdout stays a clean table for scripts
    progress = (n, total, id) => Console.Error.WriteLine($"checking {n}/{total}: {id}");
}
else
{
    var pages = options.Pages ?? FeedCheck.DefaultPagesFolder(options.Csv);
    if (!Directory.Exists(pages)) return Fail($"pages folder not found: {pages}");
    getHtml = new FixturePageSource(pages).GetHtml;
}

CheckReport report;
try
{
    report = FeedCheck.Run(options.Csv, getHtml, progress);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // e.g. the CSV is open in Excel, which locks it
    return Fail($"cannot read feed file: {ex.Message}");
}

foreach (var feedError in report.FeedErrors)
    Console.WriteLine($"feed row {feedError.Row}: {feedError.Message}");
if (report.FeedErrors.Count > 0) Console.WriteLine();

var idWidth = Math.Max("ID".Length, report.Results.Select(r => r.Id.Length).DefaultIfEmpty(0).Max());
string Row(string id, string verdict, string matchedBy, string detail) =>
    $"{id.PadRight(idWidth)}  {verdict,-8}  {matchedBy,-12}  {detail}";

Console.WriteLine(Row("ID", "VERDICT", "MATCHED BY", "DETAILS"));
foreach (var r in report.Results)
{
    // first detail on the id line, the rest indented underneath
    Console.WriteLine(Row(r.Id, r.Verdict.ToString().ToLowerInvariant(), r.MatchedBy ?? "-", r.Details[0]));
    foreach (var detail in r.Details.Skip(1))
        Console.WriteLine(Row("", "", "", detail));
}

Console.WriteLine();
Console.WriteLine(report.Summary);
return report.ExitCode;

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}
