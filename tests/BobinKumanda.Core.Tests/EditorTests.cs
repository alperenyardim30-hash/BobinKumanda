using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.IO;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Düzenleyicinin çekirdek işlemleri: yönlendirme, bağlama, taşıma, döndürme, kopyalama, geri alma.</summary>
public class EditorTests
{
    private const string S = "s1";

    private static Vec Term(Circuit c, ComponentInstance x, string t)
    {
        ComponentDef def = ComponentCatalog.Get(x.Type);
        return def.TerminalWorld(x, def.FindTerminal(t)!);
    }

    private static Side SideOf(Circuit c, ComponentInstance x, string t)
    {
        ComponentDef def = ComponentCatalog.Get(x.Type);
        return def.TerminalSide(x, def.FindTerminal(t)!);
    }

    /// <summary>Arayüzdeki gibi: iki klemens arasına otomatik yönlendirilmiş kablo.</summary>
    private static Wire Connect(Circuit c, ComponentInstance a, string ta, ComponentInstance b, string tb)
    {
        List<Vec> pts = CircuitEditor.RouteBetween(c, S, Term(c, a, ta), SideOf(c, a, ta), Term(c, b, tb), SideOf(c, b, tb));
        return CircuitEditor.AddWire(c, S, WireEnd.ToTerminal(a.Id, ta), WireEnd.ToTerminal(b.Id, tb), pts);
    }

    /// <summary>L1 ─ H1 ─ N: besleme üstte, lamba ortada.</summary>
    private static (Circuit C, ComponentInstance L1, ComponentInstance N, ComponentInstance H1) LampCircuit()
    {
        var c = new Circuit();
        ComponentInstance l1 = CircuitEditor.AddComponent(c, "source-l1", new Vec(0, 0), S);
        ComponentInstance n = CircuitEditor.AddComponent(c, "source-n", new Vec(200, 0), S);
        ComponentInstance h1 = CircuitEditor.AddComponent(c, "lamp", new Vec(90, 80), S);
        Connect(c, l1, "L1", h1, "X1");
        Connect(c, h1, "X2", n, "N");
        return (c, l1, n, h1);
    }

    private static void AssertWiresSound(Circuit c)
    {
        foreach (Wire w in c.Wires)
        {
            Assert.True(w.Points.Count >= 2, w.Id);
            Assert.True(WirePath.IsOrthogonal(w.Points), $"{w.Id} dik açılı değil: {string.Join(" ", w.Points)}");
            if (!w.A.IsFree)
            {
                Assert.True(w.Points[0].NearlyEquals(CircuitEditor.EndPosition(c, w, true), 0.01), $"{w.Id} A ucu klemenste değil");
            }
            if (!w.B.IsFree)
            {
                Assert.True(w.Points[^1].NearlyEquals(CircuitEditor.EndPosition(c, w, false), 0.01), $"{w.Id} B ucu klemenste değil");
            }
        }
    }

    [Fact]
    public void Router_leaves_terminals_in_their_direction_and_stays_orthogonal()
    {
        // Sağa bakan klemensten, altta sola bakan klemense
        List<Vec> p = WireRouter.Route(new WireRouter.End(new Vec(20, 10), Side.Right), new WireRouter.End(new Vec(100, 60), Side.Left));
        Assert.True(WirePath.IsOrthogonal(p));
        Assert.Equal(new Vec(20, 10), p[0]);
        Assert.Equal(new Vec(100, 60), p[^1]);
        Assert.True(p[1].X > p[0].X, "sağa çıkmalı");
        Assert.True(p[^2].X < p[^1].X, "hedefe soldan girmeli");
        Assert.InRange(p.Count, 2, 6);

        // Aynı hizadaki iki klemens düz bağlanır
        Assert.Equal(2, WireRouter.Route(new WireRouter.End(new Vec(20, 10), Side.Right), new WireRouter.End(new Vec(80, 10), Side.Left)).Count);
    }

