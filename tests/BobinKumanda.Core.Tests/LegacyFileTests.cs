using System.Globalization;
using System.Text;
using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.IO;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;
using BobinKumanda.Core.Tests.Rendering;

namespace BobinKumanda.Core.Tests;

/// <summary>
/// Eski EKTS örnekleriyle uyum. Örnek dosyalar orijinal programa ait olduğu için depoda değildir (tests/LegacySamples);
/// klasör yoksa bu testler atlanır. Beklenen sayılar dosyaların gerçek BinaryFormatter çıktısından alınmıştır.
/// </summary>
public class LegacyFileTests
{
    private static string? SamplesDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "LegacySamples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string Sample(string name)
    {
        string? dir = SamplesDir();
        Assert.SkipWhen(dir == null, "tests/LegacySamples yok");
        return Path.Combine(dir!, name);
    }

    public static TheoryData<string, int, int, int, int> Samples => new()
    {
        { "3f.esc", 2, 18, 3, 57 },
        { "asansor.esc", 13, 81, 11, 264 },
        { "kapi mix.esc", 3, 16, 3, 69 },
        { "kapi1.esc", 13, 86, 11, 262 },
        { "kapi2.esc", 13, 87, 11, 264 },
        { "proble2.esc", 29, 145, 23, 450 },
        { "problem2.esc", 29, 145, 23, 450 },
        { "yildizucgen.esc", 19, 73, 14, 251 }
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Reads_same_records_as_the_old_program(string file, int elements, int lines, int dots, int connPoints)
    {
        EscDocument doc = EscReader.Read(Sample(file));
        Assert.Equal(elements, doc.Elements.Count);
        Assert.Equal(lines, doc.Lines.Count);
        Assert.Equal(dots, doc.Dots.Count);
        Assert.Equal(connPoints, doc.ConnPoints.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Imports_without_warnings_and_every_wire_end_sits_on_its_terminal(string file, int elements, int lines, int dots, int connPoints)
    {
        _ = (lines, connPoints);
        EscImportResult r = EscImporter.Read(Sample(file));
        Assert.Empty(r.Warnings);
        Assert.Equal(0, r.MisplacedWireEnds);
        Assert.Equal(elements, r.Circuit.Components.Count);
        Assert.Equal(dots, r.Circuit.Junctions.Count);
        Assert.All(r.Circuit.Wires, w => Assert.True(w.Points.Count >= 2));
    }

    /// <summary>
    /// Bağlantı grafiği eski dosyadakiyle aynı olmalı: eski bağlantı noktalarından bağımsız olarak kurulan iletken
    /// gruplarıyla yeni devrenin netlist'i karşılaştırılır. İki klemens eskide aynı iletkendeyse yenide de öyledir.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void Connectivity_matches_the_old_file(string file, int elements, int lines, int dots, int connPoints)
    {
        _ = (elements, lines, dots, connPoints);
        EscDocument doc = EscReader.Read(Sample(file));
        EscImportResult r = EscImporter.Import(doc);

        int n = doc.ConnPoints.Count;
        int[] parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);
        for (int i = 0; i < n; i++)
        {
            if (doc.ConnPoints[i].ConnectedTo >= 0)
            {
                Union(i, doc.ConnPoints[i].ConnectedTo);
            }
        }
        foreach (EscLine l in doc.Lines)
        {
            Union(l.ConnA, l.ConnB);
        }
        foreach (EscDot d in doc.Dots)
        {
            foreach (int cp in d.ConnPoints)
            {
                Union(d.ConnPoints[0], cp);
            }
        }

        // Elemanların sırası korunur: i. eski eleman = i. bileşen
        Netlist net = Netlist.Build(r.Circuit);
        var terminals = new List<(int OldCp, int Node, string Name)>();
        for (int i = 0; i < doc.Elements.Count; i++)
        {
            ComponentInstance c = r.Circuit.Components[i];
            ComponentDef def = ComponentCatalog.Get(c.Type);
            for (int k = 0; k < def.LegacyTerminalCount; k++)
            {
                terminals.Add((doc.Elements[i].ConnPoints[k], net.NodeOf(c.Id, def.Terminals[k].Id), $"{i}:{c.Type}.{def.Terminals[k].Id}"));
            }
        }
        foreach (var a in terminals)
        {
            foreach (var b in terminals)
            {
                bool oldSame = Find(a.OldCp) == Find(b.OldCp);
                bool newSame = a.Node == b.Node;
                Assert.True(oldSame == newSame, $"{file}: {a.Name} ile {b.Name} eskide {(oldSame ? "bağlı" : "ayrı")}, yenide {(newSame ? "bağlı" : "ayrı")}");
            }
        }
    }

    [Fact]
    public void Star_delta_sample_runs_star_then_delta()
    {
        Circuit c = EscImporter.Read(Sample("yildizucgen.esc")).Circuit;
        ComponentInstance start = c.Components.Single(x => x.Label == "START");
        ComponentInstance stop = c.Components.Single(x => x.Label == "STOP");
        ComponentInstance motor = c.Components.Single(x => x.Type == "motor-3ph-6t");
        ComponentInstance timer = c.Components.Single(x => x.Type == "timer-on");
        Assert.Equal(3, timer.GetDouble("time", 0));

        var sim = new Simulator(c);
        sim.Click(start);
        sim.Run(1.5);
        Assert.DoesNotContain(sim.Problems, p => p.Severity == ProblemSeverity.Error);
        Assert.True(sim.Of(motor).Visual.Active);
        Assert.StartsWith("yıldız", ConnectionOf(sim, motor));

        sim.Run(4);
        Assert.Equal(SimStatus.Running, sim.Status);
        Assert.True(sim.Of(motor).Visual.Active);
        Assert.StartsWith("üçgen", ConnectionOf(sim, motor));

        sim.Click(stop);
        sim.Run(3);
        Assert.False(sim.Of(motor).Visual.Active);
    }

    private static string ConnectionOf(Simulator sim, ComponentInstance motor) =>
        sim.Of(motor).Readouts(sim.Solution!).First(r => r.Value.Contains("V/")).Value;

    [Theory]
    [MemberData(nameof(Samples))]
    public void Circuit_file_round_trip_keeps_everything(string file, int elements, int lines, int dots, int connPoints)
    {
        _ = (elements, lines, dots, connPoints);
        Circuit a = EscImporter.Read(Sample(file)).Circuit;
        var ms = new MemoryStream();
        CircuitFile.Save(a, ms);
        ms.Position = 0;
        Circuit b = CircuitFile.Load(ms);
        var ms2 = new MemoryStream();
        CircuitFile.Save(b, ms2);
        Assert.Equal(Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8.GetString(ms2.ToArray()));
        Assert.Equal(a.Components.Count, b.Components.Count);
        Assert.Equal(a.Wires.Count, b.Wires.Count);
        // Yüklenen devrede yeni kimlikler eskilerle çakışmamalı
        string id = b.NewId("c");
        Assert.DoesNotContain(b.Components, x => x.Id == id);
    }

    /// <summary>Örnekleri SVG olarak çizer; KLAB_RENDER ortam değişkeni bir klasörse dosyaları oraya yazar.</summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void Renders_sample(string file, int elements, int lines, int dots, int connPoints)
    {
        _ = (elements, lines, dots, connPoints);
        Circuit c = EscImporter.Read(Sample(file)).Circuit;
        foreach (bool numbers in new[] { false, true })
        {
            string svg = RenderSvg(c, scale: 2.4, numbers);
            Assert.Contains("<svg", svg);
            string? dir = Environment.GetEnvironmentVariable("KLAB_RENDER");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                string name = Path.GetFileNameWithoutExtension(file).Replace(' ', '_') + (numbers ? ".numbers" : "") + ".svg";
                File.WriteAllText(Path.Combine(dir, name), svg);
            }
        }
    }

    internal static string RenderSvg(Circuit c, double scale, bool numbers)
    {
        RectD box = CircuitRenderer.Bounds(c).Inflate(12);
        var sb = new StringBuilder();
        double w = box.Width * scale, h = box.Height * scale;
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w:0} {h:0}\" width=\"{w:0}\" height=\"{h:0}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        var painter = new SvgPainter(sb, new Vec(-box.X, -box.Y) * scale, scale);
        CircuitRenderer.Draw(c, painter, new CircuitRenderOptions { ShowTerminalNumbers = numbers });
        sb.Append("</svg>");
        return sb.ToString();
    }
}
