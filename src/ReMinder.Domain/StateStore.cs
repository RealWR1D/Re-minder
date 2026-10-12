using System.Text;
using System.Text.Json;

namespace ReMinder.Domain;

public sealed class StateStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private AppState state = new();
    public string DirectoryPath { get; }
    public string DataPath => Path.Combine(DirectoryPath, "state.json");
    public string BackupPath => Path.Combine(DirectoryPath, "backups");
    public string RecoveryNotice { get; private set; } = "";
    public string LastSaveError { get; private set; } = "";
    public bool ReadOnly { get; private set; }
    public event Action? Changed;
    public AppState Snapshot => Serialization.Copy(state);
    public StateStore(string directory) => DirectoryPath = Path.GetFullPath(directory);

    public async Task LoadAsync()
    {
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(BackupPath);
        if (!File.Exists(DataPath)) return;
        try
        {
            state = await ReadAsync(DataPath);
            try { await BackupAsync("startup"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { RecoveryNotice = "任务数据已读取，但启动备份失败。请检查磁盘空间和目录权限。"; }
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Future versions must never be replaced by an older program.
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(DataPath));
                if (doc.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() > 1) { ReadOnly = true; RecoveryNotice = "数据来自较新版本，请使用更新的 Re:minder 打开。原文件已保留。"; return; }
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException) { }
            var candidates = Directory.EnumerateFiles(BackupPath, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            foreach (var file in candidates)
            {
                try
                {
                    var recovered = await ReadAsync(file);
                    File.Copy(DataPath, Path.Combine(DirectoryPath, $"damaged-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json"), false);
                    state = recovered;
                    await WriteAtomicAsync(state);
                    RecoveryNotice = "数据文件异常，已恢复最近的有效备份，原文件另行保留。";
                    return;
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            ReadOnly = true;
            RecoveryNotice = "无法读取任务且没有有效备份。原文件已保留，请从设置中打开数据目录或导出恢复文件。";
        }
    }

    private static async Task<AppState> ReadAsync(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("数据文件过大。");
        var value = JsonSerializer.Deserialize<AppState>(await File.ReadAllTextAsync(path), Serialization.Json) ?? throw new InvalidDataException("数据为空。");
        Validation.State(value);
        return value;
    }

    public async Task<bool> CommitAsync(Func<AppState, bool> mutate)
    {
        await gate.WaitAsync();
        var changed = false;
        try
        {
            if (ReadOnly) throw new InvalidOperationException(RecoveryNotice);
            var candidate = Serialization.Copy(state);
            if (!mutate(candidate)) return false;
            candidate = candidate with { StateVersion = state.StateVersion + 1 };
            Validation.State(candidate);
            await WriteAtomicAsync(candidate);
            state = candidate;
            LastSaveError = "";
            changed = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastSaveError = "未能保存，请检查磁盘空间和数据目录权限后重试。";
            throw new IOException(LastSaveError, e);
        }
        finally { gate.Release(); }
        if (changed) Changed?.Invoke();
        return changed;
    }

    private async Task WriteAtomicAsync(AppState candidate)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = Path.Combine(DirectoryPath, $".state-{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, Serialization.Json));
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough | FileOptions.Asynchronous))
            { await file.WriteAsync(bytes); await file.FlushAsync(); file.Flush(true); }
            _ = await ReadAsync(temp);
            if (File.Exists(DataPath))
            {
                var backup = Path.Combine(BackupPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfffffff}-previous.json");
                File.Replace(temp, DataPath, backup, true);
                RotateBackups();
            }
            else File.Move(temp, DataPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task BackupAsync(string reason)
    {
        await gate.WaitAsync();
        try
        {
            if (!File.Exists(DataPath)) return;
            _ = await ReadAsync(DataPath);
            Directory.CreateDirectory(BackupPath);
            File.Copy(DataPath, Path.Combine(BackupPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfffffff}-{reason}.json"));
            RotateBackups();
        }
        finally { gate.Release(); }
    }

    private void RotateBackups()
    {
        // Cleanup cannot turn a successful atomic replace into a reported save failure.
        try
        {
            var backups = Directory.EnumerateFiles(BackupPath, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            foreach (var file in backups.Skip(7)) { try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
