using MacroEngine.UI;
using Xunit;

namespace MacroEngine.Tests;

public sealed class TriggerPresentationTests
{
    [Theory]
    [InlineData("text", "name@example.com", "Вставить текст · name@example.com")]
    [InlineData("richtext", "<b>Готово</b>", "Вставить форматированный текст · <b>Готово</b>")]
    [InlineData("macro", "AcadSave", "Макрос · AcadSave")]
    [InlineData("open", @"D:\Projects", @"Открыть · D:\Projects")]
    [InlineData("launch", "notepad.exe", "Запустить · notepad.exe")]
    public void ActionSummary_ExplainsWhatRuleDoes(string action, string value, string expected)
    {
        var row = new TriggerRow { Action = action, Value = value };

        Assert.Equal(expected, row.ActionSummary);
    }

    [Fact]
    public void ActionSummary_FlattensAndTruncatesLongValues()
    {
        var row = new TriggerRow
        {
            Action = "script",
            Value = "first line\r\nsecond line   " + new string('x', 80)
        };

        Assert.StartsWith("Скрипт · first line second line ", row.ActionSummary);
        Assert.DoesNotContain('\n', row.ActionSummary);
        Assert.EndsWith("...", row.ActionSummary);
    }
}
