using System.IO;
using System.Text;
using Serilog;

namespace MettlerDataCollection.Services;

/// <summary>
///     默认实现：每条记录以 "yyyy-MM-dd HH:mm:ss.fff| 原始内容\n" 格式追加写入。
///     关键点：每条都 <c>Flush(true)</c> 刷到 OS 缓冲区，保证断电不丢。
/// </summary>
public class DataPersistenceService : IDataPersistenceService
{
    private readonly object _fileLock = new();

    private FileStream? _stream;
    private StreamWriter? _writer;
    private string? _currentFilePath;

    public string? CurrentFilePath
    {
        get
        {
            lock (_fileLock)
            {
                return _currentFilePath;
            }
        }
    }

    public void StartNewFile(string sampleNo, string dataPath)
    {
        lock (_fileLock)
        {
            CloseFileUnsafe();

            Directory.CreateDirectory(dataPath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _currentFilePath = Path.Combine(dataPath, $"{timestamp}-{sampleNo}.txt");

            // FileMode.Append: 不存在则创建，存在则追加（理论上不会撞名，但保险起见）
            _stream = new FileStream(
                _currentFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None);
            _writer = new StreamWriter(_stream, Encoding.UTF8);

            Log.Information($"已创建数据文件: {_currentFilePath}");
        }
    }

    public void WriteRecord(string rawRecord)
    {
        FileStream? stream;
        StreamWriter? writer;

        lock (_fileLock)
        {
            stream = _stream;
            writer = _writer;
        }

        if (stream is null || writer is null) return;

        try
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}| {rawRecord}{Environment.NewLine}";

            writer.Write(logEntry);
            writer.Flush();

            // 关键：把 FileStream 缓冲刷到操作系统层（防断电）
            stream.Flush(true);
        }
        catch (IOException ex)
        {
            Log.Error($"写入数据文件时发生错误: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_fileLock)
        {
            CloseFileUnsafe();
            _currentFilePath = null;
        }
    }

    private void CloseFileUnsafe()
    {
        _writer?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
    }
}
