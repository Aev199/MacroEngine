using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;

namespace MacroEngine.Core;

/// <summary>
/// Eager snapshot of the clipboard. Rich-text expansion temporarily replaces the
/// clipboard, so all available formats must be restored rather than text alone.
/// </summary>
internal sealed class ClipboardSnapshot
{
    private sealed class UnsafeClipboardFormatException : Exception
    {
        public UnsafeClipboardFormatException(string message) : base(message) { }
    }

    private readonly List<(string Format, object Data)> _items;
    private readonly bool _wasEmpty;
    private readonly uint _sequenceNumber;

    private ClipboardSnapshot(
        List<(string Format, object Data)> items,
        bool wasEmpty,
        uint sequenceNumber)
    {
        _items = items;
        _wasEmpty = wasEmpty;
        _sequenceNumber = sequenceNumber;
    }

    public bool IsCurrent => NativeMethods.GetClipboardSequenceNumber() == _sequenceNumber;

    public static ClipboardSnapshot Capture()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                uint sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
                IDataObject? source = Clipboard.GetDataObject();
                if (source == null)
                {
                    uint emptySequence = NativeMethods.GetClipboardSequenceNumber();
                    if (emptySequence != sequenceBefore)
                        throw new IOException("Clipboard changed while it was being captured.");
                    return new ClipboardSnapshot(new(), wasEmpty: true, emptySequence);
                }

                var items = new List<(string Format, object Data)>();
                foreach (string format in source.GetFormats(autoConvert: false).Distinct(StringComparer.Ordinal))
                {
                    object? data = source.GetData(format, autoConvert: false);
                    if (data == null)
                    {
                        throw new UnsafeClipboardFormatException(
                            $"Формат «{format}» объявлен, но его данные недоступны.");
                    }

                    items.Add((format, CloneKnownData(data, format)));
                }

                uint sequenceAfter = NativeMethods.GetClipboardSequenceNumber();
                if (sequenceAfter != sequenceBefore)
                    throw new IOException("Clipboard changed while it was being captured.");

                return new ClipboardSnapshot(
                    items,
                    wasEmpty: items.Count == 0,
                    sequenceAfter);
            }
            catch (UnsafeClipboardFormatException ex)
            {
                throw new InvalidOperationException(
                    "Буфер обмена содержит формат, который нельзя безопасно сохранить. Операция отменена.",
                    ex);
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(20);
            }
        }

        throw new InvalidOperationException(
            "Не удалось безопасно сохранить текущее содержимое буфера обмена.",
            lastError);
    }

    public void Restore()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (_wasEmpty)
                {
                    Clipboard.Clear();
                    return;
                }

                var restored = new DataObject();
                foreach (var (format, data) in _items)
                    restored.SetData(format, autoConvert: false, CloneKnownData(data, format));

                Clipboard.SetDataObject(restored, copy: true, retryTimes: 10, retryDelay: 50);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(20);
            }
        }

        throw new InvalidOperationException(
            "Не удалось восстановить исходное содержимое буфера обмена.",
            lastError);
    }

    private static object CloneKnownData(object data, string format)
    {
        object? clone = data switch
        {
            string text => text,
            byte[] bytes => bytes.ToArray(),
            string[] strings => strings.ToArray(),
            Stream stream => CloneStream(stream),
            StringCollection collection => CloneCollection(collection),
            Image image => image.Clone(),
            _ when data.GetType().IsValueType => data,
            _ => null
        };

        if (clone == null)
        {
            throw new UnsafeClipboardFormatException(
                $"Формат «{format}» использует неподдерживаемый тип {data.GetType().FullName}.");
        }

        return clone;
    }

    private static MemoryStream CloneStream(Stream source)
    {
        if (!source.CanRead || !source.CanSeek)
        {
            throw new UnsafeClipboardFormatException(
                "Поток данных clipboard нельзя безопасно прочитать и вернуть в исходное состояние.");
        }

        long originalPosition = source.Position;
        try
        {
            source.Position = 0;

            var clone = new MemoryStream();
            source.CopyTo(clone);
            clone.Position = 0;
            return clone;
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    private static StringCollection CloneCollection(StringCollection source)
    {
        var result = new StringCollection();
        result.AddRange(source.Cast<string>().ToArray());
        return result;
    }
}
