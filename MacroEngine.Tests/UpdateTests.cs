using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class SemVersionTests
{
    [Theory]
    [InlineData("0.5.0", "0.5.0-beta.1")]
    [InlineData("0.5.0-beta.2", "0.5.0-beta.1")]
    [InlineData("0.5.0-beta.10", "0.5.0-beta.9")]
    [InlineData("0.5.0-rc.1", "0.5.0-beta.3")]
    [InlineData("0.5.0-beta.1.1", "0.5.0-beta.1")]
    [InlineData("0.6.0-beta.1", "0.5.9")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("v0.5.1", "0.5.0")]
    public void Precedence(string higher, string lower)
    {
        var h = SemVersion.TryParse(higher)!;
        var l = SemVersion.TryParse(lower)!;

        Assert.True(h > l);
        Assert.True(l < h);
    }

    [Fact]
    public void BuildMetadataIsIgnored() =>
        Assert.Equal(SemVersion.TryParse("0.5.0-beta.1+abc"), SemVersion.TryParse("0.5.0-beta.1"));

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("release")]
    public void MalformedIsRejected(string text) => Assert.Null(SemVersion.TryParse(text));
}

public sealed class UpdateServiceTests
{
    private static readonly SemVersion Current = SemVersion.TryParse("0.5.0-beta.1")!;

    private static string Release(string tag, bool prerelease = false, bool draft = false, bool assets = true) => $$"""
        {
          "tag_name": "{{tag}}", "name": "MacroEngine {{tag}}", "body": "notes {{tag}}",
          "html_url": "https://github.com/Aev199/MacroEngine/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-10-19T10:00:00Z",
          "assets": [{{(assets ? $$"""
            { "name": "MacroEngine-{{tag.TrimStart('v')}}-win-x64.zip", "size": 1234,
              "browser_download_url": "https://example.test/{{tag}}/pkg.zip" },
            { "name": "SHA256SUMS.txt", "size": 100,
              "browser_download_url": "https://example.test/{{tag}}/SHA256SUMS.txt" }
            """ : "")}}]
        }
        """;

    private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void PicksHighestNewerRelease()
    {
        string json = Feed(Release("v0.5.1"), Release("v0.6.0"), Release("v0.4.0"));

        ReleaseInfo? r = UpdateService.SelectRelease(json, Current, includePrerelease: false);

        Assert.NotNull(r);
        Assert.Equal("0.6.0", r!.Version.ToString());
        Assert.Equal("MacroEngine-0.6.0-win-x64.zip", r.PackageName);
        Assert.Equal(1234, r.PackageSize);
    }

    [Fact]
    public void PrereleasesOnlyWhenAllowed()
    {
        string json = Feed(Release("v0.5.0-beta.2", prerelease: true));

        Assert.Null(UpdateService.SelectRelease(json, Current, includePrerelease: false));
        Assert.Equal("0.5.0-beta.2", UpdateService.SelectRelease(json, Current, includePrerelease: true)!.Version.ToString());
    }

    [Fact]
    public void SkipsDraftsReleasesWithoutAssetsAndOlderOnes()
    {
        string json = Feed(
            Release("v0.9.0", draft: true),
            Release("v0.8.0", assets: false),
            Release("v0.5.0-beta.1"),
            Release("not-a-version"));

        Assert.Null(UpdateService.SelectRelease(json, Current, includePrerelease: true));
    }

    [Fact]
    public void FindsChecksumInSha256sumOutput()
    {
        string hash = new('a', 64);
        string sums = $"{new string('b', 64)}  other.zip\n{hash} *MacroEngine-0.6.0-win-x64.zip\r\n";

        Assert.Equal(hash, UpdateService.FindChecksum(sums, "MacroEngine-0.6.0-win-x64.zip"));
        Assert.Null(UpdateService.FindChecksum(sums, "missing.zip"));
        Assert.Null(UpdateService.FindChecksum("short  MacroEngine.zip", "MacroEngine.zip"));
    }

    [Fact]
    public void InstallReplacesFilesAndKeepsUserConfig()
    {
        using var dirs = new TempDirs();
        dirs.App("MacroEngine.exe", "old exe");
        dirs.App("icon.png", "old icon");
        dirs.App(Path.Combine("config", "triggers.json"), "user triggers");
        dirs.Pkg("MacroEngine.exe", "new exe");
        dirs.Pkg("icon.png", "new icon");
        dirs.Pkg(Path.Combine("config", "triggers.json"), "template triggers");
        dirs.Pkg(Path.Combine("config", "macros.json"), "template macros");

        UpdateService.InstallFiles(dirs.Package, dirs.Application);

        Assert.Equal("new exe", dirs.ReadApp("MacroEngine.exe"));
        Assert.Equal("new icon", dirs.ReadApp("icon.png"));
        Assert.Equal("user triggers", dirs.ReadApp(Path.Combine("config", "triggers.json")));
        Assert.Equal("template macros", dirs.ReadApp(Path.Combine("config", "macros.json")));
        Assert.Equal("old exe", dirs.ReadApp("MacroEngine.exe.old"));

        UpdateService.CleanupLeftovers(dirs.Application);
        Assert.False(File.Exists(Path.Combine(dirs.Application, "MacroEngine.exe.old")));
        Assert.True(File.Exists(Path.Combine(dirs.Application, "MacroEngine.exe")));
    }

    [Fact]
    public void FailedInstallRestoresOriginalFiles()
    {
        using var dirs = new TempDirs();
        dirs.App("MacroEngine.exe", "old exe");
        dirs.App("icon.png", "old icon");
        // A directory where the package has a file makes the copy fail mid-way.
        Directory.CreateDirectory(Path.Combine(dirs.Application, "blocker.dll"));
        dirs.Pkg("MacroEngine.exe", "new exe");
        dirs.Pkg("icon.png", "new icon");
        dirs.Pkg("blocker.dll", "new dll");

        Assert.Throws<UpdateException>(() => UpdateService.InstallFiles(dirs.Package, dirs.Application));

        Assert.Equal("old exe", dirs.ReadApp("MacroEngine.exe"));
        Assert.Equal("old icon", dirs.ReadApp("icon.png"));
        Assert.Empty(Directory.GetFiles(dirs.Application, "*.old*", SearchOption.AllDirectories));
    }

    [Fact]
    public void PackageWithoutExecutableIsRejected()
    {
        using var dirs = new TempDirs();
        dirs.App("MacroEngine.exe", "old exe");
        dirs.Pkg("readme.txt", "no exe here");

        Assert.Throws<UpdateException>(() => UpdateService.InstallFiles(dirs.Package, dirs.Application));
        Assert.Equal("old exe", dirs.ReadApp("MacroEngine.exe"));
    }

    private sealed class TempDirs : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "me-update-" + Guid.NewGuid().ToString("N"));
        public string Application => Path.Combine(_root, "app");
        public string Package => Path.Combine(_root, "pkg");

        public TempDirs()
        {
            Directory.CreateDirectory(Application);
            Directory.CreateDirectory(Package);
        }

        public void App(string relative, string content) => Write(Application, relative, content);
        public void Pkg(string relative, string content) => Write(Package, relative, content);
        public string ReadApp(string relative) => File.ReadAllText(Path.Combine(Application, relative));

        private static void Write(string root, string relative, string content)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
