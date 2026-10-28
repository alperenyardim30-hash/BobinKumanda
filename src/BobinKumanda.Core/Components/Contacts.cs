using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Bobinin (veya başka bir ana cihazın) etiketiyle çalışan kontaklar.</summary>
public abstract class FollowerContactDef : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Contacts;
    public override DeviceRole Role => DeviceRole.Follower;
    public override string LabelPrefix => "K";
    public override Vec DefaultLabelOffset => new(4, -12);

    public override IReadOnlyList<ParamDef> Params =>
        [ParamDef.Choice("channel", "auto", "auto", "instant", "timed")];

    /// <summary>Zaman gecikmeli kontağın üstünde gecikme işareti vardır; etiket onun da üstüne çıkar.</summary>
    protected override Vec DefaultLabelOffsetFor(ComponentInstance c, Circuit? circuit)
    {
        Vec offset = base.DefaultLabelOffsetFor(c, circuit);
        return (c.Rotation & 1) == 0 && TimedFor(circuit, c) != TimedMark.None ? offset + new Vec(0, -6) : offset;
    }

    protected abstract ContactSpec[] Specs { get; }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new FollowerContactSim(Specs, instance.GetParam("channel", "auto"));

    /// <summary>Bağlı olduğu ana cihaz zaman rölesiyse gecikme işareti.</summary>
    protected static TimedMark TimedFor(DrawContext ctx) => TimedFor(ctx.Circuit, ctx.Instance);

    internal static TimedMark TimedFor(Circuit? circuit, ComponentInstance instance)
    {
        string channel = instance.GetParam("channel", "auto");
        if (channel == "instant" || circuit == null)
        {
            return TimedMark.None;
        }
        string label = instance.Label.Trim();
        if (label.Length == 0)
        {
            return TimedMark.None;
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            if (string.Equals(c.Label.Trim(), label, StringComparison.OrdinalIgnoreCase) && c != instance)
            {
                ComponentDef? def = ComponentCatalog.Find(c.Type);
                if (def is TimerOnDef)
                {
                    return TimedMark.OnDelay;
                }
                if (def is TimerOffDef)
                {
                    return TimedMark.OffDelay;
                }
                if (def?.Role == DeviceRole.Master)
                {
                    return channel == "timed" ? TimedMark.OnDelay : TimedMark.None;
                }
            }
        }
        return channel == "timed" ? TimedMark.OnDelay : TimedMark.None;
    }
}

/// <summary>
/// IEC 60947-5-1 yardımcı kontak numarası: ilk rakam sıra (aynı cihazın kaçıncı yardımcı kontağı), ikincisi işlev:
/// 1-2 NC, 3-4 NO; zaman gecikmeli kontaklarda 5-6 NC, 7-8 NO.
/// </summary>
public abstract class AuxContactDefBase : FollowerContactDef
{
    /// <summary>Klemens sırası → işlev rakamı (NC: 1, 2; NO: 3, 4; değiştirici: 1, 2, 4).</summary>
    protected abstract int[] FunctionDigits { get; }

    /// <summary>Kontak türü: NC, NO ya da (ikisi birden) değiştirici.</summary>
    internal (bool Nc, bool No) Kind => (FunctionDigits.Contains(1), FunctionDigits.Contains(4));

    public override bool AutoNumbered => true;

    public override string? NumberFor(TerminalDef t, ContactNumber n)
    {
        int index = -1;
        for (int i = 0; i < Terminals.Count; i++)
        {
            if (Terminals[i].Id == t.Id)
            {
                index = i;
            }
        }
        if (index < 0)
        {
            return t.Number;
        }
        int digit = FunctionDigits[index];
        if (n.Timed)
        {
            digit += 4;
        }
        return (n.Sequence % 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + digit.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class ContactNoDef : AuxContactDefBase
{
    protected override int[] FunctionDigits => [3, 4];

    public override string TypeId => "contact-no";
    public override int? LegacyType => 3;
    public override char? Shortcut => 'A';
    public override string Tags => "açık kontak normalde açık NO 13 14 yardımcı kontak";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 10), Side.Left, "13"), new TerminalDef("2", new Vec(20, 10), Side.Right, "14")];

    protected override ContactSpec[] Specs => [new ContactSpec(0, 1, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx) =>
        SymbolKit.ContactH(p, ctx, 10, false, ctx.IsClosed(0, false), timed: TimedFor(ctx));
}

public sealed class ContactNcDef : AuxContactDefBase
{
    protected override int[] FunctionDigits => [1, 2];

