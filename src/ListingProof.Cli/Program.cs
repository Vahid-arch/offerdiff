using ListingProof.Core;

const string Usage = "usage: ListingProof.Cli <feed.csv> [--pages <folder>]";

string? csv = null, pages = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--pages" && i + 1 < args.Length) pages = args[++i];
    else if (csv == null && !args[i].StartsWith("--")) csv = args[i];
    else return Fail($"unexpected argument '{args[i]}'");
}

if (csv == null) return Fail("missing feed CSV path");
if (!File.Exists(csv)) return Fail($"feed file not found: {csv}");
pages ??= FeedCheck.DefaultPagesFolder(csv);
if (!Directory.Exists(pages)) return Fail($"pages folder not found: {pages}");

var report = FeedCheck.Run(csv, pages);

foreach (var error in report.FeedErrors)
    Console.WriteLine($"feed row {error.Row}: {error.Message}");
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
    Console.Error.WriteLine(Usage);
    return 2;
}
