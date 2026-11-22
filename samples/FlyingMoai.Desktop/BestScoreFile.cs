namespace FlyingMoai.Desktop;

internal static class BestScoreFile
{
    // DoNotVerify keeps the profile path when the save directory doesn't exist yet.
    private static string BestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "FlyingMoai", "best.txt");

    public static int Load()
    {
        try
        {
            return int.TryParse(File.ReadAllText(BestPath), out var best) ? best : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static void Save(int best)
    {
        try
        {
            var path = BestPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, best.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only profile leaves the best score available for this session.
        }
    }
}
