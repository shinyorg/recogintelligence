namespace Shiny.FaceIntelligence.Testing;

/// <summary>
/// Locates the bundled sqlite-vec native binary (<c>vec0</c>) so vector-backed tests/benchmarks can
/// load it. Searches next to the running assembly first, then walks up to a committed
/// <c>runtimes/&lt;rid&gt;/native</c> folder (covers BenchmarkDotNet's generated sub-process layout).
/// Returns null when missing — callers <c>Skip</c> on CI that hasn't provisioned it.
/// </summary>
public static class Vec0Locator
{
    static readonly string[] Names = ["vec0.dylib", "vec0.so", "vec0.dll"];

    public static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var name in Names)
            {
                var direct = Path.Combine(dir.FullName, name);
                if (File.Exists(direct))
                    return direct;
            }

            var runtimes = Path.Combine(dir.FullName, "runtimes");
            if (Directory.Exists(runtimes))
            {
                foreach (var name in Names)
                {
                    var hit = Directory.GetFiles(runtimes, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null)
                        return hit;
                }
            }
        }
        return null;
    }
}
