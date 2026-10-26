using System.Formats.Nrbf;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.IO;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Eski .esc biçiminde kaydetme: yazılan dosya geri okununca içerik ve bağlantılar aynı kalmalı.</summary>
public class EscWriterTests
{
    private static string? Sample(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "LegacySamples", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static TheoryData<string> Files => new() { "3f.esc", "asansor.esc", "kapi mix.esc", "kapi1.esc", "kapi2.esc", "proble2.esc", "problem2.esc", "yildizucgen.esc" };

    [Theory]
    [MemberData(nameof(Files))]
    public void Legacy_document_round_trips_through_the_writer(string file)
    {
        string? path = Sample(file);
        Assert.SkipWhen(path == null, "tests/LegacySamples yok");
        EscDocument a = EscReader.Read(path!);
        byte[] bytes = EscWriter.ToBytes(a);
        EscDocument b = EscReader.Read(new MemoryStream(bytes));

        Assert.Equal(a.Elements.Count, b.Elements.Count);
        for (int i = 0; i < a.Elements.Count; i++)
        {
            EscElement x = a.Elements[i], y = b.Elements[i];
            Assert.Equal((x.Type, x.Location, x.Label1Location, x.Label2Location, x.Label1, x.Label2, x.Label2Visible, x.Rotation),
                (y.Type, y.Location, y.Label1Location, y.Label2Location, y.Label1, y.Label2, y.Label2Visible, y.Rotation));
            Assert.Equal(x.Links, y.Links);
            Assert.Equal(x.ConnPoints, y.ConnPoints);
            Assert.Equal(x.ColorName is null or "Black" ? null : x.ColorName, y.ColorName is null or "Black" ? null : y.ColorName);
        }
        Assert.Equal(a.Lines.Select(l => (l.Kind, l.A, l.B, l.ConnA, l.ConnB)), b.Lines.Select(l => (l.Kind, l.A, l.B, l.ConnA, l.ConnB)));
        Assert.Equal(a.Dots.Select(d => (d.Origin, string.Join(",", d.ConnPoints))), b.Dots.Select(d => (d.Origin, string.Join(",", d.ConnPoints))));
        Assert.Equal(a.ConnPoints.Select(c => (c.Side, c.ConnectedTo)), b.ConnPoints.Select(c => (c.Side, c.ConnectedTo)));
        Assert.Equal(a.Texts.Select(t => (t.Location, t.Size, t.Text)), b.Texts.Select(t => (t.Location, t.Size, t.Text)));
    }

    /// <summary>Yazılan akış eski programın beklediği tip ve kütüphane adlarını taşır.</summary>
    [Fact]
    public void Written_file_uses_the_old_type_names()
    {
        var doc = new EscDocument();
        doc.Elements.Add(new EscElement { Type = 9, Label1 = "H1", Label2 = "0", ColorName = "Red" });
        doc.ConnPoints.Add(new EscConnPoint { Side = EscSide.Left });
        doc.Elements[0].ConnPoints.Add(0);
        doc.Texts.Add(new EscText { Text = "not", Size = new EscPoint(80, 20) });
        var ms = new MemoryStream(EscWriter.ToBytes(doc));
        Assert.Equal(EscDocument.Signature, ((PrimitiveTypeRecord<string>)NrbfDecoder.Decode(ms, leaveOpen: true)).Value);
        var root = (ClassRecord)NrbfDecoder.Decode(ms, leaveOpen: true);
        Assert.Equal("System.Collections.ArrayList", root.TypeName.FullName);

        string text = System.Text.Encoding.UTF8.GetString(EscWriter.ToBytes(doc));
        Assert.Contains("ESCS.Elements+ElementType", text);
        Assert.Contains("ESCS.CPoint+Sides", text);
        Assert.Contains("EKTS, Version=1.0.2012.17010, Culture=neutral, PublicKeyToken=null", text);
        Assert.Contains("System.Drawing.Size", text);
        Assert.Equal("Red", EscReader.Read(new MemoryStream(EscWriter.ToBytes(doc))).Elements[0].ColorName);
    }

    /// <summary>Örnek devre eski biçime çevrilip geri açılınca aynı klemensler aynı iletkende olmalı.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void Exported_circuit_keeps_its_connections(string file)
    {
        string? path = Sample(file);
        Assert.SkipWhen(path == null, "tests/LegacySamples yok");
        Circuit original = EscImporter.Read(path!).Circuit;
        EscExportResult export = EscExporter.Export(original);
        Assert.Empty(export.Warnings);
        Circuit back = EscImporter.Import(EscReader.Read(new MemoryStream(EscWriter.ToBytes(export.Document)))).Circuit;
        AssertSameConnectivity(original, back);
    }

    /// <summary>Bir klemense üç kablo, bir ek noktasına altı kablo: eski biçimde ek noktası zinciri gerekir.</summary>
    [Fact]
    public void Crowded_terminals_and_junctions_are_split_into_dots()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var lamps = Enumerable.Range(1, 6).Select(i => b.Add("lamp", "H" + i)).ToList();
        var j = new Junction { Id = b.Circuit.NewId("j"), Position = new Vec(300, 200) };
        b.Circuit.Junctions.Add(j);
        foreach (var h in lamps)
        {
            var def = ComponentCatalog.Get("lamp");
            b.Circuit.Wires.Add(new Wire
            {
                Id = b.Circuit.NewId("w"),
                A = WireEnd.ToJunction(j.Id),
                B = WireEnd.ToTerminal(h.Id, "X1"),
                Points = [j.Position, new Vec(j.Position.X, 260), def.TerminalWorld(h, def.FindTerminal("X1")!)]
            });
            b.Wire(h, "X2", n, "N"); // N klemensine altı kablo
        }
        b.Wire(l1, "L1", lamps[0], "X1");
        b.Circuit.Wires.Add(new Wire
        {
            Id = b.Circuit.NewId("w"),
            A = WireEnd.ToTerminal(l1.Id, "L1"),
            B = WireEnd.ToJunction(j.Id),
            Points = [new Vec(5, 30), new Vec(5, 200), j.Position]
        });

        EscExportResult export = EscExporter.Export(b.Circuit);
        Assert.Empty(export.Warnings);
        Assert.True(export.Document.Dots.Count >= 4);
        Assert.All(export.Document.ConnPoints.Select((c, i) => (c, i)), x =>
            Assert.True(x.c.ConnectedTo < 0 || export.Document.ConnPoints[x.c.ConnectedTo].ConnectedTo == x.i));
        Circuit back = EscImporter.Import(EscReader.Read(new MemoryStream(EscWriter.ToBytes(export.Document)))).Circuit;
        AssertSameConnectivity(b.Circuit, back);

        // geri açılan devre de çalışır: altı lamba yanar
        var sim = new Simulator(back);
        Assert.All(back.Components.Where(c => c.Type == "lamp"), c => Assert.True(sim.Find(c.Id)!.Visual.Active));
    }

