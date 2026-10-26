using System.Windows.Media;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App.Rendering;

/// <summary>Paletteki küçük sembol resimleri: elemanın kendi çizimi, varsayılan ayarlarıyla, etiketsiz.</summary>
public static class SymbolIcons
{
    public static DrawingImage Create(ComponentDef def, CanvasTheme theme, SymbolStyle style = SymbolStyle.School)
    {
        var instance = new ComponentInstance { Id = "icon", Type = def.TypeId, Label = "" };
        def.ApplyDefaults(instance);
        var circuit = new Circuit();
        circuit.Components.Add(instance);
        var group = new DrawingGroup();
        using (DrawingContext dc = group.Open())
        {
            var painter = new WpfPainter();
            // Küçültülerek gösterileceği için çizgiler biraz kalın tutulur
            painter.Begin(dc, theme, zoom: 1.2);
            CircuitRenderer.Draw(circuit, painter, new CircuitRenderOptions { Style = style, ShowTerminalNumbers = false, ColorWires = false });
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
