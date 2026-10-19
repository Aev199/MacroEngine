namespace MacroEngine.Core;

internal static class CommandLineParser
{
    public static void Split(string command, out string fileName, out string arguments)
    {
        command = command.Trim();
        if (command.Length == 0)
            throw new ArgumentException("Команда запуска пуста.", nameof(command));

        if (command.StartsWith('"'))
        {
            int endQuote = command.IndexOf('"', 1);
            if (endQuote <= 1)
                throw new FormatException("Не найдена закрывающая кавычка в пути запуска.");

            fileName = command[1..endQuote];
            arguments = command[(endQuote + 1)..].Trim();
            return;
        }

        int spaceIndex = command.IndexOf(' ');
        if (spaceIndex > 0)
        {
            fileName = command[..spaceIndex];
            arguments = command[(spaceIndex + 1)..].Trim();
        }
        else
        {
            fileName = command;
            arguments = string.Empty;
        }
    }
}
