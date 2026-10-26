using System.Globalization;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Samples;

/// <summary>Hazır örnek devre: adı ve kısa açıklaması dile göre, devre her açılışta kodla yeniden kurulur.</summary>
public sealed record SampleInfo(string Id, Func<Circuit> Build)
{
    public string Title => Loc.T("sample." + Id);
    public string Description => Loc.T("sample." + Id + ".info");
}

/// <summary>
/// Örnek devreler kütüphanesi: ders kitaplarındaki temel kumanda devreleri. Dosya değil kod olarak tutulur; böylece
/// her sürümde güncel elemanlarla açılır, iki dilde adlandırılır ve otomatik testlerle çalıştıkları doğrulanır.
/// </summary>
public static class SampleLibrary
{
    public static IReadOnlyList<SampleInfo> All { get; } =
    [
        new("button-lamp", ButtonLamp),
        new("latching", Latching),
        new("dol", DirectOnLine),
        new("jog", JogContinuous),
        new("reversing", Reversing),
        new("star-delta", StarDelta),
        new("sequence", Sequence),
        new("pump", FloatPump),
        new("rcd", ResidualCurrent),
        new("control-24v", Control24V),
        new("thermostat", Thermostat),
        new("counter", Counter),
        new("alarm", Alarm),
        new("sensor", ProximitySensor)
    ];

    public static SampleInfo? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    private static string N(int i) => i.ToString(CultureInfo.InvariantCulture);

