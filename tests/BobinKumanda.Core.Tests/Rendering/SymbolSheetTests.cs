using System.Globalization;
using System.Text;
using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Tests.Rendering;

public class SymbolSheetTests
{
    /// <summary>Katalogdaki her sembolü çizer; hata vermemeli. KLAB_SHEET ortam değişkeni varsa SVG dosyası yazar.</summary>
    [Fact]
    public void All_symbols_draw_without_errors()
    {
        string svg = Render(active: false);
        Assert.Contains("<svg", svg);
        string? path = Environment.GetEnvironmentVariable("KLAB_SHEET");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, svg);
            File.WriteAllText(Path.ChangeExtension(path, ".active.svg"), Render(active: true));
        }
    }

    private static string Render(bool active)
    {
        const double scale = 2.2, cellW = 190, cellH = 170;
        var defs = ComponentCatalog.All.Where(d => d.Size.X <= 90 && d.Size.Y <= 110).ToList();
        int cols = 6;
        int rows = (defs.Count + cols - 1) / cols;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {cols * cellW} {rows * cellH}\" width=\"{cols * cellW}\" height=\"{rows * cellH}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        for (int i = 0; i < defs.Count; i++)
        {
            ComponentDef def = defs[i];
            var inst = new ComponentInstance { Id = "x", Type = def.TypeId, Label = def.DefaultLabel ?? def.LabelPrefix + "1" };
            def.ApplyDefaults(inst);
            double cx = (i % cols) * cellW, cy = (i / cols) * cellH;
            var size = def.Size * scale;
            var offset = new Vec(cx + (cellW - size.X) / 2, cy + 18 + (110 - size.Y) / 2);
            SymbolState? state = null;
            if (active)
            {
                state = new SymbolState
                {
                    Active = true,
                    Position = 1,
                    Level = 0.9,
                    Direction = 1,
                    Closed = def.Role == DeviceRole.Follower ? [true, true, true] : [false, true, false, true, false, true]
                };
            }
            var painter = new SvgPainter(sb, def, inst, offset, scale);
            def.Draw(painter, new DrawContext { Instance = inst, Def = def, State = state, AnimTime = 0.3 });
            foreach (TerminalDef t in def.Terminals)
            {
                Vec p = t.Position * scale + offset;
                sb.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{p.X:0.#}\" cy=\"{p.Y:0.#}\" r=\"2.6\" fill=\"#1c7ed6\"/>");
                if (t.Number != null && def.Terminals.Count <= 9)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{p.X + 3:0.#}\" y=\"{p.Y - 4:0.#}\" font-size=\"9\" fill=\"#1c7ed6\" font-family=\"Consolas, monospace\">{t.Number}</text>");
                }
            }
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{cx + 8:0}\" y=\"{cy + 14:0}\" font-size=\"12\" font-weight=\"700\" fill=\"#1f2430\" font-family=\"Segoe UI, Arial\">{inst.Label}</text>");
            string name = System.Security.SecurityElement.Escape(def.Name)!;
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{cx + cellW / 2:0}\" y=\"{cy + cellH - 14:0}\" font-size=\"11.5\" text-anchor=\"middle\" fill=\"#3b4252\" font-family=\"Segoe UI, Arial\">{name}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
