using MacroEngine.Modules;
using Xunit;

namespace MacroEngine.Tests;

public sealed class ParsingTests
{
    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F24", 0x87)]
    [InlineData("Enter", 0x0D)]
    [InlineData("A", 0x41)]
    [InlineData("9", 0x39)]
    public void MacroRunner_NameToVk_MapsSupportedKeys(string name, int expected)
    {
        Assert.Equal((ushort)expected, MacroRunner.NameToVk(name));
    }

    [Theory]
    [InlineData("notepad.exe", "notepad.exe", "")]
    [InlineData("python script.py --fast", "python", "script.py --fast")]
    [InlineData("\"C:\\Program Files\\Tool\\tool.exe\" --quiet", "C:\\Program Files\\Tool\\tool.exe", "--quiet")]
    public void ScriptRunner_SplitCommand_HandlesQuotedPaths(
        string command,
        string expectedFile,
        string expectedArguments)
    {
        ScriptRunner.SplitCommand(command, out string file, out string arguments);

        Assert.Equal(expectedFile, file);
        Assert.Equal(expectedArguments, arguments);
    }
}
