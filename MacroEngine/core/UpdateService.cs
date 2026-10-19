using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MacroEngine.Core;

/// <summary>A published build the application can update to.</summary>
internal sealed record ReleaseInfo(
    SemVersion Version,
    string Tag,
    string Title,
    string Notes,
    string PageUrl,
    string PackageName,
    Uri PackageUrl,
    long PackageSize,
    Uri ChecksumsUrl,
    DateTimeOffset Published,
    bool Prerelease);

internal sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Self-update from GitHub Releases of <see cref="ProductInfo.RepositoryUrl"/>.
///
/// A release is eligible when it carries <c>MacroEngine-&lt;version&gt;-win-x64.zip</c> and
/// <c>SHA256SUMS.txt</c>. The package is downloaded into the data directory, its hash
/// checked against SHA256SUMS (no checksum — no install), extracted, and copied over
/// the application directory. Files in use cannot be overwritten on Windows but can be
/// renamed, so every replaced file is first moved aside to "*.old" (removed on the
/// next start) and restored if anything fails half-way. User configuration under
/// "config\" and the ".portable" marker are never overwritten.
/// </summary>
internal static class UpdateService
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";
    private const string PackageSuffix = "-win-x64.zip";
    private const string ExecutableName = "MacroEngine.exe";
    private static readonly string ReleasesApi =
        ProductInfo.RepositoryUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases?per_page=20";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MacroEngine", ProductInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Check
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Newest eligible release above <paramref name="current"/>, or null when up to date.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(SemVersion current, bool includePrerelease, CancellationToken ct)
    {
        string json;
        try
        {
            json = await Http.GetStringAsync(ReleasesApi, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Не удалось связаться с GitHub. Проверьте подключение к интернету.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException("GitHub не ответил вовремя.", ex);
        }
        return SelectRelease(json, current, includePrerelease);
    }

    /// <summary>Pick the highest eligible release from a GitHub "list releases" response.</summary>
    public static ReleaseInfo? SelectRelease(string releasesJson, SemVersion current, bool includePrerelease)
    {
        using JsonDocument doc = JsonDocument.Parse(releasesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        ReleaseInfo? best = null;
        foreach (JsonElement r in doc.RootElement.EnumerateArray())
        {
            if (Bool(r, "draft")) continue;
            bool prerelease = Bool(r, "prerelease");
            if (prerelease && !includePrerelease) continue;

            string tag = Str(r, "tag_name");
            SemVersion? version = SemVersion.TryParse(tag);
            if (version == null || !(version > current)) continue;
            if (best != null && !(version > best.Version)) continue;

            JsonElement? package = null, sums = null;
            if (r.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in assets.EnumerateArray())
                {
                    string name = Str(a, "name");
                    if (name.Equals(ChecksumsAssetName, StringComparison.OrdinalIgnoreCase)) sums = a;
                    else if (name.StartsWith("MacroEngine-", StringComparison.OrdinalIgnoreCase)
                             && name.EndsWith(PackageSuffix, StringComparison.OrdinalIgnoreCase)) package = a;
                }
            }
            if (package == null || sums == null) continue;
            if (!Uri.TryCreate(Str(package.Value, "browser_download_url"), UriKind.Absolute, out Uri? packageUrl)) continue;
            if (!Uri.TryCreate(Str(sums.Value, "browser_download_url"), UriKind.Absolute, out Uri? sumsUrl)) continue;

            DateTimeOffset.TryParse(Str(r, "published_at"), out DateTimeOffset published);
            best = new ReleaseInfo(
                version,
                tag,
                Str(r, "name") is { Length: > 0 } title ? title : tag,
                Str(r, "body"),
                Str(r, "html_url"),
                Str(package.Value, "name"),
                packageUrl,
                package.Value.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0,
                sumsUrl,
                published,
                prerelease);
        }
        return best;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    // ═══════════════════════════════════════════════════════════════
    //  Download and verify
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Download the package into the updates folder and verify its SHA-256.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        string dir = Path.Combine(AppPaths.UpdatesDirectory, release.Version.ToString());
        Directory.CreateDirectory(dir);
        string zipPath = Path.Combine(dir, release.PackageName);

        string sums;
        try
        {
            sums = await Http.GetStringAsync(release.ChecksumsUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Не удалось скачать контрольные суммы релиза.", ex);
        }
        string expected = FindChecksum(sums, release.PackageName)
            ?? throw new UpdateException($"В {ChecksumsAssetName} нет суммы для {release.PackageName} — установка отменена.");

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.PackageSize;

            await using Stream source = await response.Content.ReadAsStreamAsync(ct);
            await using (var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Загрузка обновления прервалась.", ex);
        }

        string actual = await ComputeSha256Async(zipPath, ct);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(zipPath);
            throw new UpdateException("Контрольная сумма скачанного файла не совпала — файл повреждён или подменён. Установка отменена.");
        }
        return zipPath;
    }

    /// <summary>Find the hash for <paramref name="fileName"/> in "sha256sum" output ("hash  name" or "hash *name").</summary>
    public static string? FindChecksum(string sumsText, string fileName)
    {
        foreach (string raw in sumsText.Split('\n'))
        {
            string line = raw.Trim();
            int space = line.IndexOfAny(new[] { ' ', '\t' });
            if (space != 64) continue;
            string hash = line[..64];
            string name = line[space..].Trim().TrimStart('*');
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && hash.All(Uri.IsHexDigit))
                return hash.ToLowerInvariant();
        }
        return null;
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Install
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Extract a verified package and install it over the running application.</summary>
    public static void InstallZip(string zipPath, string appDirectory)
    {
        string staging = Path.Combine(Path.GetDirectoryName(zipPath)!, "files");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            // ExtractToDirectory rejects entries that would land outside the target.
            ZipFile.ExtractToDirectory(zipPath, staging);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new UpdateException("Архив обновления повреждён.", ex);
        }
        InstallFiles(staging, appDirectory);
    }

    /// <summary>
    /// Copy <paramref name="packageDirectory"/> over <paramref name="appDirectory"/>.
    /// All-or-nothing: on any failure the files already replaced are put back.
    /// </summary>
    public static void InstallFiles(string packageDirectory, string appDirectory)
    {
        if (!File.Exists(Path.Combine(packageDirectory, ExecutableName)))
            throw new UpdateException($"В архиве обновления нет {ExecutableName}.");

        EnsureWritable(appDirectory);

        var done = new List<(string Target, string? Backup)>();
        try
        {
            foreach (string source in Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(packageDirectory, source);
                string target = Path.Combine(appDirectory, relative);
                if (IsProtected(relative) && File.Exists(target)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = FreeBackupName(target);
                    File.Move(target, backup);
                }
                done.Add((target, backup));
                File.Copy(source, target, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var (target, backup) = done[i];
                try
                {
                    if (File.Exists(target) && (backup == null || File.Exists(backup))) File.Delete(target);
                    if (backup != null && File.Exists(backup)) File.Move(backup, target);
                }
                catch (Exception rollbackEx)
                {
                    AppLog.Write($"Update rollback failed for {target}: {rollbackEx.GetType().Name}: {rollbackEx.Message}");
                }
            }
            throw ex as UpdateException ?? new UpdateException("Не удалось заменить файлы программы: " + ex.Message, ex);
        }
    }

    /// <summary>User data that lives beside the EXE in portable mode is never overwritten.</summary>
    private static bool IsProtected(string relativePath)
    {
        string p = relativePath.Replace('\\', '/');
        return p.StartsWith("config/", StringComparison.OrdinalIgnoreCase)
            || p.Equals(".portable", StringComparison.OrdinalIgnoreCase);
    }

    private static string FreeBackupName(string target)
    {
        string backup = target + ".old";
        if (!File.Exists(backup)) return backup;
        try
        {
            File.Delete(backup);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A previous ".old" is still locked; use a unique name, cleaned up later.
            return $"{target}.old-{Guid.NewGuid():N}"[..(target.Length + 13)];
        }
    }

    private static void EnsureWritable(string directory)
    {
        string probe = Path.Combine(directory, $".update-probe-{Environment.ProcessId}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UpdateException(
                "Нет прав на запись в папку программы. Переместите MacroEngine в папку пользователя " +
                "или скачайте новую версию со страницы релиза вручную.", ex);
        }
    }

    /// <summary>Remove "*.old" leftovers of a previous update and finished downloads. Best effort.</summary>
    public static void CleanupLeftovers(string appDirectory, string? downloadsDirectory = null)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(appDirectory, "*.old*", SearchOption.AllDirectories))
            {
                // Updates never back up into the protected user area, so never clean it either.
                if (IsProtected(Path.GetRelativePath(appDirectory, file))) continue;
                string name = Path.GetFileName(file);
                if (name.EndsWith(".old", StringComparison.OrdinalIgnoreCase) || name.Contains(".old-", StringComparison.OrdinalIgnoreCase))
                    TryDelete(file);
            }
            if (downloadsDirectory != null && Directory.Exists(downloadsDirectory))
                Directory.Delete(downloadsDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Update cleanup skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* still locked; next start will retry */ }
    }
}
