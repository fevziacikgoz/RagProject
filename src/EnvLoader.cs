namespace RagMini;

/// <summary>.env dosyasını okuyup ortam değişkenlerine yükler (repoya gitmez).</summary>
public static class EnvLoader
{
    public static void Load()
    {
        foreach (var path in new[] { ".env", Path.Combine(AppContext.BaseDirectory, ".env") })
        {
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Environment.SetEnvironmentVariable(line[..eq].Trim(), line[(eq + 1)..].Trim().Trim('"'));
            }
            return;
        }
    }
}
