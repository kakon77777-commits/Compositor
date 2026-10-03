namespace Compositor.Core.IO;

/// <summary>One profile for recent projects, tools and shortcuts, separate from document files.</summary>
internal static class PreferencePaths
{
    public static string For(string filename)
    {
        var profile = Environment.GetEnvironmentVariable("COMPOSITOR_PROFILE");
        var folder = string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compositor")
            : Path.GetFullPath(profile);
        return Path.Combine(folder, filename);
    }
}
