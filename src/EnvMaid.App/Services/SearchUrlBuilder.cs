using System.IO;

namespace EnvMaid.App.Services;

public static class SearchUrlBuilder
{
    // ponytail: Google only for now, swap this to build the engine's URL when a settings toggle exists.
    public static string BuildMultipleVersionsQuery(string exeName)
    {
        var name = Path.GetFileNameWithoutExtension(exeName);
        var query = Uri.EscapeDataString($"how to use multiple {name} versions on windows");
        return $"https://www.google.com/search?q={query}";
    }
}
