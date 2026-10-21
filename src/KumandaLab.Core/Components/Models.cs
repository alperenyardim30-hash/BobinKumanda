using System.Globalization;
using System.Numerics;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.Localization;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;

namespace KumandaLab.Core.Components;

/// <summary>
/// Röle/kontaktör bobini: anma gerilimine göre empedans, çekme (%85) ve bırakma (%55) eşikleri,
/// mekanik gecikme, aşırı gerilimde yanma.
/// </summary>
public sealed class CoilModel
{
    public static readonly string[] VoltageChoices = ["230AC", "24AC", "24DC", "400AC", "110AC", "12DC"];

    private readonly SimComponent owner;
    private bool target;
    private EventHandle? pending;
    private EventHandle? burnTimer;
    private int branch = -1;

    public double RatedVoltage { get; }
    public bool Dc { get; }
    public Complex Zac { get; }
    public double Rdc { get; }
    public bool Pulled { get; private set; }
    public bool Burned { get; private set; }
    public double Uac { get; private set; }
    public double Udc { get; private set; }
    public double Ueff => Dc ? Udc : Uac;
    public Complex Current { get; private set; }

    /// <summary>Bobin durumu değişince (çekti/bıraktı) çağrılır.</summary>
    public event Action<bool>? Changed;

    public CoilModel(SimComponent owner, string code, double holdingVa = 8)
    {
        this.owner = owner;
        (RatedVoltage, Dc) = Parse(code);
        if (Dc)
        {
            // DC bobin: ~3 W, yüksek endüktans
            Rdc = RatedVoltage * RatedVoltage / 3.0;
            Zac = new Complex(Rdc, 2.2 * Rdc);
        }
        else
        {
            double z = RatedVoltage * RatedVoltage / holdingVa;
            const double cosPhi = 0.35;
            Zac = Complex.FromPolarCoordinates(z, Math.Acos(cosPhi));
            Rdc = z * cosPhi;
        }
    }

    public static (double Volts, bool Dc) Parse(string code)
    {
        code = code.Trim().ToUpperInvariant();
        bool dc = code.EndsWith("DC", StringComparison.Ordinal);
        string num = code.Replace("AC", "").Replace("DC", "").Replace("V", "");
        double v = double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d : 230;
        return (v, dc);
    }

    public void Stamp(Stamper s, Pin a, Pin b)
    {
        branch = Burned ? s.Switch(a, b, false) : s.Impedance(a, b, Zac, Rdc);
    }

    public void Evaluate(Solution sol)
    {
        if (branch < 0)
        {
            return;
        }
        Uac = sol.BranchVoltageAc(branch).Magnitude;
        Udc = Math.Abs(sol.BranchVoltageDc(branch));
        Current = sol.CurrentAc(branch);
        if (Burned)
        {
            return;
        }
        double u = Ueff;
        bool want = Pulled ? u >= 0.55 * RatedVoltage : u >= 0.85 * RatedVoltage;
        bool expected = pending != null ? target : Pulled;
        if (want != expected)
        {
            Simulator.Cancel(pending);
            pending = null;
            if (want != Pulled)
            {
                target = want;
                pending = owner.Sim.Schedule(owner.Sim.ContactDelay, () =>
                {
                    pending = null;
                    Pulled = target;
                    owner.Sim.AddLog(Loc.T(target ? "log.pulled" : "log.released", owner.Label), owner.Id);
                    Changed?.Invoke(target);
                });
            }
        }

        bool overVoltage = u > 1.3 * RatedVoltage;
        bool acOnDc = !Dc && Udc > 0.25 * RatedVoltage;
        if ((overVoltage || acOnDc) && burnTimer == null)
        {
            burnTimer = owner.Sim.Schedule(acOnDc ? 0.5 : 1.5, () =>
            {
                burnTimer = null;
                Burned = true;
                Simulator.Cancel(pending);
                pending = null;
                bool was = Pulled;
                Pulled = false;
                owner.Visual.Fault = true;
                owner.Sim.ReportFault("coil-burned",
                    Loc.T("problem.coilBurned", owner.Label, Simulator.FormatVolts(acOnDc ? Udc : Uac), FormatRating()),
                    Loc.T(acOnDc ? "explain.coilAcOnDc" : "explain.coilOverVoltage"),
                    [owner.Id], []);
                if (was)
                {
                    Changed?.Invoke(false);
                }
            });
        }
        else if (!overVoltage && !acOnDc && burnTimer != null)
        {
            Simulator.Cancel(burnTimer);
            burnTimer = null;
        }
    }

