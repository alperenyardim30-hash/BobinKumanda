using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Samples;

/// <summary>
/// Güç devresi, soldan sağa: L1-L2-L3 (yan yatırılmış faz kaynakları), sonra sırayla üç kutuplu elemanlar
/// (W otomatı, kontaktör ana kontakları, termik röle) ve en sonda yan yatırılmış motor. Kutuplar 25 birim aralıklıdır.
/// </summary>
internal sealed class PowerLine
{
    private readonly SampleBuilder b;
    private double x;

    public double Y { get; }

    /// <summary>Şu anki üç iletkenin uçları (L1, L2, L3 sırasıyla).</summary>
    public SampleBuilder.End[] Ends { get; private set; }

    public PowerLine(SampleBuilder b, double x, double y)
    {
        this.b = b;
        Y = y;
        string[] types = ["source-l1", "source-l2", "source-l3"];
        Ends = new SampleBuilder.End[3];
        for (int i = 0; i < 3; i++)
        {
            ComponentDef def = ComponentCatalog.Get(types[i]);
            ComponentInstance src = b.AddAt(types[i], def.DefaultLabel!, def.Terminals[0].Id, x, PoleY(i), rotation: 3);
            Ends[i] = (src, def.Terminals[0].Id);
        }
        this.x = x + 40;
    }

    public double PoleY(int i) => Y + 10 + 25 * i;

    public double X => x;

    /// <summary>Hat, araya elle kablolanan bir elemanın çıkışlarından devam eder.</summary>
    public void Continue(SampleBuilder.End[] ends, double nextX)
    {
        Ends = ends;
        x = nextX;
    }

    /// <summary>Üç kutuplu elemanı hatta ekler ve uçlarını bir sonrakine bağlanmak üzere ilerletir.</summary>
    public ComponentInstance Add(string type, string label, double gap = 40, params (string, string)[] parameters)
    {
        ComponentInstance c = b.AddAt(type, label, "1", x, PoleY(0), parameters: parameters);
        for (int i = 0; i < 3; i++)
        {
            b.W(Ends[i], (c, (2 * i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        Ends = [(c, "2"), (c, "4"), (c, "6")];
        x = SampleBuilder.Pos(c, "2").X + gap;
        return c;
    }

    /// <summary>
    /// Hattın şu anki uçlarına ek noktaları koyar (ikinci kol buradan ayrılır). Faz i'nin noktası x0 + dx·i'de: aşağı
    /// inen kolların dikey kabloları üst üste binmesin diye her faz ayrı x'te.
    /// </summary>
    public Junction[] Tap(double x0, double dx = -10)
    {
        var js = new Junction[3];
        for (int i = 0; i < 3; i++)
        {
            js[i] = b.J(x0 + dx * i, PoleY(i));
            b.W(Ends[i], js[i]);
            Ends[i] = js[i];
        }
        x = Math.Max(x, Math.Max(x0, x0 + 2 * dx) + 20);
        return js;
    }

    /// <summary>Ek noktalarından aşağı inen ikinci kol: üç kutuplu eleman (girişleri sırayla ek noktalarına bağlı).</summary>
    public ComponentInstance Branch(Junction[] taps, string type, string label, double atX, double dy, params (string, string)[] parameters)
    {
        ComponentInstance c = b.AddAt(type, label, "1", atX, PoleY(0) + dy, parameters: parameters);
        for (int i = 0; i < 3; i++)
        {
            string t = (2 * i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            b.W(taps[i], (c, t), new Vec(taps[i].Position.X, SampleBuilder.Pos(c, t).Y));
        }
        return c;
    }

    /// <summary>
    /// Üç fazlı motoru (aynalanıp yan yatırılmış: U üstte) hattın sonuna bağlar. Motor 3 uçlu ya da Dahlander/6 uçlu
    /// değilse <paramref name="type"/> motor-3ph olmalıdır.
    /// </summary>
    public ComponentInstance Motor(string label, string type = "motor-3ph", params (string, string)[] parameters)
    {
        ComponentInstance m = b.AddAt(type, label, "U", x + 20, PoleY(0), rotation: 3, mirror: true, parameters: parameters);
        string[] t = ["U", "V", "W"];
        for (int i = 0; i < 3; i++)
        {
            Vec from = SampleBuilder.Pos(Ends[i]), to = SampleBuilder.Pos(m, t[i]);
            double mid = x + 10 - 0;
            b.W(Ends[i], (m, t[i]), new Vec(mid, from.Y), new Vec(mid, to.Y));
        }
        return m;
    }
}