    public override string TypeId => "contact-nc";
    public override int? LegacyType => 4;
    public override char? Shortcut => 'C';
    public override string Tags => "kapalı kontak normalde kapalı NC 21 22 yardımcı kontak";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 10), Side.Left, "21"), new TerminalDef("2", new Vec(20, 10), Side.Right, "22")];

    protected override ContactSpec[] Specs => [new ContactSpec(0, 1, true)];

    public override void Draw(ISymbolPainter p, DrawContext ctx) =>
        SymbolKit.ContactH(p, ctx, 10, true, ctx.IsClosed(0, true), timed: TimedFor(ctx));
}

/// <summary>Kontaktör ana kontakları (3 kutup), güç devresi için.</summary>
public abstract class ThreePoleContactDef : FollowerContactDef
{
    protected abstract bool Nc { get; }
    public override Vec Size => new(20, 70);
    public override Vec DefaultLabelOffset => new(2, -13);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "1"), new TerminalDef("2", new Vec(20, 10), Side.Right, "2"),
        new TerminalDef("3", new Vec(0, 35), Side.Left, "3"), new TerminalDef("4", new Vec(20, 35), Side.Right, "4"),
        new TerminalDef("5", new Vec(0, 60), Side.Left, "5"), new TerminalDef("6", new Vec(20, 60), Side.Right, "6")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, Nc), new(2, 3, Nc), new(4, 5, Nc)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Vec m1 = SymbolKit.ContactH(p, ctx, 10, Nc, ctx.IsClosed(0, Nc));
        SymbolKit.ContactH(p, ctx, 35, Nc, ctx.IsClosed(1, Nc));
        Vec m3 = SymbolKit.ContactH(p, ctx, 60, Nc, ctx.IsClosed(2, Nc));
        SymbolKit.MechanicalLink(p, ctx, m1, m3);
    }
}

public sealed class ContactThreeNoDef : ThreePoleContactDef
{
    public override string TypeId => "contact-3no";
    public override int? LegacyType => 22;
    public override string Tags => "ana kontak üç kutuplu güç kontağı kontaktör 1 2 3 4 5 6 main contacts";
    protected override bool Nc => false;
}

public sealed class ContactThreeNcDef : ThreePoleContactDef
{
    public override string TypeId => "contact-3nc";
    public override int? LegacyType => 23;
    public override string Tags => "üç kutuplu kapalı kontak";
    protected override bool Nc => true;
}

/// <summary>Değiştirici (çift yollu) yardımcı kontak: ortak uç, NC ve NO.</summary>
public sealed class ContactChangeoverDef : AuxContactDefBase
{
    protected override int[] FunctionDigits => [1, 2, 4];

    public override string TypeId => "contact-co";
    public override string Tags => "değiştirici kontak changeover 11 12 14";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("C", new Vec(0, 15), Side.Left, "11"),
        new TerminalDef("NC", new Vec(20, 5), Side.Right, "12"),
        new TerminalDef("NO", new Vec(20, 15), Side.Right, "14")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, true), new(0, 2, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolChangeover(p, ctx, 15, 5, ctx.IsClosed(1, false), roller: false);
            return;
        }
        ChangeoverSymbol.Draw(p, ctx, ctx.IsClosed(1, false), 15, 5, TimedFor(ctx));
    }
}

internal static class ChangeoverSymbol
{
    /// <summary>Ortak uç (0,yCommon), NC (20,yNc), NO (20,yCommon). actuated: bıçak NO tarafında.</summary>
    public static Vec Draw(ISymbolPainter p, DrawContext ctx, bool actuated, double yCommon, double yNc, TimedMark timed = TimedMark.None)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        var pivot = new Vec(6, yCommon);
        p.Line(new Vec(0, yCommon), pivot, ink);
        p.Line(new Vec(14, yCommon), new Vec(20, yCommon), ink);
        p.Line(new Vec(14, yNc), new Vec(20, yNc), ink);
        p.Line(new Vec(14, yNc), new Vec(14, yNc + 2), ink);
        Vec tip = actuated ? new Vec(14.5, yCommon) : new Vec(14.5, yNc + 1.5);
        p.Line(pivot, tip, SymbolKit.ActiveInk(ctx, ctx.Simulating), ctx.Simulating ? 1.3 : 1);
        Vec mid = (pivot + tip) / 2;
        if (timed != TimedMark.None)
        {
            SymbolKit.DelayMark(p, ink, mid, timed);
        }
        return mid;
    }
}