    [Fact]
    public void Router_goes_around_a_component_when_it_can()
    {
        var box = new RectD(40, -20, 40, 60);
        List<Vec> p = WireRouter.Route(new WireRouter.End(new Vec(0, 10), Side.Right), new WireRouter.End(new Vec(120, 10), Side.Left), [box]);
        Assert.True(WirePath.IsOrthogonal(p));
        for (int i = 1; i < p.Count; i++)
        {
            Assert.False(WireRouter.SegmentHitsRect(p[i - 1], p[i], box), string.Join(" ", p));
        }
    }

    [Fact]
    public void Moving_an_end_keeps_the_wire_orthogonal_and_the_other_end_fixed()
    {
        List<Vec> pts = [new(0, 0), new(50, 0), new(50, 40), new(100, 40)];
        List<Vec> moved = WirePath.MoveEnd(pts, true, new Vec(10, 15));
        Assert.True(WirePath.IsOrthogonal(moved));
        Assert.Equal(new Vec(10, 15), moved[0]);
        Assert.Equal(new Vec(100, 40), moved[^1]);

        List<Vec> straight = WirePath.MoveEnd([new(0, 0), new(100, 0)], false, new Vec(100, 30));
        Assert.True(WirePath.IsOrthogonal(straight));
        Assert.Equal(new Vec(0, 0), straight[0]);
        Assert.Equal(new Vec(100, 30), straight[^1]);

        // Uçtaki parçayı sürüklemek ucu klemensten koparmaz
        List<Vec> dragged = WirePath.Simplify(WirePath.DragSegment([new(0, 0), new(100, 0)], 0, new Vec(0, 20)));
        Assert.True(WirePath.IsOrthogonal(dragged));
        Assert.Equal(new Vec(0, 0), dragged[0]);
        Assert.Equal(new Vec(100, 0), dragged[^1]);
        Assert.Contains(new Vec(50, 20), dragged.Select(v => new Vec(50, v.Y)));
    }

    [Fact]
    public void Built_in_the_editor_a_lamp_lights()
    {
        (Circuit c, _, _, ComponentInstance h1) = LampCircuit();
        Assert.Equal("H1", h1.Label);
        AssertWiresSound(c);
        var sim = new Simulator(c);
        Assert.True(sim.Find(h1.Id)!.Visual.Active);
    }

    [Fact]
    public void Labels_count_up_and_reuse_freed_numbers()
    {
        var c = new Circuit();
        ComponentInstance k1 = CircuitEditor.AddComponent(c, "relay-coil", new Vec(0, 0), S);
        ComponentInstance k2 = CircuitEditor.AddComponent(c, "relay-coil", new Vec(0, 40), S);
        ComponentInstance kt = CircuitEditor.AddComponent(c, "timer-on", new Vec(0, 80), S);
        ComponentInstance contact = CircuitEditor.AddComponent(c, "contact-no", new Vec(60, 0), S);
        Assert.Equal(("K1", "K2", "KT1"), (k1.Label, k2.Label, kt.Label));
        Assert.Equal("KT1", contact.Label);
        Assert.Equal(["K1", "K2", "KT1"], Labeler.MasterLabels(c));

        var sel = Selection.Of(k1);
        CircuitEditor.Delete(c, sel);
        Assert.Equal("K1", CircuitEditor.AddComponent(c, "relay-coil", new Vec(0, 120), S).Label);
        Assert.True(Labeler.NaturalCompare("K2", "K10") < 0);
    }

