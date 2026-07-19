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
    private readonly List<(string Format, object Data)> _items;
    private readonly bool _wasEmpty;

    private ClipboardSnapshot(List<(string Format, object Data)> items, bool wasEmpty)
    {
        _items = items;
        _wasEmpty = wasEmpty;
    }

    public static ClipboardSnapshot Capture()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                IDataObject? source = Clipboard.GetDataObject();
                if (source == null)
                    return new ClipboardSnapshot(new(), wasEmpty: true);

                var items = new List<(string Format, object Data)>();
                foreach (string format in source.GetFormats(autoConvert: false).Distinct(StringComparer.Ordinal))
                {
                    object? data = source.GetData(format, autoConvert: false);
                    if (data != null)
                        items.Add((format, CloneKnownData(data)));
                }

                return new ClipboardSnapshot(items, wasEmpty: items.Count == 0);
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
                    restored.SetData(format, autoConvert: false, CloneKnownData(data));

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

    private static object CloneKnownData(object data) => data switch
    {
        byte[] bytes => bytes.ToArray(),
        string[] strings => strings.ToArray(),
        MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
        StringCollection collection => CloneCollection(collection),
        Image image => image.Clone(),
        ICloneable cloneable => cloneable.Clone() ?? data,
        _ => data
    };

    private static StringCollection CloneCollection(StringCollection source)
    {
        var result = new StringCollection();
        result.AddRange(source.Cast<string>().ToArray());
        return result;
    }
}
