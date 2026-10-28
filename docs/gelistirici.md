# Geliştirici rehberi

Bu belge kodun nasıl düzenlendiğini, simülasyonun nasıl çalıştığını ve yeni bir elemanın nasıl ekleneceğini anlatır.

## Projeler

| Proje | Görevi |
|---|---|
| `src/BobinKumanda.Core` | Arayüzden bağımsız her şey: devre modeli, eleman kataloğu, simülasyon, dosya okuma/yazma, düzenleme işlemleri, çizim, örnekler, yardım. `net10.0` |
| `src/BobinKumanda.App` | WPF arayüzü: şerit menü (Fluent.Ribbon), çizim alanı, paneller, ölçü aletleri, yazdırma. `net10.0-windows` |
| `tests/BobinKumanda.Core.Tests` | xUnit v3 testleri: simülasyon senaryoları, elemanlar, dosya biçimleri, düzenleyici, örnekler, metinler |

```
Core/
  Model/        Circuit, ComponentInstance, Wire, Junction, TextNote, TitleBlock
  Catalog/      ComponentDef (eleman tipi tabanı), ComponentCatalog (otomatik kayıt)
  Components/   bütün elemanlar: sembol çizimi + simülasyon modeli
  Simulation/   Netlist, MNA çözücü (Mna.cs), Simulator (olaylar, zaman), Meter (ölçü aletleri)
  Drawing/      CircuitRenderer, CrossReference, PageRenderer, SvgPainter
  Editing/      CircuitEditor, WireRouter, MoveSession, EditHistory, HitTest, Labeler
  IO/           CircuitFile (.bobin), EscReader/EscImporter, EscWriter/EscExporter (.esc)
  Samples/      SampleLibrary: örnek devreler kodla kurulur
  Help/         HelpPage (programdaki yardım), ComponentReference (docs/ eleman listesi)
  Localization/ Strings.txt (anahtar | Türkçe | English), Loc
```

## Simülasyon

Devre her durum değişikliğinde **fazörlerle düğüm analiziyle (MNA)** çözülür:

1. `Netlist` kablolardan, ek noktalarından ve elemanların içindeki kalıcı bağlantılardan iletkenleri (düğümleri) çıkarır.
2. Her eleman `Stamp` ile modelini ekler: empedans (`Impedance`), anahtar (`Switch`: kapalı 1 mΩ, açık sonsuz), kaynak (`Source`: Norton eşdeğeri, AC ve DC ayrı).
3. AC ve DC sistemleri ayrı çözülür (`Solution`). Her kaynak için ayrı çözüm yapılır; kabloda akış yönü buradan bulunur.
4. Her eleman `Evaluate` ile sonucu okur ve durumunu günceller (bobin çekti, W otomatı attı...). Bir eleman `true` dönerse devre yeniden çözülür; kararlı hale gelene kadar sürer.
5. Zamanla değişen süreçler (motor hızlanması, ısınma, zaman rölesi) olay kuyruğu (`Schedule`) ve küçük zaman adımlarıyla (`Advance`) ilerler.

Şebeke TN-S'dir: L1 = 230∠0°, L2 = 230∠−120°, L3 = 230∠120°, N ve PE yıldız noktasına küçük empedansla bağlı. Kısa devre, büyük akımdan kendiliğinden anlaşılır; koruma elemanları çözüm sırasında açar.

## Yeni eleman eklemek

Yeni eleman, `ComponentDef`'ten türeyen tek bir sınıftır; katalog onu kendiliğinden bulur. Örnek, iki uçlu basit bir yük:

```csharp
public sealed class BuzzerDef : TwoTerminalLoadDef
{
    public override string TypeId => "my-buzzer";           // dosyalarda saklanan kimlik
    public override string LabelPrefix => "P";              // IEC harfi: P1, P2...
    public override string Tags => "buzzer zil ikaz";       // paletteki arama için

    public override IReadOnlyList<ParamDef> Params => [ParamDef.Number("power", 5, "W", 1, 100)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Leads(p, ink, 5);
        p.Circle(new Vec(10, 10), 5, ink, 1, ctx.State?.Active == true ? Paint.Of(Ink.Active, 0.4) : null);
    }

    public override SimComponent CreateSim(ComponentInstance c) =>
        new ResistiveLoadSim(230, c.GetDouble("power", 5), "buzzer");
}
```

