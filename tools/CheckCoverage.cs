// Only run, never published, so it needs no native AOT compiler, whose package NuGet's audit may flag.
#:property PublishAot=false

// Checks the code coverage of the packages and lists every line that no test runs.
//
//   dotnet test TemplateDsl.slnx --settings coverage.runsettings --results-directory TestResults/coverage
//   dotnet run tools/CheckCoverage.cs TestResults/coverage
//   dotnet run tools/CheckCoverage.cs TestResults/coverage --branches    also lists the branches not taken
//
// Reads every Cobertura file in the directory, merges them, and fails unless every line is covered. Branch coverage
// is reported as well. A line counts as covered if any test runs it, also through a partial class, a lambda or a
// state machine, which the report lists as classes of their own.

using System.Globalization;
using System.Xml.Linq;

var directory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "TestResults/coverage";
var listBranches = args.Contains("--branches");
var reports = Directory.Exists(directory)
    ? Directory.GetFiles(directory, "*.cobertura.xml", SearchOption.AllDirectories)
    : [];

if (reports.Length == 0)
{
    Console.Error.WriteLine($"No *.cobertura.xml in {directory}. Run the tests with --settings coverage.runsettings first.");
    return 2;
}

// Per assembly, file and line: whether it ran, and the branches it has and that ran.
var lines = new Dictionary<(string Assembly, string File, int Line), (bool Hit, int Branches, int BranchesHit)>();

foreach (var report in reports)
{
    foreach (var package in XDocument.Load(report).Descendants("package"))
    {
        var assembly = (string)package.Attribute("name")!;

        foreach (var @class in package.Descendants("class"))
        {
            var file = (string)@class.Attribute("filename")!;

            foreach (var line in @class.Element("lines")?.Elements("line") ?? [])
            {
                var key = (assembly, file, (int)line.Attribute("number")!);
                var (branches, branchesHit) = ParseConditions((string?)line.Attribute("condition-coverage"));
                var previous = lines.GetValueOrDefault(key);

                lines[key] = (
                    previous.Hit || (int)line.Attribute("hits")! > 0,
                    Math.Max(previous.Branches, branches),
                    Math.Max(previous.BranchesHit, branchesHit));
            }
        }
    }
}

Console.WriteLine($"{"Assembly",-26} {"Lines",16} {"Branches",16}");

foreach (var assembly in lines.GroupBy(entry => entry.Key.Assembly).OrderBy(group => group.Key))
{
    var total = assembly.Count();
    var hit = assembly.Count(entry => entry.Value.Hit);
    var branches = assembly.Sum(entry => entry.Value.Branches);
    var branchesHit = assembly.Sum(entry => entry.Value.BranchesHit);

    Console.WriteLine($"{assembly.Key,-26} {Percent(hit, total),7} {$"{hit}/{total}",8} {Percent(branchesHit, branches),7} {$"{branchesHit}/{branches}",8}");
}

if (listBranches)
{
    var partial = lines.Where(entry => entry.Value.BranchesHit < entry.Value.Branches).OrderBy(entry => entry.Key.File).ThenBy(entry => entry.Key.Line).ToList();

    Console.WriteLine($"\n{partial.Count} lines have branches that no test takes:");

    foreach (var entry in partial)
    {
        Console.WriteLine($"  {Location(entry.Key.File, entry.Key.Line)} ({entry.Value.BranchesHit}/{entry.Value.Branches})  {Source(entry.Key.File, entry.Key.Line)}");
    }
}

var missed = lines.Where(entry => !entry.Value.Hit).OrderBy(entry => entry.Key.File).ThenBy(entry => entry.Key.Line).ToList();

if (missed.Count == 0)
{
    Console.WriteLine("\nEvery line is covered.");
    return 0;
}

Console.WriteLine($"\n{missed.Count} lines are not covered:");

foreach (var entry in missed)
{
    Console.WriteLine($"  {Location(entry.Key.File, entry.Key.Line)}  {Source(entry.Key.File, entry.Key.Line)}");
}

return 1;

static (int Branches, int Hit) ParseConditions(string? coverage)
{
    // As in "50% (1/2)".
    if (coverage?.IndexOf('(') is not { } open || open < 0)
    {
        return (0, 0);
    }

    var parts = coverage[(open + 1)..].TrimEnd(')').Split('/');
    return (int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[0], CultureInfo.InvariantCulture));
}

static string Location(string file, int line) => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}:{line}";

static string? Source(string file, int line) =>
    File.Exists(file) ? File.ReadLines(file).ElementAtOrDefault(line - 1)?.Trim() : null;

static string Percent(int part, int total) => total == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * part / total:0.0}%");