    [Fact]
    public void New_components_and_extra_sheets_are_reported()
    {
        var b = new CircuitBuilder();
        b.Add("mcb-1p", "F1");
        b.Add("lamp", "H1");
        b.Circuit.Sheets.Add(new Sheet { Id = "s2" });
        EscExportResult export = EscExporter.Export(b.Circuit);
        Assert.Single(export.Document.Elements);
        Assert.Equal(2, export.Warnings.Count);
    }

    /// <summary>Eleman sırası korunduğundan i. eleman ile i. eleman karşılaştırılır.</summary>
    private static void AssertSameConnectivity(Circuit a, Circuit b)
    {
        var ca = a.Components.Where(c => ComponentCatalog.Get(c.Type).LegacyType != null).ToList();
        var cb = b.Components;
        Assert.Equal(ca.Count, cb.Count);
        Netlist na = Netlist.Build(a), nb = Netlist.Build(b);
        var ta = new List<int>();
        var tb = new List<int>();
        var names = new List<string>();
        for (int i = 0; i < ca.Count; i++)
        {
            Assert.Equal(ca[i].Type, cb[i].Type);
            Assert.Equal(ca[i].Position, cb[i].Position);
            ComponentDef def = ComponentCatalog.Get(ca[i].Type);
            for (int k = 0; k < def.LegacyTerminalCount; k++)
            {
                string t = def.Terminals[k].Id;
                // bağlı olmayan klemensler kendi iletkenindedir; karşılaştırmada yalnızca bağlı olanlar
                ta.Add(na.Nets[na.NodeOf(ca[i].Id, t)].Points.Count > 1 ? na.NodeOf(ca[i].Id, t) : -1 - ta.Count);
                tb.Add(nb.Nets[nb.NodeOf(cb[i].Id, t)].Points.Count > 1 ? nb.NodeOf(cb[i].Id, t) : -1 - tb.Count);
                names.Add($"{i}:{ca[i].Type}.{t}");
            }
        }
        for (int x = 0; x < ta.Count; x++)
        {
            for (int y = x + 1; y < ta.Count; y++)
            {
                Assert.True((ta[x] == ta[y]) == (tb[x] == tb[y]), $"{names[x]} ve {names[y]}: önce {(ta[x] == ta[y] ? "bağlı" : "ayrı")}");
            }
        }
    }
}