    private static Circuit Finish(SampleBuilder b, string id, double noteX, double noteY)
    {
        string[] lines = Loc.T("sample." + id + ".note").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            b.Text(noteX, noteY + 11 * i, lines[i]);
        }
        b.Circuit.Title.CircuitName = Loc.T("sample." + id);
        b.Circuit.RecalculateNextId();
        return b.Circuit;
    }

    /// <summary>Butonla lamba: basılı tutulduğu sürece yanar (kesik çalışma).</summary>
    private static Circuit ButtonLamp()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 300, 0);
        lad.Rung(60).Add("button-no", "S1").End("lamp", "H1");
        lad.Build();
        return Finish(b, "button-lamp", 40, 100);
    }

    /// <summary>Mühürleme: S1 ile çeker, K1 kendi kontağıyla kendini tutar, S0 ile durur.</summary>
    private static Circuit Latching()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 360, 0);
        lad.Rung(60).Add("button-nc", "S0").Add("button-no", "S1").End("relay-coil", "K1").Parallel(1, 2, 40, ("contact-no", "K1"));
        lad.Rung(140).Add("contact-no", "K1").End("lamp", "H1", parameters: ("lampColor", "green"));
        lad.Build();
        return Finish(b, "latching", 40, 180);
    }

    /// <summary>Termik korumalı direkt yol verme: kumanda (stop-start-mühürleme, termik kontağı) ve güç devresi.</summary>
    private static Circuit DirectOnLine()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 400, 0);
        lad.Rung(60).Add("overload-nc", "F2").Add("button-nc", "S0").Add("button-no", "S1")
            .End("relay-coil", "K1").Parallel(2, 3, 40, ("contact-no", "K1"));
        lad.Rung(140).Add("contact-no", "K1").End("lamp", "H1", parameters: ("lampColor", "green"));
        lad.Rung(190).Add("overload-no", "F2").End("lamp", "H2", parameters: ("lampColor", "red"));
        lad.Build();
        var p = new PowerLine(b, 470, 20);
        p.Add("mcb-3p", "Q1");
        p.Add("contact-3no", "K1");
        p.Add("overload", "F2", parameters: ("setCurrent", "3.2"));
        p.Motor("M1");
        return Finish(b, "dol", 40, 230);
    }

    /// <summary>Kesik ve sürekli çalışma: S1 mühürlü (sürekli), S2 basılı tutuldukça (kesik); S2'nin NC kontağı mühürü keser.</summary>
    private static Circuit JogContinuous()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 420, 0);
        lad.Rung(60).Add("button-nc", "S0").Add("button-no", "S1").End("relay-coil", "K1")
            .Parallel(1, 2, 40, ("contact-no", "K1"), ("contact-nc", "S2"))
            .Parallel(1, 2, 80, ("button-no", "S2"));
        lad.Rung(200).Add("contact-no", "K1").End("lamp", "H1", parameters: ("lampColor", "green"));
        lad.Build();
        return Finish(b, "jog", 40, 240);
    }

    /// <summary>İleri-geri: buton ve kontaktör kilitlemeli; K2 L1 ile L3'ün yerini değiştirir.</summary>
    private static Circuit Reversing()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 480, 0);
        Rung r1 = lad.Rung(60).Add("overload-nc", "F2").Add("button-nc", "S0").Add("button-no", "S1")
            .Add("contact-nc", "K2").End("relay-coil", "K1").Parallel(2, 3, 40, ("contact-no", "K1"));
        lad.Rung(140).From(r1, 2).Add("button-no", "S2").Add("contact-nc", "K1").End("relay-coil", "K2")
            .Parallel(0, 1, 40, ("contact-no", "K2"));
        lad.Build();

        var p = new PowerLine(b, 560, 20);
        p.Add("mcb-3p", "Q1", gap: 70);
        Junction[] taps = p.Tap(p.X - 30);
        var k1 = p.Add("contact-3no", "K1", gap: 70);
        Junction[] outs = p.Tap(p.X - 50, 10);
        var k2 = p.Branch(taps, "contact-3no", "K2", SampleBuilder.Pos(k1, "1").X, 120);
        // K2 çıkışında L1 ile L3 yer değiştirir: 2 → üçüncü hat, 6 → birinci hat
        int[] line = [2, 1, 0];
        for (int i = 0; i < 3; i++)
        {
            Junction j = outs[line[i]];
            string t = N(2 * i + 2);
            b.W((k2, t), j, new Vec(j.Position.X, SampleBuilder.Pos(k2, t).Y));
        }
        p.Add("overload", "F2", parameters: ("setCurrent", "3.2"));
        p.Motor("M1");
        return Finish(b, "reversing", 40, 190);
    }

    /// <summary>Yıldız-üçgen yol verme: KM ana, KY yıldız, KD üçgen kontaktörü; KT zaman rölesi yıldızdan üçgene geçirir.</summary>
    private static Circuit StarDelta()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 560, 0);
        Rung r1 = lad.Rung(60).Add("overload-nc", "F2").Add("button-nc", "S0").Add("button-no", "S1")
            .End("relay-coil", "KM").Parallel(2, 3, 40, ("contact-no", "KM"));
        lad.Rung(140).From(r1, 3).End("timer-on", "KT", parameters: ("time", "4"));
        lad.Rung(190).From(r1, 3).Add("contact-nc", "KT").Add("contact-nc", "KD").End("relay-coil", "KY");
        lad.Rung(240).From(r1, 3).Add("contact-no", "KT").Add("contact-nc", "KY").End("relay-coil", "KD");
        lad.Build();

        var p = new PowerLine(b, 640, 20);
        p.Add("mcb-3p", "Q1");
        p.Add("contact-3no", "KM");
        p.Add("overload", "F2", gap: 70, parameters: ("setCurrent", "4.9"));
        Junction[] taps = p.Tap(p.X - 50, 10);
        // Motor 6 uçlu, yan yatırılmış: solda U1 V1 W1, sağda W2 U2 V2
        var m = b.AddAt("motor-3ph-6t", "M1", "U1", p.X + 20, p.PoleY(0), rotation: 3, mirror: true);
        string[] first = ["U1", "V1", "W1"], second = ["W2", "U2", "V2"];
        for (int i = 0; i < 3; i++)
        {
            Vec to = SampleBuilder.Pos(m, first[i]);
            b.W(taps[i], (m, first[i]), new Vec(p.X + 10, taps[i].Position.Y), new Vec(p.X + 10, to.Y));
        }
        // KD: girişleri ana hattan, çıkışları W2-U2-V2'ye (U1-W2, V1-U2, W1-V2)
        var kd = p.Branch(taps, "contact-3no", "KD", p.X - 10, 130);
        double right = SampleBuilder.Pos(m, "W2").X;
        for (int i = 0; i < 3; i++)
        {
            Vec from = SampleBuilder.Pos(kd, N(2 * i + 2)), to = SampleBuilder.Pos(m, second[i]);
            double x = right + 20 + 10 * (2 - i);
            b.W((kd, N(2 * i + 2)), (m, second[i]), new Vec(x, from.Y), new Vec(x, to.Y));
        }
        // KY: W2-U2-V2'yi kısa devre ederek yıldız noktası yapar
        var ky = b.AddAt("contact-3no", "KY", "1", right + 80, p.PoleY(0));
        for (int i = 0; i < 3; i++)
        {
            Vec to = SampleBuilder.Pos(ky, N(2 * i + 1));
            b.W((m, second[i]), (ky, N(2 * i + 1)), new Vec(right + 60, SampleBuilder.Pos(m, second[i]).Y), new Vec(right + 60, to.Y));
        }
        b.W((ky, "2"), (ky, "4"), new Vec(SampleBuilder.Pos(ky, "2").X + 10, SampleBuilder.Pos(ky, "2").Y),
            new Vec(SampleBuilder.Pos(ky, "2").X + 10, SampleBuilder.Pos(ky, "4").Y));
        b.W((ky, "4"), (ky, "6"), new Vec(SampleBuilder.Pos(ky, "4").X + 10, SampleBuilder.Pos(ky, "4").Y),
            new Vec(SampleBuilder.Pos(ky, "4").X + 10, SampleBuilder.Pos(ky, "6").Y));
        return Finish(b, "star-delta", 40, 290);
    }

    /// <summary>Sıralı çalışma: M2, ancak M1 çalışıyorsa çalıştırılabilir; S0 ikisini de durdurur.</summary>
    private static Circuit Sequence()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 460, 0);
        Rung r1 = lad.Rung(60).Add("button-nc", "S0").Add("button-no", "S1").End("relay-coil", "K1").Parallel(1, 2, 40, ("contact-no", "K1"));
        lad.Rung(140).From(r1, 1).Add("contact-no", "K1").Add("button-no", "S2").End("relay-coil", "K2")
            .Parallel(1, 2, 40, ("contact-no", "K2"));
        lad.Build();

        var p = new PowerLine(b, 540, 20);
        p.Add("mcb-3p", "Q1", gap: 70);
        Junction[] taps = p.Tap(p.X - 30);
        p.Add("contact-3no", "K1");
        p.Motor("M1", parameters: ("powerKw", "1.5"));
        var k2 = p.Branch(taps, "contact-3no", "K2", SampleBuilder.Pos(taps[0]).X + 30, 120);
        var m2 = b.AddAt("motor-3ph", "M2", "U", SampleBuilder.Pos(k2, "2").X + 60, SampleBuilder.Pos(k2, "2").Y, rotation: 3, mirror: true);
        string[] t = ["U", "V", "W"];
        for (int i = 0; i < 3; i++)
        {
            Vec from = SampleBuilder.Pos(k2, N(2 * i + 2)), to = SampleBuilder.Pos(m2, t[i]);
            b.W((k2, N(2 * i + 2)), (m2, t[i]), new Vec(from.X + 30, from.Y), new Vec(from.X + 30, to.Y));
        }
        return Finish(b, "sequence", 40, 190);
    }

    /// <summary>Şamandıralı otomatik pompa: depo dolunca pompa çalışır, alt seviyeye inince durur.</summary>
    private static Circuit FloatPump()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 420, 0);
        lad.Rung(60).Add("overload-nc", "F2").Add("button-nc", "S0").Add("float-switch", "B1", "C", "NO")
            .End("relay-coil", "K1");
        lad.Rung(120).Add("contact-no", "K1").End("lamp", "H1", parameters: ("lampColor", "green"));
        lad.Build();
        var p = new PowerLine(b, 490, 20);
        p.Add("mcb-3p", "Q1");
        p.Add("contact-3no", "K1");
        p.Add("overload", "F2", parameters: ("setCurrent", "1.6"));
        p.Motor("M1", parameters: ("powerKw", "0.75"));
        return Finish(b, "pump", 40, 170);
    }

    /// <summary>Kaçak akım rölesi: gövde kaçağı olan motor PE'ye bağlıyken röle açar; PE'siz gövdede dokunma gerilimi.</summary>
    private static Circuit ResidualCurrent()
    {
        var b = new SampleBuilder();
        var p = new PowerLine(b, 40, 20);
        var n = b.AddAt("source-n", "N", "N", 40, p.PoleY(3), rotation: 3);
        var pe = b.AddAt("source-pe", "PE", "PE", 40, p.PoleY(4) + 10, rotation: 3);
        var rcd = b.AddAt("rcd-4p", "F1", "1", 80, p.PoleY(0), parameters: ("sensitivity", "30"));
        for (int i = 0; i < 3; i++)
        {
            b.W(p.Ends[i], (rcd, N(2 * i + 1)));
        }
        b.W((n, "N"), (rcd, "7"));
        p.Continue([(rcd, "2"), (rcd, "4"), (rcd, "6")], SampleBuilder.Pos(rcd, "2").X + 40);
        p.Add("mcb-3p", "Q1");
        var m = p.Motor("M1");
        Vec mpe = SampleBuilder.Pos(m, "PE");
        b.W((pe, "PE"), (m, "PE"), new Vec(mpe.X, SampleBuilder.Pos(pe, "PE").Y));
        return Finish(b, "rcd", 40, 200);
    }

    /// <summary>24 V kumanda: trafo sekonderinden sigortalı kumanda hattı, 24 V AC bobinli kontaktör ve 24 V lamba.</summary>
    private static Circuit Control24V()
    {
        var b = new SampleBuilder();
        var l1 = b.AddAt("source-l1", "L1", "L1", 40, 30);
        var n = b.AddAt("source-n", "N", "N", 20, 30);
        var f1 = b.AddAt("mcb-1p", "F1", "1", 40, 60, rotation: 1, parameters: ("ratedCurrent", "2"));
        var t1 = b.AddAt("transformer", "T1", "1", 80, 110);
        b.W((l1, "L1"), (f1, "1"));
        b.W((f1, "2"), (t1, "1"), new Vec(40, 110));
        b.W((n, "N"), (t1, "2"), new Vec(20, SampleBuilder.Pos(t1, "2").Y));
        var f2 = b.AddAt("fuse-1p", "F2", "1", 150, 110, parameters: ("ratedCurrent", "4"));
        b.W((t1, "3"), (f2, "1"));
        // 0 V hattı aşağıdan dolaşır, 24 V hattıyla kesişmesin
        Vec t4 = SampleBuilder.Pos(t1, "4");
        Junction zero = b.J(460, 300);
        b.W((t1, "4"), zero, new Vec(t4.X + 10, t4.Y), new Vec(t4.X + 10, 300));
        var lad = new Ladder(b, 200, 460, (f2, "2"), zero);
        lad.Rung(170).Add("button-nc", "S0").Add("button-no", "S1").End("relay-coil", "K1", parameters: ("coilVoltage", "24AC"))
            .Parallel(1, 2, 40, ("contact-no", "K1"));
        lad.Rung(250).Add("contact-no", "K1").End("lamp", "H1", parameters: [("lampColor", "green"), ("ratedVoltage", "24")]);
        lad.Build();
        return Finish(b, "control-24v", 40, 330);
    }

    /// <summary>Termostatla ısıtıcı: sıcaklık ayarın altına inince K1 çeker, ısıtıcı ve fan çalışır.</summary>
    private static Circuit Thermostat()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 400, 0);
        lad.Rung(60).Add("mcb-1p", "F1", parameters: ("ratedCurrent", "16")).Add("thermostat", "B1", "C", "NO", ("setPoint", "40")).End("relay-coil", "K1");
        lad.Rung(120).Add("contact-no", "K1").End("heater", "E1", parameters: ("power", "2000"));
        lad.Rung(170).Add("contact-no", "K1").End("fan", "M1");
        lad.Build();
        return Finish(b, "thermostat", 40, 210);
    }

    /// <summary>Sayıcı: S1'e her basışta bir sayar; 5'e ulaşınca lamba yanar. S2 sıfırlar.</summary>
    private static Circuit Counter()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 400, 0);
        lad.Rung(60).Add("button-no", "S1").End("counter", "K1", "A1", "A2", ("preset", "5"));
        lad.Rung(170).Add("contact-no", "K1").End("lamp", "H1", parameters: ("lampColor", "green"));
        // Sıfırlama girişi R1-R2 sayıcının altında: S2 ile L1'den, R2 N hattına
        var k1 = b.Circuit.Components.Single(c => c.Type == "counter");
        Vec r1 = SampleBuilder.Pos(k1, "R1"), r2 = SampleBuilder.Pos(k1, "R2");
        var s2 = b.AddAt("button-no", "S2", "1", 70, 120);
        lad.Tap(true, 120, (s2, "1"));
        lad.Tap(false, r2.Y, (k1, "R2"));
        b.W((s2, "2"), (k1, "R1"), new Vec(r1.X - 20, 120), new Vec(r1.X - 20, r1.Y));
        lad.Build();
        return Finish(b, "counter", 40, 210);
    }

    /// <summary>Flaşörlü ikaz: S1 ile alarm devreye girer; lamba yanıp söner, siren öter; S0 susturur.</summary>
    private static Circuit Alarm()
    {
        var b = new SampleBuilder();
        var lad = new Ladder(b, 40, 400, 0);
        lad.Rung(60).Add("button-nc", "S0").Add("button-no", "S1").End("relay-coil", "K1").Parallel(1, 2, 40, ("contact-no", "K1"));
        lad.Rung(140).Add("contact-no", "K1").End("flasher", "K2", parameters: [("onTime", "0.5"), ("offTime", "0.5")]);
        lad.Rung(190).Add("contact-no", "K2").End("lamp", "H1", parameters: ("lampColor", "red"));
        lad.Rung(240).Add("contact-no", "K1").End("horn", "P1", parameters: ("soundKind", "siren"));
        lad.Build();
        return Finish(b, "alarm", 40, 280);
    }

    /// <summary>Endüktif sensör: 24 V DC güç kaynağı sensörü besler, PNP çıkışı 24 V DC röleyi sürer; röle lambayı yakar.</summary>
    private static Circuit ProximitySensor()
    {
        var b = new SampleBuilder();
        var l1 = b.AddAt("source-l1", "L1", "L1", 40, 30);
        var n = b.AddAt("source-n", "N", "N", 20, 30);
        var g1 = b.AddAt("psu-dc", "G1", "L", 100, 70);
        b.W((l1, "L1"), (g1, "L"), new Vec(40, 70));
        b.W((n, "N"), (g1, "N"), new Vec(20, SampleBuilder.Pos(g1, "N").Y));
        // 0 V hattı aşağıdan dolaşır
        Vec minus = SampleBuilder.Pos(g1, "-");
        Junction zero = b.J(460, 300);
        b.W((g1, "-"), zero, new Vec(minus.X + 10, minus.Y), new Vec(minus.X + 10, 300));
        var lad = new Ladder(b, 200, 460, (g1, "+"), zero);
        // Sensör aynalı: kabloları sağda (BN +, BK çıkış, BU 0 V)
        var b1 = b.AddAt("prox-inductive", "B1", "BN", 320, 130, mirror: true);
        Vec bn = SampleBuilder.Pos(b1, "BN"), bk = SampleBuilder.Pos(b1, "BK"), bu = SampleBuilder.Pos(b1, "BU");
        Junction plus = b.J(200, 110);
        lad.Tap(true, 110, plus);
        b.W(plus, (b1, "BN"), new Vec(340, 110), new Vec(340, bn.Y));
        var k1 = b.AddAt("relay-coil", "K1", "A1", 380, bk.Y, parameters: ("coilVoltage", "24DC"));
        b.W((b1, "BK"), (k1, "A1"));
        lad.Tap(false, bk.Y, (k1, "A2"));
        Junction z1 = b.J(460, bu.Y + 30);
        lad.Tap(false, bu.Y + 30, z1);
        b.W((b1, "BU"), z1, new Vec(350, bu.Y), new Vec(350, bu.Y + 30));
        lad.Rung(240).Add("contact-no", "K1").End("lamp", "H1", parameters: [("lampColor", "yellow"), ("ratedVoltage", "24")]);
        lad.Build();
        return Finish(b, "sensor", 40, 320);
    }
}
