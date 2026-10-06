namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

/// <summary>
/// A temporary folder tree for extension tests, deleted afterwards.
/// </summary>
internal sealed class ExtensionTestFolder : IDisposable
{
    public ExtensionTestFolder()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kam-ext-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    public string Write(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
