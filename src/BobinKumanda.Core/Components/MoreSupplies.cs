using System.Globalization;
using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>
/// Kumanda trafosu: primer (1-2) 230/400 V, sekonder (3-4) 24 V AC (ya da seçilen). Sekonder, primer gerilimine bağlı
/// bir kaynaktır; primer de sekonderin çektiği akımı yansıtan bir empedanstır. İkisi her çözümde birbirine göre güncellenir.
/// Sekonder kısa devresi koruma yoksa trafoyu birkaç saniyede yakar.
/// </summary>
public sealed class TransformerDef : ComponentDef
{
    public override string TypeId => "transformer";
    public override ComponentCategory Category => ComponentCategory.Supply;
    public override string LabelPrefix => "T";
    public override Vec Size => new(40, 30);
    public override Vec DefaultLabelOffset => new(14, -10);
    public override string Tags => "kumanda trafosu transformatör trafo 24V 230V transformer";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 5), Side.Left, "1"),
        new TerminalDef("2", new Vec(0, 25), Side.Left, "2"),
        new TerminalDef("3", new Vec(40, 5), Side.Right, "3"),
        new TerminalDef("4", new Vec(40, 25), Side.Right, "4")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("primary", "230", "230", "400"),
        ParamDef.Choice("secondary", "24", "12", "24", "48", "110", "230"),
        ParamDef.Number("ratedVa", 63, "VA", 5, 5000)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0}/{1} V {2:0} VA", c.GetParam("primary", "230"), c.GetParam("secondary", "24"), c.GetDouble("ratedVa", 63));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool on = ctx.State?.Active == true;
        Paint winding = on ? Paint.Of(Ink.Active) : ink;
        p.Polyline([new Vec(0, 5), new Vec(10, 5), new Vec(10, 7)], ink);
        p.Polyline([new Vec(0, 25), new Vec(10, 25), new Vec(10, 23)], ink);
        p.Polyline([new Vec(40, 5), new Vec(30, 5), new Vec(30, 7)], ink);
        p.Polyline([new Vec(40, 25), new Vec(30, 25), new Vec(30, 23)], ink);
        // iki bobin yarım halkaları ve çekirdek
        for (int i = 0; i < 4; i++)
        {
            double y = 7 + 4 * i;
            SymbolKit.Arc(p, winding, new Vec(10, y + 2), 2, -90, 180, 1);
            SymbolKit.Arc(p, winding, new Vec(30, y + 2), 2, 90, 180, 1);
        }
        p.Line(new Vec(18, 4), new Vec(18, 26), ink, 1.1);
        p.Line(new Vec(22, 4), new Vec(22, 26), ink, 1.1);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new TransformerSim(instance.GetDouble("primary", 230), instance.GetDouble("secondary", 24), instance.GetDouble("ratedVa", 63));
}

internal sealed class TransformerSim(double primary, double secondary, double ratedVa) : SimComponent
{
    private const double Uk = 0.08;     // kısa devre gerilimi %8
    private const double NoLoadVa = 0.04; // boşta çektiği görünür güç (anma gücünün %4'ü)

    private readonly double ratio = secondary / Math.Max(1, primary);
    private readonly Complex zSecondary = Complex.FromPolarCoordinates(Uk * secondary * secondary / Math.Max(1, ratedVa), 1.2);
    private readonly Complex zMagnetizing = Complex.FromPolarCoordinates(primary * primary / (NoLoadVa * Math.Max(1, ratedVa)), 1.35);
    private Complex emf;
    private Complex zPrimary;
    private Complex vPrimary, iSecondary;
    private int pBranch = -1, sBranch = -1;
    private Thermal heat;
    private bool burned;

    private double RatedSecondaryAmps => ratedVa / Math.Max(1, secondary);

    public override bool NeedsTicks => iSecondary.Magnitude > 0.5 * RatedSecondaryAmps || heat.Theta > 0.05;

    public override void Stamp(Stamper s)
    {
        if (zPrimary == Complex.Zero)
        {
            zPrimary = zMagnetizing;
        }
        if (burned)
        {
            pBranch = s.Switch(T(0), T(1), false);
            sBranch = s.Switch(T(2), T(3), false);
            return;
        }
        pBranch = s.Impedance(T(0), T(1), zPrimary, double.PositiveInfinity);
        // sekonder: 3 (+) - 4; DC yolu yok (trafo DC geçirmez)
        sBranch = s.Source(T(2), T(3), emf, zSecondary, 0, 1e9, "T:" + Id);
    }

