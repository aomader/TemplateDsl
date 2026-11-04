// Only run, never published, so it needs no native AOT compiler, whose package NuGet's audit may flag.
#:property PublishAot=false

// Prepares a release: sets the version of the packages and updates everything that goes with it.
//
//   dotnet run tools/SetVersion.cs 0.1.0-preview.2
//
// - Directory.Build.props: Version becomes the new version.
// - CHANGELOG.md: "Unreleased" becomes the version and today's date, a new empty "Unreleased" goes above it, and the
//   links at the end compare and point to the new version.
// - For a stable version only, such as 0.1.0: the rules of AnalyzerReleases.Unshipped.md move to a "## Release 0.1.0"
//   section of AnalyzerReleases.Shipped.md. A preview leaves them unshipped, because those release headers only take
//   plain version numbers.
//
// It checks everything first and changes nothing if a check fails: the version is valid, higher than the current one,
// not tagged yet, and "Unreleased" lists what changed. Merging the result to main releases the version, see
// .github/workflows/release.yml.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

const string Repository = "https://github.com/aomader/TemplateDsl";

var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, ".."));
var propsPath = Path.Combine(root, "Directory.Build.props");
var changelogPath = Path.Combine(root, "CHANGELOG.md");
var shippedPath = Path.Combine(root, "src", "TemplateDsl.Generators", "AnalyzerReleases.Shipped.md");
var unshippedPath = Path.Combine(root, "src", "TemplateDsl.Generators", "AnalyzerReleases.Unshipped.md");

if (args is not [var version])
{
    return Fail("Usage: dotnet run tools/SetVersion.cs <version>, such as 0.1.0-preview.2 or 0.1.0");
}

var props = File.ReadAllText(propsPath);
var changelog = File.ReadAllText(changelogPath);
var current = Patterns.Version().Match(props) is { Success: true } match ? match.Groups[1].Value : null;

if (current is null)
{
    return Fail("Directory.Build.props has no <Version>.");
}

if (!Patterns.SemVer().IsMatch(current))
{
    return Fail($"The <Version> in Directory.Build.props, '{current}', isn't a version, such as 0.1.0-preview.2 or 0.1.0.");
}

if (!Patterns.SemVer().IsMatch(version))
{
    return Fail($"'{version}' isn't a version, such as 0.1.0-preview.2 or 0.1.0.");
}

if (version == "0.0.0")
{
    return Fail("0.0.0 is the version before the first release, which is never published.");
}

if (SemVer.Compare(version, current) <= 0)
{
    return Fail($"'{version}' isn't higher than the current version, {current}.");
}

if (IsTagged(version))
{
    return Fail($"'{version}' is tagged already, so it is released.");
}

var unreleased = Patterns.Unreleased().Match(changelog);

if (!unreleased.Success)
{
    return Fail("CHANGELOG.md has no '## [Unreleased]' section.");
}

if (string.IsNullOrWhiteSpace(unreleased.Groups["changes"].Value))
{
    return Fail("'## [Unreleased]' in CHANGELOG.md lists no changes. Add what this version changes first.");
}

var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
var isStable = !version.Contains('-', StringComparison.Ordinal);

File.WriteAllText(propsPath, Patterns.Version().Replace(props, $"<Version>{version}</Version>", 1));
File.WriteAllText(changelogPath, UpdateChangelog(changelog, version, date));
Console.WriteLine($"Directory.Build.props: {current} -> {version}");
Console.WriteLine($"CHANGELOG.md: '## [{version}] - {date}', with the changes of 'Unreleased', and its link");

if (isStable)
{
    Console.WriteLine(ShipRules(version)
        ? $"AnalyzerReleases: the unshipped rules moved to '## Release {version}' of AnalyzerReleases.Shipped.md"
        : "AnalyzerReleases: no unshipped rules to move");
}
else
{
    Console.WriteLine("AnalyzerReleases: unchanged, a preview leaves its rules unshipped");
}

Console.WriteLine($"\nCommit this in a pull request. Merging it to main releases {version}.");
return 0;

