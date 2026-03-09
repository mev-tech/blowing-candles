using System.Text.Json;
using BlowingCandles.Application;

namespace BlowingCandles.Api.Services;

public sealed class SignalsFileReader
{
    private readonly string _signalsJsonPath;

    public SignalsFileReader(string signalsJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalsJsonPath);
        _signalsJsonPath = signalsJsonPath;
    }

    public SignalsFileReadResult ReadSignals()
    {
        if (!File.Exists(_signalsJsonPath))
        {
            return SignalsFileReadResult.NotFound();
        }

        try
        {
            var json = File.ReadAllText(_signalsJsonPath);
            return SignalsFileReadResult.Success(SignalsJsonSerializer.Deserialize(json));
        }
        catch (JsonException)
        {
            return SignalsFileReadResult.Corrupt();
        }
        catch (FileNotFoundException)
        {
            return SignalsFileReadResult.NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return SignalsFileReadResult.NotFound();
        }
        catch (IOException)
        {
            return SignalsFileReadResult.Unavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return SignalsFileReadResult.Unavailable();
        }
    }
}

public enum SignalsFileReadStatus
{
    Success,
    NotFound,
    Corrupt,
    Unavailable
}

public sealed record SignalsFileReadResult(SignalsFileReadStatus Status, IReadOnlyList<SignalFileEntry> Signals)
{
    public static SignalsFileReadResult Success(IReadOnlyList<SignalFileEntry> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return new SignalsFileReadResult(SignalsFileReadStatus.Success, signals);
    }

    public static SignalsFileReadResult NotFound() => new(SignalsFileReadStatus.NotFound, Array.Empty<SignalFileEntry>());

    public static SignalsFileReadResult Corrupt() => new(SignalsFileReadStatus.Corrupt, Array.Empty<SignalFileEntry>());

    public static SignalsFileReadResult Unavailable() => new(SignalsFileReadStatus.Unavailable, Array.Empty<SignalFileEntry>());
}