    public override bool Evaluate(Solution sol)
    {
        if (burned)
        {
            return false;
        }
        vPrimary = sol.BranchVoltageAc(pBranch);
        // kaynak dalı 4→3 yönünde; sekonderden yüke çıkan akım (3'ten dışarı)
        iSecondary = sol.CurrentAc(sBranch);
        Complex newEmf = vPrimary * ratio;
        // primer akımı = mıknatıslanma + sekonderin yansıması
        Complex ip = vPrimary / zMagnetizing + iSecondary * ratio;
        Complex newZ = vPrimary.Magnitude > 1 && ip.Magnitude > 1e-9 ? vPrimary / ip : zMagnetizing;
        bool changed = (newEmf - emf).Magnitude > 0.002 * secondary || (newZ - zPrimary).Magnitude > 0.01 * zPrimary.Magnitude;
        emf = newEmf;
        zPrimary = newZ;
        return changed;
    }

    public override bool Advance(double dt, Solution sol)
    {
        heat.Advance(iSecondary.Magnitude / RatedSecondaryAmps, dt, 60, 120);
        if (!burned && heat.Theta > 6)
        {
            burned = true;
            emf = Complex.Zero;
            Visual.Fault = true;
            Sim.ReportFault("transformer-burned", L("problem.transformerBurned", Label), L("explain.transformerBurned"), [Id], []);
            return true;
        }
        return false;
    }