    [Fact]
    public void Connecting_to_the_middle_of_a_wire_adds_a_junction_and_deleting_it_merges_back()
    {
        (Circuit c, ComponentInstance l1, _, ComponentInstance h1) = LampCircuit();
        ComponentInstance h2 = CircuitEditor.AddComponent(c, "lamp", new Vec(90, 140), S);
        Assert.Equal("H2", h2.Label);
        Wire feed = c.Wires[0];
        int wiresBefore = c.Wires.Count;

        // H2'nin X1'inden L1 kablosunun ortasına
        Vec mid = feed.Points[1];
        Hit onWire = HitTest.Wire(c, S, mid + new Vec(0.5, 0.5), 3);
        Assert.Equal(HitKind.Wire, onWire.Kind);
        WireEnd end = CircuitEditor.ResolveEnd(c, S, onWire);
        Assert.True(end.IsJunction);
        Junction j = c.FindJunction(end.Junction)!;
        Wire branch = CircuitEditor.AddWire(c, S, WireEnd.ToTerminal(h2.Id, "X1"), end,
            CircuitEditor.RouteBetween(c, S, Term(c, h2, "X1"), Side.Left, j.Position, Side.None));
        ComponentInstance n = c.Components.Single(x => x.Type == "source-n");
        Connect(c, h2, "X2", n, "N");
        AssertWiresSound(c);

        Netlist net = Netlist.Build(c);
        Assert.Equal(net.NodeOf(l1.Id, "L1"), net.NodeOf(h2.Id, "X1"));
        var sim = new Simulator(c);
        Assert.True(sim.Find(h1.Id)!.Visual.Active);
        Assert.True(sim.Find(h2.Id)!.Visual.Active);

        // Dal silinince ek noktası kalkar, bölünen kablo yeniden tek parça olur
        var del = new Selection();
        del.Wires.Add(branch.Id);
        CircuitEditor.Delete(c, del);
        Assert.Empty(c.Junctions);
        Assert.Equal(wiresBefore + 1, c.Wires.Count); // H2 → N kablosu duruyor
        AssertWiresSound(c);
        net = Netlist.Build(c);
        Assert.Equal(net.NodeOf(l1.Id, "L1"), net.NodeOf(h1.Id, "X1"));
    }

    [Fact]
    public void Dragging_a_component_drags_its_wires_and_cancel_restores()
    {
        (Circuit c, ComponentInstance l1, _, ComponentInstance h1) = LampCircuit();
        byte[] before = CircuitFile.ToBytes(c);
        var move = new MoveSession(c, Selection.Of(h1));
        move.Update(new Vec(15, 5));
        move.Update(new Vec(30, 40));
        Assert.Equal(new Vec(120, 120), h1.Position);
        AssertWiresSound(c);
        move.Cancel();
        Assert.Equal(before, CircuitFile.ToBytes(c));

        move = new MoveSession(c, Selection.Of(h1));
        move.Update(new Vec(-40, 60));
        move.Commit();
        AssertWiresSound(c);
        Netlist net = Netlist.Build(c);
        Assert.Equal(net.NodeOf(l1.Id, "L1"), net.NodeOf(h1.Id, "X1"));
    }

    [Fact]
    public void Rotating_reroutes_wires_and_four_turns_come_back()
    {
        (Circuit c, _, _, ComponentInstance h1) = LampCircuit();
        Vec x1 = Term(c, h1, "X1"), x2 = Term(c, h1, "X2");
        CircuitEditor.Rotate(c, Selection.Of(h1));
        Assert.Equal(1, h1.Rotation);
        AssertWiresSound(c);
        for (int i = 0; i < 3; i++)
        {
            CircuitEditor.Rotate(c, Selection.Of(h1));
        }
        Assert.Equal(0, h1.Rotation);
        Assert.Equal(x1, Term(c, h1, "X1"));
        Assert.Equal(x2, Term(c, h1, "X2"));
        AssertWiresSound(c);
    }

    [Fact]
    public void Mirroring_a_group_flips_it_and_keeps_wires_on_terminals()
    {
        (Circuit c, _, _, _) = LampCircuit();
        Selection all = CircuitEditor.All(c, S);
        ComponentInstance l1 = c.Components[0];
        double before = l1.Position.X;
        CircuitEditor.Mirror(c, all);
        Assert.True(l1.Position.X > before, "L1 sağa geçmeli");
        AssertWiresSound(c);
        CircuitEditor.Mirror(c, all);
        Assert.Equal(before, l1.Position.X, 6);
        AssertWiresSound(c);
    }

