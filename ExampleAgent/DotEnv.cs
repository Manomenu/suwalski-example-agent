namespace ExampleAgent;

/// <summary>Reads a <c>.env</c> file (<c>KEY=value</c> lines) into the process environment; a variable that is already set wins.</summary>
internal static class DotEnv
{
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