Sonra `Strings.txt`'ye `comp.my-buzzer` (ad) ve `info.my-buzzer` (bilgi kartı) satırlarını ekleyin. `LocalizationTests` eksik metni yakalar. Eleman listesini güncellemek için:

```powershell
$env:KLAB_DOCS = "write"; dotnet test --project tests/BobinKumanda.Core.Tests -c Release
```

Kendi davranışı olan elemanlar `SimComponent`'ten türeyen bir sınıf yazar: `Stamp`, `Evaluate`, gerekiyorsa `Advance`, `Interact` (tıklama, kaydırıcı), `Readouts` (sağ paneldeki ölçümler), `Slider` ve `Actions`. Kontakları etiketle izlenen cihazlar `IDeviceMaster`'ı uygular.

## Testler

```powershell
dotnet test --project tests/BobinKumanda.Core.Tests -c Release
```

- Senaryo testleri devreyi `CircuitBuilder` ile kurar, `Press`, `Run` ile çalıştırır ve sonuçları denetler.
- Eski EKTS örnek dosyaları telif yüzünden depoda yoktur; `tests/LegacySamples` klasörü varsa uyumluluk testleri de çalışır, yoksa atlanır.

### Arayüzü betikle denemek

Program bir deneme betiğiyle başlatılabilir; fare olaylarını gerçek fareye dokunmadan taklit eder, ekran görüntüsü alır:

```powershell
BobinKumanda.exe --script "sample:latching; start; click:S1; run:1; expect:active:K1=1; shot:a.png; quit"
```

Komutların tam listesi `src/BobinKumanda.App/ScriptRunner.cs` başındadır; sonuç `%LOCALAPPDATA%\BobinKumanda\logs\script.txt` dosyasına yazılır.

## Metinler ve diller

Bütün metinler `src/BobinKumanda.Core/Localization/Strings.txt` dosyasındadır: her satır `anahtar | Türkçe | English`. Yeni bir dil için sütun eklemek ve `Loc.Languages`'e kodunu yazmak yeterlidir.

## Yayın paketleri

`installer/package.ps1` her mimari (x64, x86) için iki paket üretir, `artifacts/release` altına yazar:

- `BobinKumanda-<sürüm>-portable-<mimari>.zip`: sıkıştırılmış tek dosyalık exe.
- `BobinKumanda-<sürüm>-setup-<mimari>.exe`: Inno Setup kurulumu (`installer/BobinKumanda.iss`). İçindeki exe sıkıştırılmamıştır, böylece zayıf bilgisayarda her açılışta açılmayı beklemez; boyutu kurulum dosyası küçültür. Başlat menüsü ve masaüstü kısayolu, `.bobin` (isteğe bağlı `.esc`) ilişkilendirmesi, kaldırıcı ve "yalnızca benim için kur" seçeneği vardır.

İkisi de self-contained'dir; hedef bilgisayarda .NET ya da internet gerekmez. Yerelde çalıştırmak için [Inno Setup 6](https://jrsoftware.org/isdl.php) gerekir.

GitHub Actions her `main` gönderiminde paketleri derleyip "BobinKumanda-paketler" adıyla saklar. Yeni sürüm yayınlamak için `Directory.Build.props` içindeki `Version`'ı artırıp etiket gönderin; Actions paketleri derleyip sürüm sayfasını `installer/release-notes.md` metniyle oluşturur:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

## Katkı

Hata bildirimi ve önerileri [Issues](https://github.com/alperenyardim30-hash/BobinKumanda/issues) sayfasından iletebilirsiniz. Kod gönderirken testlerin geçtiğinden emin olun. EKTS'nin decompile edilmiş kodundan parça kopyalamayın: Bobin Kumanda yalnızca davranış ve dosya biçimi bilgisinden yola çıkılarak yazılmıştır.
