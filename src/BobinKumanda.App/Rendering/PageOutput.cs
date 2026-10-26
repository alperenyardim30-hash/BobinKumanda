using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App.Rendering;

/// <summary>Antetli sayfa çıktısı: PNG/JPG resim ve yazıcı. Her zaman açık temada (beyaz kâğıt) çizilir.</summary>
internal static class PageOutput
{
    /// <summary>Resim çözünürlüğü: 200 nokta/inç (A4 yatay ≈ 2340 × 1650 piksel).</summary>
    public const double ImageDpi = 200;

    public sealed record Look(SymbolStyle Style, bool TerminalNumbers, bool CrossReference);

    private static string Footer => App.ProductName + " " + (typeof(PageOutput).Assembly.GetName().Version?.ToString(3) ?? "");

    public static DrawingVisual Page(Circuit circuit, string sheet, PageFormat format, double pxPerMm, Look look)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var painter = new WpfPainter { PixelsPerDip = 1 };
            painter.Begin(dc, CanvasTheme.Light, pxPerMm);
            dc.PushTransform(new ScaleTransform(pxPerMm, pxPerMm));
            PageRenderer.Draw(circuit, sheet, painter, format, new CircuitRenderOptions
            {
                Sheet = sheet,
                Style = look.Style,
                ShowTerminalNumbers = look.TerminalNumbers,
                ShowCrossReference = look.CrossReference
            }, Footer);
            dc.Pop();
        }
        return visual;
    }

    /// <summary>Sayfayı resim olarak kaydeder; uzantı .jpg/.jpeg ise JPG, değilse PNG.</summary>
    public static void SaveImage(Circuit circuit, string sheet, Look look, string path, PageFormat? format = null)
    {
        format ??= PageFormat.A4;
        double pxPerMm = ImageDpi / 25.4;
        int w = (int)Math.Round(format.WidthMm * pxPerMm), h = (int)Math.Round(format.HeightMm * pxPerMm);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(Page(circuit, sheet, format, pxPerMm, look));
        bmp.Freeze();
        string ext = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder encoder = ext is ".jpg" or ".jpeg" ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
        // Kâğıt boyutu doğru görünsün diye çözünürlük bilgisi de 200 dpi yazılır
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>Yazdırma penceresini açar; seçilen kâğıda (A4/A3) yatay sığdırarak sayfaları yazdırır.</summary>
    public static bool Print(Circuit circuit, IReadOnlyList<string> sheets, Look look, string title)
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = false };
        try
        {
            dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
            dlg.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        }
        catch (PrintQueueException)
        {
            // Yazıcı ayarı değiştirilemiyorsa varsayılanla devam
        }
        if (dlg.ShowDialog() != true)
        {
            return false;
        }
        var size = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
        dlg.PrintDocument(new SheetPaginator(circuit, sheets, look, size), title);
        return true;
    }

    private sealed class SheetPaginator(Circuit circuit, IReadOnlyList<string> sheets, Look look, Size pageSize) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;
        public override int PageCount => sheets.Count;
        public override Size PageSize { get => pageSize; set => pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            // Kâğıt A3 kadar büyükse A3 düzeni; yatay değilse sayfa yine yatay çizilip 90° döndürülür
            bool portrait = pageSize.Height > pageSize.Width;
            double longSide = Math.Max(pageSize.Width, pageSize.Height), shortSide = Math.Min(pageSize.Width, pageSize.Height);
            PageFormat format = longSide >= 1450 ? PageFormat.A3 : PageFormat.A4;
            double pxPerMm = Math.Min(longSide / format.WidthMm, shortSide / format.HeightMm);
            DrawingVisual page = Page(circuit, sheets[pageNumber], format, pxPerMm, look);
            var container = new ContainerVisual();
            container.Children.Add(page);
            if (portrait)
            {
                container.Transform = new MatrixTransform(0, 1, -1, 0, format.HeightMm * pxPerMm, 0);
            }
            return new DocumentPage(container, pageSize, new Rect(pageSize), new Rect(pageSize));
        }
    }
}