    public override void Check(Solution sol)
    {
        if (!burned && iSecondary.Magnitude > 1.2 * RatedSecondaryAmps)
        {
            Sim.ReportLive(new SimProblem("transformer-overload", ProblemSeverity.Warning,
                L("problem.transformerOverload", Label, Simulator.FormatAmps(iSecondary.Magnitude), Simulator.FormatAmps(RatedSecondaryAmps)),
                L("explain.transformerOverload"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = !burned && vPrimary.Magnitude > 0.5 * primary;
        Visual.Fault = burned;
        Visual.Warning = iSecondary.Magnitude > 1.2 * RatedSecondaryAmps;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.primaryVoltage"), Simulator.FormatVolts(vPrimary.Magnitude));
        yield return new Readout(L("readout.secondaryVoltage"), Simulator.FormatVolts(sol.BranchVoltageAc(sBranch).Magnitude));
        yield return new Readout(L("readout.secondaryCurrent"), Simulator.FormatAmps(iSecondary.Magnitude));
        yield return new Readout(L("readout.rated"), Simulator.FormatAmps(RatedSecondaryAmps));
    }
}

/// <summary>
/// 24 V DC güç kaynağı (SMPS): L-N'den 230 V AC alır, + ve − uçlarından sabit DC verir. Aşırı yükte ya da kısa
/// devrede çıkışı kapatır ve 1 saniyede bir yeniden dener.
/// </summary>
public sealed class DcPowerSupplyDef : ComponentDef
{
    public override string TypeId => "psu-dc";
    public override ComponentCategory Category => ComponentCategory.Supply;
    public override string LabelPrefix => "G";
    public override Vec Size => new(40, 30);
    public override Vec DefaultLabelOffset => new(14, -10);
    public override string Tags => "dc güç kaynağı 24V dc adaptör smps doğrultucu power supply";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("L", new Vec(0, 8), Side.Left, "L"),
        new TerminalDef("N", new Vec(0, 22), Side.Left, "N"),
        new TerminalDef("+", new Vec(40, 8), Side.Right, "+"),
        new TerminalDef("-", new Vec(40, 22), Side.Right, "−")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("outputVoltage", "24", "5", "12", "24", "48"),
        ParamDef.Number("ratedCurrent", 2.5, "A", 0.1, 40)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0} V = {1:0.#} A", c.GetParam("outputVoltage", "24"), c.GetDouble("ratedCurrent", 2.5));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool on = ctx.State?.Active == true;
        p.Line(new Vec(0, 8), new Vec(6, 8), ink);
        p.Line(new Vec(0, 22), new Vec(6, 22), ink);
        p.Line(new Vec(34, 8), new Vec(40, 8), Paint.Color(0xC62828));
        p.Line(new Vec(34, 22), new Vec(40, 22), Paint.Color(0x1E64C8));
        p.Rect(new Vec(6, 2), new Vec(28, 26), ink, 1, on ? Paint.Of(Ink.Active, 0.12) : Paint.Of(Ink.Background));
        p.Line(new Vec(6, 28), new Vec(34, 2), ink, 0.7);
        p.Text(new Vec(13, 11), "~", 7, ink, bold: true);
        p.Text(new Vec(27, 25), "=", 7, ink, bold: true);
        p.Text(new Vec(31, 9.5), "+", 4, ink);
        p.Text(new Vec(31, 20), "−", 4, ink);
        p.Circle(new Vec(10, 24), 1.4, ink, 0.5, on ? Paint.Color(0x43A047) : Paint.Of(Ink.Background));
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new DcSupplySim(instance.GetDouble("outputVoltage", 24), instance.GetDouble("ratedCurrent", 2.5));
}

internal sealed class DcSupplySim(double outputVoltage, double ratedCurrent) : SimComponent
{
    private const double Efficiency = 0.88;
    private readonly double vOut = outputVoltage > 0 ? outputVoltage : 24;
    private int inBranch = -1, outBranch = -1;
    private double vIn, iOut;
    private bool inputOk;
    private double emf;
    private bool limiting;
    private double inputOhms = 230 * 230 / 2.0;

    private double Rated => Math.Max(0.05, ratedCurrent);

    /// <summary>Akım sınırı: anma akımının %120'si. Üstünde kaynak sabit akımda çalışır, gerilimi düşer.</summary>
    private double Limit => 1.2 * Rated;

    public override void Stamp(Stamper s)
    {
        inBranch = s.Impedance(T(0), T(1), new Complex(inputOhms, -0.15 * inputOhms), double.PositiveInfinity);
        // çıkış: + (2), − (3); AC yolu yok
        outBranch = s.Source(T(2), T(3), Complex.Zero, new Complex(1e9, 0), inputOk ? emf : 0, 0.02 * vOut / Rated, "G:" + Id);
    }

    public override bool Evaluate(Solution sol)
    {
        vIn = sol.BranchVoltageAc(inBranch).Magnitude;
        iOut = sol.CurrentDc(outBranch);
        bool changed = false;
        bool ok = vIn > 0.85 * 230 && vIn < 1.2 * 400;
        if (ok != inputOk)
        {
            inputOk = ok;
            emf = ok ? vOut : 0;
            changed = true;
        }
        if (inputOk)
        {
            // Sabit akım (CC) bölgesi: devre doğrusal olduğundan EMF'yi akım oranında küçültmek akımı tam sınıra getirir
            double amps = Math.Abs(iOut);
            double target = amps > Limit * 1.001 ? emf * Limit / amps
                : emf < vOut && amps < Limit * 0.98 ? (amps > 1e-6 ? Math.Min(vOut, emf * Limit / amps) : vOut)
                : emf;
            if (Math.Abs(target - emf) > 1e-4 * vOut)
            {
                emf = target;
                changed = true;
            }
            bool nowLimiting = emf < 0.97 * vOut;
            if (nowLimiting && !limiting)
            {
                Sim.AddLog(L("log.psuLimit", Label, Simulator.FormatAmps(Limit)), Id, ProblemSeverity.Warning);
            }
            limiting = nowLimiting;
        }
        // giriş gücü çıkış gücüne göre; boşta ~2 W
        double pIn = 2 + Math.Abs(emf * iOut) / Efficiency;
        double ohms = vIn > 1 ? Math.Max(10, vIn * vIn / pIn) : 230 * 230 / 2.0;
        if (Math.Abs(ohms - inputOhms) > 0.02 * inputOhms)
        {
            inputOhms = ohms;
            changed = true;
        }
        return changed;
    }

    public override void Check(Solution sol)
    {
        if (limiting)
        {
            Sim.ReportLive(new SimProblem("psu-overload", ProblemSeverity.Warning,
                L("problem.psuOverload", Label, Simulator.FormatAmps(Limit)), L("explain.psuOverload"), [Id], []));
        }
        else if (vIn > 30 && !inputOk)
        {
            Sim.ReportLive(new SimProblem("psu-input", ProblemSeverity.Warning,
                L("problem.psuInput", Label, Simulator.FormatVolts(vIn)), L("explain.psuInput"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = inputOk;
        Visual.Warning = limiting;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.inputVoltage"), Simulator.FormatVolts(vIn) + " AC");
        yield return new Readout(L("readout.outputVoltage"), Simulator.FormatVolts(sol.BranchVoltageDc(outBranch)) + " DC");
        yield return new Readout(L("readout.outputCurrent"), Simulator.FormatAmps(Math.Abs(iOut)));
        yield return new Readout(L("readout.rated"), Simulator.FormatAmps(Rated));
    }
}

/// <summary>Toprak (PE) barası: bütün klemensleri birbirine bağlı; gövdelerin toprak bağlantısını toplar.</summary>
public sealed class PeBarDef : ComponentDef
{
    public override string TypeId => "pe-bar";
    public override ComponentCategory Category => ComponentCategory.Misc;
    public override string LabelPrefix => "X";
    public override string? DefaultLabel => "PE";
    public override Vec Size => new(60, 10);
    public override Vec DefaultLabelOffset => new(62, 0);
    public override string Tags => "pe barası toprak barası topraklama bara earth bar";

    public override IReadOnlyList<TerminalDef> Terminals =>
        Enumerable.Range(0, 6).Select(i => new TerminalDef((i + 1).ToString(CultureInfo.InvariantCulture),
            new Vec(5 + 10 * i, 10), Side.Bottom)).ToArray();

    public override IReadOnlyList<(string A, string B)> NetLinks =>
        Enumerable.Range(2, 5).Select(i => ("1", i.ToString(CultureInfo.InvariantCulture))).ToArray();

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Paint pe = Paint.Color(SymbolKit.PhaseColor("PE"));
        p.Rect(new Vec(0, 1), new Vec(60, 5), ink, 1, Paint.Color(SymbolKit.PhaseColor("PE"), 0.35));
        for (int i = 0; i < 6; i++)
        {
            double x = 5 + 10 * i;
            p.Line(new Vec(x, 6), new Vec(x, 10), pe);
            SymbolKit.Terminal(p, ctx, new Vec(x, 3.5), 1.3);
        }
    }

    public override SimComponent? CreateSim(ComponentInstance instance) => null;
}

/// <summary>Klemens (sıra klemensi): iki ucu iç bağlı; pano içi ile dışarıdaki cihazlar arasındaki bağlantı noktası.</summary>
public sealed class TerminalBlockDef : ComponentDef
{
    public override string TypeId => "terminal";
    public override ComponentCategory Category => ComponentCategory.Misc;
    public override string LabelPrefix => "X";
    public override Vec Size => new(20, 20);
    public override Vec DefaultLabelOffset => new(4, -9);
    public override string Tags => "klemens sıra klemensi bağlantı klemensi terminal block";

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 10), Side.Left), new TerminalDef("2", new Vec(20, 10), Side.Right)];

    public override IReadOnlyList<(string A, string B)> NetLinks => [("1", "2")];

    public override IReadOnlyList<ParamDef> Params => [ParamDef.Integer("pin", 1, 1, 999)];

    public override string? SubLabel(ComponentInstance c) => ":" + c.GetInt("pin", 1).ToString(CultureInfo.InvariantCulture);

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Line(new Vec(0, 10), new Vec(6.5, 10), ink);
        p.Line(new Vec(13.5, 10), new Vec(20, 10), ink);
        p.Circle(new Vec(10, 10), 3.5, ink, 1, Paint.Of(Ink.Background));
        p.Line(new Vec(7.5, 12.5), new Vec(12.5, 7.5), ink, 0.8);
    }

    public override SimComponent? CreateSim(ComponentInstance instance) => null;
}

/// <summary>Sayfa bağlantı oku: aynı adı taşıyan bütün oklar (başka sayfalarda bile) aynı iletkendir.</summary>
public sealed class SheetLinkDef : ComponentDef
{
    public override string TypeId => "sheet-link";
    public override ComponentCategory Category => ComponentCategory.Misc;
    public override string LabelPrefix => "";
    public override bool DrawsOwnLabel => true;
    public override Vec Size => new(30, 10);
    public override Vec DefaultLabelOffset => new(0, -9);
    public override string Tags => "sayfa bağlantısı sayfa oku potansiyel oku çapraz bağlantı sheet link off page connector";
    public override bool LinksByLabel => true;

    public override IReadOnlyList<TerminalDef> Terminals => [new TerminalDef("1", new Vec(0, 5), Side.Left)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Polyline([new Vec(0, 5), new Vec(6, 5)], ink);
        p.Polyline([new Vec(6, 0.5), new Vec(24, 0.5), new Vec(29.5, 5), new Vec(24, 9.5), new Vec(6, 9.5)], ink, 1, closed: true,
            fill: Paint.Of(Ink.Background));
        p.Text(new Vec(16, 7), ctx.Instance.Label, 5, ink, bold: true);
    }

    public override SimComponent? CreateSim(ComponentInstance instance) => null;
}
