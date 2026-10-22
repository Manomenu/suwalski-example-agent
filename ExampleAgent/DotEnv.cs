namespace ExampleAgent;

/// <summary>Reads a <c>.env</c> file (<c>KEY=value</c> lines) into the process environment; a variable that is already set wins.</summary>
internal static class DotEnv
{
    /// <summary>
    /// The nearest <c>.env</c> in <paramref name="directory"/> or above it, or null: <c>dotnet run --project ExampleAgent</c> starts the
    /// agent in <c>ExampleAgent/</c>, while the file is at the repository root.
    /// </summary>
    public static string? Find(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, ".env");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        foreach (var line in File.ReadLines(path).Select(line => line.Trim()).Where(line => line.Length > 0 && line[0] != '#'))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }
            var name = line[..separator].Trim();
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, line[(separator + 1)..].Trim().Trim('"', '\''));
            }
        }
    }
}
