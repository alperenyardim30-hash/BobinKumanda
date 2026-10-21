using KumandaLab.Core.Catalog;
using KumandaLab.Core.Drawing;
using KumandaLab.Core.Localization;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;

namespace KumandaLab.Core.Components;

/// <summary>Elle kumanda edilen kontak takımları (buton, şalter, sınır anahtarı). Ana cihazdır: aynı etiketli kontaklar onu izler.</summary>
public abstract class ManualDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Control;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "S";
    public override Vec DefaultLabelOffset => new(4, -8);

    /// <summary>true: basılı tutulduğu sürece (buton); false: her tıklamada konum değiştirir.</summary>
    protected virtual bool Momentary => true;

    protected abstract ContactSpec[] Specs { get; }

    protected virtual string PressKey => "log.pressed";
    protected virtual string ReleaseKey => "log.releasedButton";

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new ManualSim(Specs, Momentary, PressKey, ReleaseKey);

    protected static bool Pressed(DrawContext ctx) => ctx.State?.Position == 1;
}

internal class ManualSim(ContactSpec[] specs, bool momentary, string pressKey, string releaseKey) : ContactSetSim, IDeviceMaster
{
    private bool actuated;
    private bool latched;

    protected bool Actuated => actuated;

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => actuated;
    public override bool IsInteractive => true;

    public override void Bind(DeviceRegistry devices) => Contacts = specs;

    protected override bool IsActuated(int group) => actuated;

    public override void Interact(Interaction action)
    {
        bool before = actuated;
        switch (action.Kind)
        {
            case InteractionKind.Press:
                if (momentary)
                {
                    actuated = true;
                }
                else
                {
                    latched = !latched;
                    actuated = latched;
                }
                break;
            case InteractionKind.Release:
                if (momentary && !latched)
                {
                    actuated = false;
                }
                break;
            case InteractionKind.Toggle:
                latched = !latched;
                actuated = latched;
                break;
            case InteractionKind.SetValue:
                latched = action.Value > 0.5;
                actuated = latched;
                break;
        }
        if (before != actuated)
        {
            Sim.AddLog(L(actuated ? pressKey : releaseKey, Label), Id);
        }
    }

    /// <summary>Sistem elemanlarının (kapı, asansör) veya sensörlerin konumu değiştirmesi için.</summary>
    internal void SetActuated(bool value)
    {
        actuated = value;
        latched = value;
    }

    public override void UpdateVisual(Solution sol)
    {
        base.UpdateVisual(sol);
        Visual.Position = actuated ? 1 : 0;
        Visual.Active = actuated;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.state"), L(actuated ? "state.actuated" : "state.rest"));
        if (momentary)
        {
            yield return new Readout(L("readout.hint"), L("hint.momentary"));
        }
    }
}

public sealed class ButtonNoDef : ManualDefBase
{
    public override string TypeId => "button-no";
    public override int? LegacyType => 5;
    public override char? Shortcut => 'B';
    public override string Tags => "start butonu başlat buton NO push button 13 14";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 10), Side.Left, "13"), new TerminalDef("2", new Vec(20, 10), Side.Right, "14")];

    protected override ContactSpec[] Specs => [new(0, 1, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolPushContact(p, ctx, 10, false, Pressed(ctx));
            return;
        }
        Vec mid = SymbolKit.ContactH(p, ctx, 10, false, ctx.IsClosed(0, false));
        SymbolKit.Actuator(p, ctx, ActuatorKind.Push, mid, 0, Pressed(ctx));
    }
}

public sealed class ButtonNcDef : ManualDefBase
{
    public override string TypeId => "button-nc";
    public override int? LegacyType => 6;
    public override char? Shortcut => 'D';
    public override string Tags => "stop butonu durdur buton NC push button 11 12";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 10), Side.Left, "11"), new TerminalDef("2", new Vec(20, 10), Side.Right, "12")];

    protected override ContactSpec[] Specs => [new(0, 1, true)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolPushContact(p, ctx, 10, true, Pressed(ctx));
            return;
        }
        Vec mid = SymbolKit.ContactH(p, ctx, 10, true, ctx.IsClosed(0, true));
        SymbolKit.Actuator(p, ctx, ActuatorKind.Push, mid, 0, Pressed(ctx));
    }
}

