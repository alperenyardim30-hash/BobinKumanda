using BobinKumanda.Core.Help;

namespace BobinKumanda.Core.Tests;

/// <summary>
/// docs/ altındaki eleman listeleri katalogla aynı olmalı. KLAB_DOCS=write ile çalıştırılırsa dosyalar yeniden üretilir.
/// </summary>
public class DocsTests
{
    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BobinKumanda.sln")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    [Theory]
    [InlineData("tr", "tr/elemanlar.md")]
    [InlineData("en", "en/components.md")]
    public void Component_reference_in_docs_is_up_to_date(string lang, string file)
    {
        string? root = RepoRoot();
        Assert.SkipWhen(root == null, "kaynak klasörü bulunamadı");
        string path = Path.Combine(root!, "docs", file);
        string expected = ComponentReference.Markdown(lang);
        if (Environment.GetEnvironmentVariable("KLAB_DOCS") == "write")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }
        Assert.SkipWhen(!File.Exists(path), "docs dosyası yok");
        Assert.Equal(expected.ReplaceLineEndings("\n"), File.ReadAllText(path).ReplaceLineEndings("\n"));
    }
}
