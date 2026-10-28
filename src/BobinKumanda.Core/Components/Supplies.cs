using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>230/400 V TN-S şebeke modeli. Yıldız noktası referans (toprak) düğümüdür.</summary>
public static class Grid
{
    public const double PhaseVoltage = 230;
    public static readonly Complex Zphase = new(0.08, 0.04);
    public static readonly Complex Zneutral = new(0.08, 0.04);
    public static readonly Complex Zearth = new(0.15, 0.05);

    public static Complex Emf(string phase) => phase switch
    {
        "L1" => Complex.FromPolarCoordinates(PhaseVoltage, 0),
        "L2" => Complex.FromPolarCoordinates(PhaseVoltage, -2 * Math.PI / 3),
        "L3" => Complex.FromPolarCoordinates(PhaseVoltage, 2 * Math.PI / 3),
        _ => Complex.Zero
    };

    public static int StampTerminal(Stamper s, Pin pin, string phase)
    {
        Complex z = phase switch { "N" => Zneutral, "PE" => Zearth, _ => Zphase };
        return s.Source(pin, Pin.Ground, Emf(phase), z, 0, z.Real, phase);
    }
}

/// <summary>Tek klemensli şebeke beslemesi (eski EKTS'deki R, S, T, N).</summary>
public abstract class PhaseSourceDef : ComponentDef
{
    protected abstract string Phase { get; }

    public override ComponentCategory Category => ComponentCategory.Supply;
    public override string LabelPrefix => Phase;
    public override string? DefaultLabel => Phase;
    public override Vec Size => new(10, 30);
    public override IReadOnlyList<TerminalDef> Terminals => [new TerminalDef(Phase, new Vec(5, 30), Side.Bottom)];
    public override Vec DefaultLabelOffset => new(12, -2);

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Circle(new Vec(5, 9), 3, ink, 1, Paint.Color(SymbolKit.PhaseColor(Phase)));
        p.Line(new Vec(5, 12), new Vec(5, 30), ink);
        // yan yatırılınca yazı dairenin yanına gelir; genişliği yüksekliğinden fazla olduğu için daha uzağa yazılır
        double gap = (ctx.Instance.Rotation & 1) == 1 ? 2.5 + 2 * Phase.Length : 2.5;
        p.Text(new Vec(5, 5 - gap), Phase, 6.5, ink, bold: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new PhaseSourceSim(Phase);
}

public sealed class SourceL1Def : PhaseSourceDef
{
    public override string TypeId => "source-l1";
    protected override string Phase => "L1";
    public override int? LegacyType => 11;
    public override string Tags => "faz R kaynak besleme phase supply";
}

public sealed class SourceL2Def : PhaseSourceDef
{
    public override string TypeId => "source-l2";
    protected override string Phase => "L2";
    public override int? LegacyType => 12;
    public override string Tags => "faz S kaynak besleme phase supply";
}

public sealed class SourceL3Def : PhaseSourceDef
{
    public override string TypeId => "source-l3";
    protected override string Phase => "L3";
    public override int? LegacyType => 13;
    public override string Tags => "faz T kaynak besleme phase supply";
}

public sealed class SourceNDef : PhaseSourceDef
{
    public override string TypeId => "source-n";
    protected override string Phase => "N";
    public override int? LegacyType => 14;
    public override string Tags => "nötr sıfır neutral";
}

public sealed class SourcePeDef : PhaseSourceDef
{
    public override string TypeId => "source-pe";
    protected override string Phase => "PE";
    public override string Tags => "toprak topraklama koruma iletkeni earth ground";
}

internal sealed class PhaseSourceSim(string phase) : SimComponent
{
    public override void Stamp(Stamper s) => Grid.StampTerminal(s, T(0), phase);

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(Loc.T("readout.voltageToEarth"), Simulator.FormatVolts(sol.NodeAc(TerminalNodes[0]).Magnitude));
    }
}

/// <summary>Üç fazlı besleme (L1 L2 L3), isteğe bağlı N ve PE ile.</summary>
public class ThreePhaseSourceDef : ComponentDef
{
    public override string TypeId => "source-3ph";
    public override ComponentCategory Category => ComponentCategory.Supply;
    public override string LabelPrefix => "X";
    public override string? DefaultLabel => "3~";
    public override int? LegacyType => 17;
    public override string Tags => "üç faz 3 faz rst 400V three phase supply";
    protected virtual string[] Phases => ["L1", "L2", "L3"];
    public override Vec Size => new(Phases.Length * 10, 30);
    public override Vec DefaultLabelOffset => new(Phases.Length * 10 + 3, -2);

    public override IReadOnlyList<TerminalDef> Terminals =>
        Phases.Select((ph, i) => new TerminalDef(ph, new Vec(5 + 10 * i, 30), Side.Bottom)).ToArray();

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        string[] phases = Phases;
        for (int i = 0; i < phases.Length; i++)
        {
            double x = 5 + 10 * i;
            p.Circle(new Vec(x, 9), 2.6, ink, 1, Paint.Color(SymbolKit.PhaseColor(phases[i])));
            p.Line(new Vec(x, 11.6), new Vec(x, 30), ink);
            p.Text(new Vec(x, 2.5), phases[i], 5, ink, bold: true);
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new MultiSourceSim(Phases);
}

public sealed class ThreePhaseNeutralSourceDef : ThreePhaseSourceDef
{
    public override string TypeId => "source-3phnpe";
    public override int? LegacyType => null;
    public override string Tags => "üç faz nötr toprak 5 iletken TN-S supply";
    protected override string[] Phases => ["L1", "L2", "L3", "N", "PE"];
}

internal sealed class MultiSourceSim(string[] phases) : SimComponent
{
    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < phases.Length; i++)
        {
            Grid.StampTerminal(s, T(i), phases[i]);
        }
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        if (phases.Length >= 2)
        {
            yield return new Readout(Loc.T("readout.lineVoltage"),
                Simulator.FormatVolts(sol.VoltageAc(TerminalNodes[0], TerminalNodes[1]).Magnitude));
        }
    }
}