/// <summary>Jumelli buton: üstte NC, altta NO (eski EKTS ile aynı klemens yerleşimi).</summary>
public sealed class ButtonNoNcDef : ManualDefBase
{
    public override string TypeId => "button-nonc";
    public override int? LegacyType => 7;
    public override string Tags => "jumelli buton start stop çift kontaklı NO NC push button";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "11"), new TerminalDef("2", new Vec(20, 10), Side.Right, "12"),
        new TerminalDef("3", new Vec(0, 18), Side.Left, "23"), new TerminalDef("4", new Vec(20, 18), Side.Right, "24")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, true), new(2, 3, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolDualContact(p, ctx, 10, 18, Pressed(ctx), roller: false);
            return;
        }
        SymbolKit.ContactH(p, ctx, 10, true, ctx.IsClosed(0, true));
        Vec mid = SymbolKit.ContactH(p, ctx, 18, false, ctx.IsClosed(1, false));
        SymbolKit.Actuator(p, ctx, ActuatorKind.Push, mid, -2, Pressed(ctx));
    }
}

/// <summary>Değiştirici sınır anahtarı: ortak uç, NO, NC (eski EKTS klemens sırası).</summary>
public sealed class LimitSwitchCoDef : ManualDefBase
{
    public override string TypeId => "limit-co";
    public override int? LegacyType => 8;
    public override string LabelPrefix => "B";
    public override string Tags => "sınır anahtarı limit switch değiştirici";
    protected override bool Momentary => false;
    protected override string PressKey => "log.limitOn";
    protected override string ReleaseKey => "log.limitOff";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("C", new Vec(0, 15), Side.Left, "11"),
        new TerminalDef("NO", new Vec(20, 15), Side.Right, "14"),
        new TerminalDef("NC", new Vec(20, 7), Side.Right, "12")
    ];

    protected override ContactSpec[] Specs => [new(0, 2, true), new(0, 1, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolChangeover(p, ctx, 15, 7, Pressed(ctx), roller: true);
            return;
        }
        Vec mid = ChangeoverSymbol.Draw(p, ctx, ctx.IsClosed(1, false), 15, 7);
        SymbolKit.Actuator(p, ctx, ActuatorKind.Roller, mid, 0.5, Pressed(ctx));
    }
}

/// <summary>Sınır anahtarı: üstte NC, altta NO.</summary>
public sealed class LimitSwitchNoNcDef : ManualDefBase
{
    public override string TypeId => "limit-nonc";
    public override int? LegacyType => 20;
    public override string LabelPrefix => "B";
    public override string Tags => "sınır anahtarı limit switch NO NC";
    protected override bool Momentary => false;
    protected override string PressKey => "log.limitOn";
    protected override string ReleaseKey => "log.limitOff";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "11"), new TerminalDef("2", new Vec(20, 10), Side.Right, "12"),
        new TerminalDef("3", new Vec(0, 18), Side.Left, "23"), new TerminalDef("4", new Vec(20, 18), Side.Right, "24")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, true), new(2, 3, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            SymbolKit.SchoolDualContact(p, ctx, 10, 18, Pressed(ctx), roller: true);
            return;
        }
        SymbolKit.ContactH(p, ctx, 10, true, ctx.IsClosed(0, true));
        Vec mid = SymbolKit.ContactH(p, ctx, 18, false, ctx.IsClosed(1, false));
        SymbolKit.Actuator(p, ctx, ActuatorKind.Roller, mid, -1.5, Pressed(ctx));
    }
}