    public void Check(Solution sol)
    {
        if (Burned)
        {
            return;
        }
        double u = Ueff;
        if (u > 0.2 * RatedVoltage && u < 0.85 * RatedVoltage && !Pulled)
        {
            owner.Sim.ReportLive(new SimProblem("coil-undervoltage", ProblemSeverity.Warning,
                Loc.T("problem.coilUnderVoltage", owner.Label, Simulator.FormatVolts(u), FormatRating()),
                Loc.T("explain.coilUnderVoltage"), [owner.Id], []));
        }
        if (Dc && Uac > 0.5 * RatedVoltage)
        {
            owner.Sim.ReportLive(new SimProblem("coil-dc-on-ac", ProblemSeverity.Warning,
                Loc.T("problem.coilDcOnAc", owner.Label, Simulator.FormatVolts(Uac)),
                Loc.T("explain.coilDcOnAc"), [owner.Id], []));
        }
    }

    public string FormatRating() => string.Format(CultureInfo.InvariantCulture, "{0:0} V {1}", RatedVoltage, Dc ? "DC" : "AC");

    public IEnumerable<Readout> Readouts()
    {
        yield return new Readout(Loc.T("readout.coilVoltage"),
            Dc ? Simulator.FormatVolts(Udc) + " DC" : Simulator.FormatVolts(Uac) + " AC");
        yield return new Readout(Loc.T("readout.coilRated"), FormatRating());
        yield return new Readout(Loc.T("readout.current"), Simulator.FormatAmps(Current.Magnitude));
        yield return new Readout(Loc.T("readout.state"), Loc.T(Burned ? "state.burned" : Pulled ? "state.pulled" : "state.released"));
    }
}

/// <summary>Bir kontak: iki klemens arası; NC ise dinlenmede kapalı. Group: aynı anda konum değiştiren takım.</summary>
public readonly record struct ContactSpec(int A, int B, bool NormallyClosed, int Group = 0);

/// <summary>Kontak takımlarından oluşan elemanlar için ortak taban.</summary>
public abstract class ContactSetSim : SimComponent
{
    protected ContactSpec[] Contacts = [];

    protected abstract bool IsActuated(int group);

    public bool ContactClosed(int i) => IsActuated(Contacts[i].Group) ^ Contacts[i].NormallyClosed;

    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < Contacts.Length; i++)
        {
            s.Switch(T(Contacts[i].A), T(Contacts[i].B), ContactClosed(i));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        if (Visual.Closed.Length != Contacts.Length)
        {
            Visual.Closed = new bool[Contacts.Length];
        }
        for (int i = 0; i < Contacts.Length; i++)
        {
            Visual.Closed[i] = ContactClosed(i);
        }
    }
}

/// <summary>Etiketiyle bir ana cihazı (bobin, buton, koruma rölesi) izleyen kontaklar.</summary>
public class FollowerContactSim : ContactSetSim
{
    private IDeviceMaster? master;
    private readonly string channel;

    public FollowerContactSim(ContactSpec[] contacts, string channel)
    {
        Contacts = contacts;
        this.channel = channel;
    }

    public IDeviceMaster? Master => master;

    public override void Bind(DeviceRegistry devices)
    {
        master = Instance.Label.Trim().Length > 0 ? devices.Find(Instance.Label) : null;
        if (master == null)
        {
            Sim.ReportLatched(new SimProblem("no-master", ProblemSeverity.Warning,
                Loc.T("problem.noMaster", Label), Loc.T("explain.noMaster"), [Id], []));
        }
    }

    protected override bool IsActuated(int group) =>
        master != null && master.ChannelActive(channel == "auto" ? master.DefaultChannel : channel);
}
