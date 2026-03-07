namespace BlowingCandles.Infrastructure.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"blowing-candles-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }

    public void WriteFile(string relativePath, string contents)
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