/// <summary>Üç kutuplu şalter (eski EKTS "Startx3").</summary>
public sealed class Switch3Def : ManualDefBase
{
    public override string TypeId => "switch-3p";
    public override int? LegacyType => 15;
    public override string LabelPrefix => "Q";
    public override string Tags => "üç kutuplu şalter ana şalter 3 pole switch";
    protected override bool Momentary => false;
    protected override string PressKey => "log.switchOn";
    protected override string ReleaseKey => "log.switchOff";
    public override Vec Size => new(20, 60);
    public override Vec DefaultLabelOffset => new(22, 25);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "1"), new TerminalDef("2", new Vec(20, 10), Side.Right, "2"),
        new TerminalDef("3", new Vec(0, 30), Side.Left, "3"), new TerminalDef("4", new Vec(20, 30), Side.Right, "4"),
        new TerminalDef("5", new Vec(0, 50), Side.Left, "5"), new TerminalDef("6", new Vec(20, 50), Side.Right, "6")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, false), new(2, 3, false), new(4, 5, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        if (ctx.School)
        {
            bool on = Pressed(ctx);
            Vec k1 = SymbolKit.SchoolKnife(p, ctx, 10, on, false);
            SymbolKit.SchoolKnife(p, ctx, 30, on, false);
            Vec k3 = SymbolKit.SchoolKnife(p, ctx, 50, on, false);
            p.Line(k1, new Vec(k1.X, k1.Y - 7), SymbolKit.InkFor(ctx));
            SymbolKit.MechanicalLink(p, ctx, k1, k3);
            return;
        }
        Vec m1 = SymbolKit.ContactH(p, ctx, 10, false, ctx.IsClosed(0, false));
        SymbolKit.ContactH(p, ctx, 30, false, ctx.IsClosed(1, false));
        Vec m3 = SymbolKit.ContactH(p, ctx, 50, false, ctx.IsClosed(2, false));
        SymbolKit.MechanicalLink(p, ctx, m1, m3);
        SymbolKit.Actuator(p, ctx, ActuatorKind.Rotary, m1, 0, Pressed(ctx));
    }
}

/// <summary>Üç kutuplu iki konumlu (değiştirici) şalter (eski EKTS "Start3x3").</summary>
public sealed class Switch3CoDef : ManualDefBase
{
    public override string TypeId => "switch-3p-co";
    public override int? LegacyType => 16;
    public override string LabelPrefix => "Q";
    public override string Tags => "üç kutuplu değiştirici şalter iki konumlu 3 pole changeover";
    protected override bool Momentary => false;
    protected override string PressKey => "log.switchPos2";
    protected override string ReleaseKey => "log.switchPos1";
    public override Vec Size => new(20, 60);
    public override Vec DefaultLabelOffset => new(22, 25);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "1"), new TerminalDef("2", new Vec(20, 2), Side.Right, "2"), new TerminalDef("3", new Vec(20, 10), Side.Right, "3"),
        new TerminalDef("4", new Vec(0, 30), Side.Left, "4"), new TerminalDef("5", new Vec(20, 22), Side.Right, "5"), new TerminalDef("6", new Vec(20, 30), Side.Right, "6"),
        new TerminalDef("7", new Vec(0, 50), Side.Left, "7"), new TerminalDef("8", new Vec(20, 42), Side.Right, "8"), new TerminalDef("9", new Vec(20, 50), Side.Right, "9")
    ];

    protected override ContactSpec[] Specs =>
        [new(0, 1, true), new(0, 2, false), new(3, 4, true), new(3, 5, false), new(6, 7, true), new(6, 8, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        bool on = ctx.IsClosed(1, false);
        if (ctx.School)
        {
            Vec k1 = SymbolKit.SchoolKnife(p, ctx, 10, on, true);
            SymbolKit.SchoolKnife(p, ctx, 30, on, true);
            Vec k3 = SymbolKit.SchoolKnife(p, ctx, 50, on, true);
            p.Line(k1, new Vec(k1.X, k1.Y - 7), SymbolKit.InkFor(ctx));
            SymbolKit.MechanicalLink(p, ctx, k1, k3);
            return;
        }
        Vec m1 = ChangeoverSymbol.Draw(p, ctx, on, 10, 2);
        ChangeoverSymbol.Draw(p, ctx, on, 30, 22);
        Vec m3 = ChangeoverSymbol.Draw(p, ctx, on, 50, 42);
        SymbolKit.MechanicalLink(p, ctx, m1, m3);
    }
}
