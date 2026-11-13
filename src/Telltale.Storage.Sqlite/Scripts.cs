using System.Reflection;

namespace Telltale.Storage.Sqlite;

/// <summary>The embedded SQL scripts under <c>Sql/</c>, by folder.</summary>
internal static class Scripts
{
    private static readonly Assembly Assembly = typeof(Scripts).Assembly;

    /// <summary>The scripts of one folder, in file-name order.</summary>
    public static IReadOnlyList<(string Name, string Sql)> InFolder(string folder)
    {
        var prefix = folder + "/";
        return [.. Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => (name[prefix.Length..], Read(name)))];
    }

    private static string Read(string resourceName)
    {
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script {resourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
