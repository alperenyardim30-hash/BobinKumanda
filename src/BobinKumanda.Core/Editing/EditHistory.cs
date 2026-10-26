using BobinKumanda.Core.IO;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>
/// Geri al / yinele: her değişiklikten önce devrenin sıkıştırılmamış JSON anlık görüntüsü saklanır (en fazla 200).
/// Basit ve sağlam: hangi işlem olursa olsun geri alma aynı yoldan çalışır.
/// </summary>
public sealed class EditHistory
{
    public const int MaxSteps = 200;

    private readonly LinkedList<Entry> undo = new();
    private readonly Stack<Entry> redo = new();
    private string? lastKey;
    private DateTime lastTime;

    private sealed record Entry(byte[] Data, string? Sheet);

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public event Action? Changed;

    /// <summary>
    /// Değişiklikten ÖNCE çağrılır. <paramref name="coalesceKey"/> verilirse aynı anahtarla 2 saniye içinde gelen
    /// sonraki çağrılar yeni adım açmaz (bir kutuya yazı yazarken her tuş ayrı adım olmasın diye).
    /// Yeni adım açıldıysa true.
    /// </summary>
    public bool Push(Circuit circuit, string sheet, string? coalesceKey = null)
    {
        DateTime now = DateTime.UtcNow;
        if (coalesceKey != null && coalesceKey == lastKey && (now - lastTime).TotalSeconds < 2 && undo.Count > 0)
        {
            lastTime = now;
            return false;
        }
        lastKey = coalesceKey;
        lastTime = now;
        undo.AddLast(new Entry(CircuitFile.ToBytes(circuit), sheet));
        while (undo.Count > MaxSteps)
        {
            undo.RemoveFirst();
        }
        redo.Clear();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Son değişikliği geri alır; devrenin önceki halini ve o andaki sayfayı döndürür.</summary>
    public (Circuit Circuit, string? Sheet)? Undo(Circuit current, string sheet)
    {
        if (undo.Last is not { } node)
        {
            return null;
        }
        undo.RemoveLast();
        redo.Push(new Entry(CircuitFile.ToBytes(current), sheet));
        lastKey = null;
        Changed?.Invoke();
        return (CircuitFile.FromBytes(node.Value.Data), node.Value.Sheet);
    }

    public (Circuit Circuit, string? Sheet)? Redo(Circuit current, string sheet)
    {
        if (redo.Count == 0)
        {
            return null;
        }
        Entry e = redo.Pop();
        undo.AddLast(new Entry(CircuitFile.ToBytes(current), sheet));
        lastKey = null;
        Changed?.Invoke();
        return (CircuitFile.FromBytes(e.Data), e.Sheet);
    }

    /// <summary>Son Push'u geri çeker (işlem sonunda hiçbir şey değişmediyse; ör. yerinde bırakılan sürükleme).</summary>
    public void Discard()
    {
        if (undo.Count > 0)
        {
            undo.RemoveLast();
            lastKey = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Son Push'tan bu yana devre değişti mi.</summary>
    public bool ChangedSinceLastPush(Circuit circuit) =>
        undo.Last is not { } node || !node.Value.Data.AsSpan().SequenceEqual(CircuitFile.ToBytes(circuit));

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
        lastKey = null;
        Changed?.Invoke();
    }

    /// <summary>Coalesce zincirini kırar (ör. başka bir elemana geçildiğinde).</summary>
    public void Seal() => lastKey = null;
}
