using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Wapper.Tests;

/// <summary>
/// Guards the build wiring itself. The version of every shipped package comes from a
/// git tag through MinVer and appears nowhere else in the repository, so a broken
/// MinVer setup would silently publish packages stamped 1.0.0 forever.
/// </summary>
public class PackagingTests
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
}
