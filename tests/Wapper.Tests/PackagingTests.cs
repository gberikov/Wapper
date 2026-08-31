using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Wapper.Tests;

/// <summary>
/// Guards the build wiring itself. The version of every shipped package comes from a
/// git tag through MinVer and appears nowhere else in the repository, so a broken
/// MinVer setup would silently publish packages stamped 1.0.0 forever.
/// </summary>
public partial class PackagingTests
{
    [Theory]
    [InlineData("Wapper.Abstractions")]
    [InlineData("Wapper")]
    [InlineData("Wapper.AspNetCore")]
    public void Shipped_assembly_carries_a_version_derived_from_git(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        Assert.True(File.Exists(path), $"{assemblyName}.dll is missing from the test output.");

        var productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;

        Assert.False(string.IsNullOrWhiteSpace(productVersion));

        // Either a released version (1.2.3) or MinVer's untagged form
        // (0.0.0-alpha.0.7+<sha>). Both start with a SemVer core.
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), productVersion!);
    }

    [Fact]
    public void The_packaged_readme_links_nowhere_relative()
    {
        // The README is the package page on NuGet.org, rendered a long way from the
        // repository it was written in: a link to docs/configuration.md resolves to nothing
        // there, and 0.1.0 shipped with a table of them. On GitHub an absolute link works
        // just as well, so absolute is the only form that works in both places.
        var readme = Path.Combine(AppContext.BaseDirectory, "README.md");
        Assert.True(File.Exists(readme), "README.md is missing from the test output.");

        var relative = LinkTarget()
            .Matches(File.ReadAllText(readme))
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            // An anchor stays on the page it was rendered on, wherever that is.
            .Where(target => !target.StartsWith('#'))
            .ToList();

        Assert.True(
            relative.Count == 0,
            $"These README links are relative and will not resolve on the package page: " +
            string.Join(", ", relative));
    }

    /// <summary>The target of a markdown link or image.</summary>
    [GeneratedRegex(@"\]\((?<target>[^)\s]+)")]
    private static partial Regex LinkTarget();
}