string UpdateChangelog(string text, string newVersion, string releaseDate)
{
    // The changes stay where they are and get the version as their heading, under a new, empty "Unreleased".
    text = Patterns.UnreleasedHeading().Replace(text, $"## [Unreleased]\n\n## [{newVersion}] - {releaseDate}", 1);

    var unreleasedLink = $"[Unreleased]: {Repository}/compare/{newVersion}...HEAD";
    var versionLink = $"[{newVersion}]: {Repository}/releases/tag/{newVersion}";

    return Patterns.UnreleasedLink().IsMatch(text)
        ? Patterns.UnreleasedLink().Replace(text, $"{unreleasedLink}\n{versionLink}", 1)
        : $"{text.TrimEnd()}\n\n{unreleasedLink}\n{versionLink}\n";
}

bool ShipRules(string newVersion)
{
    var lines = File.ReadAllLines(unshippedPath);

    // The comments at the top stay. The rest are the rules, under headings such as "### New Rules".
    var header = lines.TakeWhile(line => !line.StartsWith("###", StringComparison.Ordinal)).ToList();
    var rules = lines.Skip(header.Count).ToList();

    if (!rules.Any(line => Patterns.Rule().IsMatch(line)))
    {
        return false;
    }

    var shipped = File.ReadAllText(shippedPath).TrimEnd();
    File.WriteAllText(shippedPath, $"{shipped}\n\n## Release {newVersion}\n\n{string.Join('\n', rules).Trim()}\n");
    File.WriteAllText(unshippedPath, string.Join('\n', header).TrimEnd() + "\n");
    return true;
}

bool IsTagged(string tag)
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", root, "tag", "--list", tag]) { RedirectStandardOutput = true })!;
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return output.Trim() == tag;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        Console.WriteLine("Git isn't available, so it isn't checked whether the version is tagged already.");
        return false;
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Nothing was changed.");
    return 1;
}

static string ThisFile([CallerFilePath] string path = "") => path;

static partial class Patterns
{
    [GeneratedRegex(@"<Version>([^<]*)</Version>")]
    public static partial Regex Version();

    /// <summary>
    /// Semantic Versioning 2.0 without build metadata, which NuGet ignores. Numbers are ASCII digits without leading
    /// zeros, also in the pre-release, whose other identifiers have a letter or a dash. \z, as $ also matches before a
    /// final newline.
    /// </summary>
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?\z")]
    public static partial Regex SemVer();

    [GeneratedRegex(@"^## \[Unreleased\][^\n]*\n(?<changes>.*?)(?=^## \[|^\[Unreleased\]:|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    public static partial Regex Unreleased();

    [GeneratedRegex(@"^## \[Unreleased\][^\n]*", RegexOptions.Multiline)]
    public static partial Regex UnreleasedHeading();

    [GeneratedRegex(@"^\[Unreleased\]:[^\n]*", RegexOptions.Multiline)]
    public static partial Regex UnreleasedLink();

    [GeneratedRegex(@"^TDSL\d+ ")]
    public static partial Regex Rule();
}

/// <summary>The order of versions by Semantic Versioning: numbers first, and a pre-release before its release.</summary>
static class SemVer
{
    // Versions are checked against Patterns.SemVer first, so numbers are ASCII digits without leading zeros, and a
    // longer one is larger. Comparing them as text that way needs no parsing, so no number is too large.
    public static int Compare(string left, string right)
    {
        var (leftCore, leftPre) = Split(left);
        var (rightCore, rightPre) = Split(right);

        for (var i = 0; i < 3; i++)
        {
            var order = CompareNumbers(leftCore[i], rightCore[i]);
            if (order != 0)
            {
                return order;
            }
        }

        if (leftPre.Length == 0 || rightPre.Length == 0)
        {
            return rightPre.Length.CompareTo(leftPre.Length);
        }

        for (var i = 0; i < Math.Min(leftPre.Length, rightPre.Length); i++)
        {
            var order = CompareIdentifier(leftPre[i], rightPre[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return leftPre.Length.CompareTo(rightPre.Length);
    }

    private static (string[] Core, string[] PreRelease) Split(string version)
    {
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        return ((dash < 0 ? version : version[..dash]).Split('.'), dash < 0 ? [] : version[(dash + 1)..].Split('.'));
    }

    // Numbers compare as numbers and before words, which compare as text. A word can look like a negative number, such as -1.
    private static int CompareIdentifier(string left, string right) =>
        (IsNumber(left), IsNumber(right)) switch
        {
            (true, true) => CompareNumbers(left, right),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };

    private static bool IsNumber(string identifier) => identifier.All(char.IsAsciiDigit);

    private static int CompareNumbers(string left, string right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
}
