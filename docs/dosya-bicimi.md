# Dosya biçimleri

## .bobin

Bobin Kumanda'nın kendi biçimidir: okunabilir, girintili JSON. Kayıt önce geçici dosyaya yazılır, sonra eskisinin yerine konur; yazma yarıda kesilse bile eski dosya bozulmaz.

```json
{
  "format": "BobinKumanda",
  "version": 1,
  "app": "0.1.0",
  "circuit": {
    "title": { "circuitName": "Mühürleme", "studentName": "", "studentNumber": "", "className": "",
               "school": "", "teacher": "", "date": "", "notes": "" },
    "sheets": [ { "id": "s1", "name": "" } ],
    "components": [
      { "id": "c2", "type": "relay-coil", "label": "K1", "sheet": "s1",
        "position": { "x": 300, "y": 50 }, "rotation": 0, "mirror": false,
        "params": { "coilVoltage": "230AC" } }
    ],
    "wires": [
      { "id": "w9", "sheet": "s1",
        "a": { "component": "c4", "terminal": "2" },
        "b": { "junction": "j7" },
        "points": [ { "x": 150, "y": 60 }, { "x": 170, "y": 60 } ] }
    ],
    "junctions": [ { "id": "j7", "sheet": "s1", "position": { "x": 170, "y": 60 } } ],
    "texts": [ { "id": "t12", "sheet": "s1", "position": { "x": 40, "y": 180 }, "size": { "x": 200, "y": 12 }, "text": "Not" } ]
  }
}
```

- `type`: eleman kimliği (bkz. [eleman listesi](tr/elemanlar.md)); `params` elemana göre değişir, değerler metindir.
- `position`: elemanın döndürülmüş sınır kutusunun sol üst köşesi. Birim eski EKTS pikselidir, ızgara 10 birimdir.
- `rotation`: 0-3, saat yönünde 90°'lik adımlar; `mirror`: döndürmeden önce yatay çevirme.
- Kablo uçları bir elemanın klemensine (`component` + `terminal`), bir ek noktasına (`junction`) bağlıdır ya da boştadır. `points` uç noktalar dahil dik açılı köşelerdir.
- Kontaklar bobinlerine `label` ile bağlıdır (aynı etiket).
- `version` dosya biçiminin sürümüdür; program kendinden yeni sürümlü dosyayı açmaz ve güncelleme ister.

Programın ilk deneme sürümünün yazdığı `.kmlab` dosyaları da açılır.

## .esc (eski EKTS)

Eski EKTS dosyaları iki BinaryFormatter grafiğinden oluşur: bir imza metni ve altı listeli bir `ArrayList` (elemanlar, çizgiler, ek noktaları, bağlantı noktaları, metinler, özet paneli). Bobin Kumanda bu dosyaları **BinaryFormatter kullanmadan** okur (`System.Formats.Nrbf`); dosyadaki hiçbir tip örneklenmez, bu yüzden bozuk ya da kötü niyetli bir dosya kod çalıştıramaz.

Kaydederken kayıtlar elle yazılır; tip ve kütüphane adları eski programınkilerle aynıdır. Eski biçimin sınırları:

- Tek sayfa vardır; yalnızca gösterilen sayfa kaydedilir.
- Eski programda olmayan elemanlar (W otomatı, sensörler, yeni motorlar...) kaydedilmez; program bunları listeler.
- Her bağlantı noktası tek bir noktaya bağlanabildiği için, birden çok kablonun birleştiği klemens ve ek noktalarına otomatik ek noktaları eklenir.
- Motorların PE klemensi eski programda yoktur.