    [Fact]
    public void Pasting_gives_new_numbers_and_contacts_follow_their_new_coil()
    {
        var c = new Circuit();
        ComponentInstance k1 = CircuitEditor.AddComponent(c, "relay-coil", new Vec(0, 0), S);
        ComponentInstance k1c = CircuitEditor.AddComponent(c, "contact-no", new Vec(60, 0), S);
        ComponentInstance lonely = CircuitEditor.AddComponent(c, "contact-nc", new Vec(120, 0), S, label: "K1");
        ComponentInstance h1 = CircuitEditor.AddComponent(c, "lamp", new Vec(0, 60), S);
        Connect(c, k1c, "2", h1, "X1");
        Assert.Equal("K1", k1c.Label);

        var copyWithCoil = new Selection();
        copyWithCoil.Components.UnionWith([k1.Id, k1c.Id, h1.Id]);
        Circuit frag = CircuitEditor.Copy(c, copyWithCoil);
        Assert.Single(frag.Wires);
        Selection pasted = CircuitEditor.Paste(c, frag, new Vec(0, 200), S);
        var labels = c.Components.Where(x => pasted.Components.Contains(x.Id)).ToDictionary(x => x.Type, x => x.Label);
        Assert.Equal("K2", labels["relay-coil"]);
        Assert.Equal("K2", labels["contact-no"]);
        Assert.Equal("H2", labels["lamp"]);
        Assert.Single(pasted.Wires);
        AssertWiresSound(c);

        // Bobini kopyalanmayan kontak eski bobinini izlemeye devam eder
        Selection p2 = CircuitEditor.Paste(c, CircuitEditor.Copy(c, Selection.Of(lonely)), new Vec(0, 300), S);
        Assert.Equal("K1", c.FindComponent(p2.Components.Single())!.Label);
    }

    [Fact]
    public void Deleting_a_component_leaves_its_wires_and_a_new_one_reconnects()
    {
        (Circuit c, ComponentInstance l1, _, ComponentInstance h1) = LampCircuit();
        Vec pos = h1.Position;
        CircuitEditor.Delete(c, Selection.Of(h1));
        Assert.Equal(2, c.Wires.Count);
        Assert.All(c.Wires, w => Assert.True(w.A.IsFree || w.B.IsFree));

        ComponentInstance again = CircuitEditor.AddComponent(c, "lamp", pos, S);
        Assert.All(c.Wires, w => Assert.False(w.A.IsFree || w.B.IsFree));
        Assert.True(new Simulator(c).Find(again.Id)!.Visual.Active);
        _ = l1;
    }

    [Fact]
    public void Undo_and_redo_restore_exact_states()
    {
        (Circuit c, _, _, ComponentInstance h1) = LampCircuit();
        var history = new EditHistory();
        byte[] original = CircuitFile.ToBytes(c);

        history.Push(c, S);
        CircuitEditor.Delete(c, Selection.Of(h1));
        byte[] deleted = CircuitFile.ToBytes(c);

        (Circuit undone, string? sheet) = history.Undo(c, S)!.Value;
        Assert.Equal(S, sheet);
        Assert.Equal(original, CircuitFile.ToBytes(undone));
        Assert.True(history.CanRedo);
        (Circuit redone, _) = history.Redo(undone, S)!.Value;
        Assert.Equal(deleted, CircuitFile.ToBytes(redone));

        // Aynı anahtarla art arda gelen değişiklikler tek adım sayılır
        var h = new EditHistory();
        h.Push(c, S, "label:c3");
        h.Push(c, S, "label:c3");
        h.Push(c, S, "label:c3");
        Assert.NotNull(h.Undo(c, S));
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Sheets_can_be_added_and_removed_with_their_contents()
    {
        (Circuit c, _, _, _) = LampCircuit();
        Sheet s2 = CircuitEditor.AddSheet(c, "Kumanda");
        ComponentInstance k = CircuitEditor.AddComponent(c, "relay-coil", new Vec(0, 0), s2.Id);
        Assert.Equal(s2.Id, k.Sheet);
        Assert.True(CircuitEditor.DeleteSheet(c, s2.Id));
        Assert.Null(c.FindComponent(k.Id));
        Assert.False(CircuitEditor.DeleteSheet(c, S));
    }
}
