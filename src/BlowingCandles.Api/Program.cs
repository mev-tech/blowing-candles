namespace BlowingCandles.Api;

internal static class Program
{
    public static void Main(string[] args)
    {
        ApiHost.Build(args).Run();
    }
}
