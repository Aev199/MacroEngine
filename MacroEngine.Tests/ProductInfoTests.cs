using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void EmbeddedReadme_IsAvailableInProductAssembly()
    {
        string readme = ProductInfo.ReadEmbeddedReadme();

        Assert.Contains("# MacroEngine", readme, StringComparison.Ordinal);
        Assert.Contains("Безопасность выполнения", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticSummary_ContainsEnvironmentButNoConfigContents()
    {
        string summary = ProductInfo.BuildDiagnosticSummary();

        Assert.Contains("MacroEngine v", summary, StringComparison.Ordinal);
        Assert.Contains("Data directory:", summary, StringComparison.Ordinal);
        Assert.Contains(AppPaths.RootDirectory, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("trigger value", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clipboard content", summary, StringComparison.OrdinalIgnoreCase);
    }
}
