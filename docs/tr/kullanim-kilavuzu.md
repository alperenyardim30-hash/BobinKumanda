# Bobin Kumanda kullanım kılavuzu

Bu kılavuz programın içindeki yardım sayfasıyla (<kbd>F1</kbd>) aynı bilgileri verir.

## İçindekiler

1. [Başlarken](#başlarken)
2. [Devre çizme](#devre-çizme)
3. [Kablolar](#kablolar)
4. [Simülasyon](#simülasyon)
5. [Ölçü aletleri](#ölçü-aletleri)
6. [Koruma elemanları ve arızalar](#koruma-elemanları-ve-arızalar)
7. [Dosya, çıktı ve yazdırma](#dosya-çıktı-ve-yazdırma)
8. [Klavye kısayolları](#klavye-kısayolları)

## Başlarken

1. **Dosya → Örnekler** menüsünden ya da açılış ekranındaki **Örnekler** düğmesinden hazır bir devre açın (örneğin *Mühürleme*).
2. <kbd>F5</kbd> ile simülasyonu başlatın.
3. Butonlara fareyle tıklayın. Start butonunu basılı tutabilir, <kbd>Shift</kbd> ile tıklayarak basılı bırakabilirsiniz.
4. Olay günlüğünde (alt panel) her şeyin sırasını görürsünüz: "S1 basıldı → K1 çekti → M1 çalışıyor".
5. <kbd>F5</kbd> ile durdurup devreyi değiştirin.

> Kontaklar, bağlı oldukları bobini **etiketten** tanır: K1 etiketli bütün kontaklar K1 bobiniyle birlikte konum değiştirir. Kontağı seçip sağdaki panelde "Bağlı olduğu bobin" kutusundan değiştirebilirsiniz.

## Devre çizme

![Düzenleyici](../images/duzenleyici.png)

- **Eleman eklemek:** soldaki paletten sürükleyip bırakın ya da bir kez tıklayıp çizim alanında istediğiniz yere tıklayın. Arama kutusuna "termik", "sigorta", "motor" gibi yazabilirsiniz (<kbd>Ctrl</kbd>+<kbd>F</kbd>). Tek tuşluk kısayollar: <kbd>K</kbd> bobin, <kbd>A</kbd> NO kontak, <kbd>C</kbd> NC kontak, <kbd>B</kbd> start, <kbd>D</kbd> stop, <kbd>H</kbd> lamba, <kbd>M</kbd> motor.
- **Yerleştirirken:** <kbd>R</kbd> döndürür, <kbd>F</kbd> aynalar, <kbd>Shift</kbd>+tık art arda birden çok koyar, <kbd>Esc</kbd> vazgeçer.
- **Seçmek ve taşımak:** tıklayın, sürükleyin. Boş yerden sürükleyerek çoklu seçim yapılır. Taşınan elemanın kabloları dik açılı olarak peşinden gelir. Ok tuşları bir ızgara adımı kaydırır.
- **Etiketler:** röleler K1, K2..., butonlar S1, S2..., lambalar H1... gibi IEC'ye uygun otomatik ad alır. Etiketi sürükleyerek yerini değiştirebilirsiniz.
- **Özellikler:** sağ panelde bobin gerilimi, zaman rölesinin süresi, motor gücü, lamba rengi, W otomatının anma akımı gibi değerler değiştirilir.
- **Geri al:** <kbd>Ctrl</kbd>+<kbd>Z</kbd> (200 adım), <kbd>Ctrl</kbd>+<kbd>Y</kbd> yinele. Kopyalanan rölelere yeni numara verilir; onlarla gelen kontaklar yeni bobini izler.
- **Sayfalar:** alttaki **+** ile yeni sayfa açılır. Uzun devrelerde *sayfa bağlantı oku* kullanın: aynı adı taşıyan oklar aynı iletkendir.
- **Metin:** <kbd>T</kbd> ile açıklama yazısı eklenir.
- **Klemens numaraları ve çapraz referans:** Görünüm sekmesinden açılır. Yardımcı kontaklar sırayla numaralanır (13-14, 21-22, 33-34...; zaman gecikmeli kontaklar 15-16, 17-18), her bobinin altında kontaklarının listesi görünür.

## Kablolar

- Bir klemense (eleman uçlarındaki nokta), sonra ikinci klemense tıklayın. Kablo dik açılı olarak kendiliğinden çizilir.
- Arada boş bir yere tıklamak köşe ekler; çift tıklamak kabloyu orada bırakır.
- Kablonun ortasına bağlamak **ek noktası** oluşturur.
- Seçili kablonun bir parçasını sürükleyerek yolunu düzeltebilirsiniz.
- Kablolar renklerini bağlı oldukları fazdan alır: L1 kahverengi, L2 siyah, L3 gri, N mavi, PE sarı-yeşil.
- Boşta kalan kablo uçları kırmızı halkayla gösterilir.

## Simülasyon

Program devreyi gerçek bir elektrik devresi gibi çözer: her noktadaki gerilim ve her kablodaki akım hesaplanır. Bobinler anma geriliminin %85'inde çeker, %55'in altına inince bırakır.

| Ne yapılır | Nasıl |
|---|---|
| Başlat / durdur | <kbd>F5</kbd> |
| Duraklat / devam | <kbd>F6</kbd> |
| Adım adım (bir sonraki olaya kadar) | <kbd>F10</kbd> |
| Hız | Simülasyon sekmesi: 0,25× ile 10× arası |
| Butona basmak | Fareyle basılı tutun; <kbd>Shift</kbd>+tık basılı bırakır |
| Şalter, seçici, pako şalter, acil stop | Tıklayınca konum değiştirir; sağ panelde kaydırıcıyla konum seçilir |
| Sensörler (şamandıra, termostat, basınç, fotosel) | Elemanı seçin, sağ paneldeki kaydırıcıyla değeri değiştirin |
| Yaklaşım sensörü | Tıklayınca önüne cisim gelir ya da uzaklaşır |
| Motor yükü | Motoru seçip "Mil yükü" kaydırıcısını değiştirin |
| Termik röle, kaçak akım rölesi, sigorta | Sağ paneldeki **Reset**, **Test**, **Sigortayı değiştir** düğmeleri |

Akım geçen kablolarda turuncu noktalar akış yönünde ilerler. Korna, buzzer, siren ve zil bilgisayardan ses verir; Görünüm sekmesinden kapatılabilir.

## Ölçü aletleri

![Multimetre](../images/multimetre.png)

### Multimetre

- **Ölçüm → Multimetre** ile açılır. Kırmızı uç (V/Ω) ve siyah uç (COM) sırayla bir klemense, ek noktasına ya da kabloya tıklanarak takılır. Alet panelini başlığından tutup taşıyabilirsiniz.
- **V~** alternatif gerilim, **V=** doğru gerilim: simülasyon çalışırken ölçülür.
- **Ω** direnç: devre **enerjisizken** (düzenleme sırasında) ölçülür. Açık devrede gösterge "OL" yazar.
- Enerjili devrede direnç ölçmeye çalışırsanız uyarı alırsınız: gerçek alet bundan zarar görür.

### Pens ampermetre

- **Ölçüm → Pens ampermetre** ile açılır; akımını ölçmek istediğiniz kabloya tıklayın.
- **MAX** en yüksek değeri tutar: motorun kalkış akımını (anma akımının 5-6 katı) görmek için motoru çalıştırmadan önce pensi takın.

![Simülasyon](../images/simulasyon.png)

## Koruma elemanları ve arızalar

- **Kısa devre:** korumasız bir kısa devrede simülasyon durur, kısa devre yolu kırmızı yanıp söner ve Sorunlar sekmesinde nedeni anlatılır. Devrenin başına W otomatı ya da sigorta koyarsanız koruma elemanı atar ve simülasyon sürer.
- **W otomatı:** B, C, D eğrisi; aşırı akımda gecikmeli (termik), kısa devrede anında (manyetik) açar. Koluna tıklayarak yeniden kurun.
- **Termik röle:** motor akımını izler. Ayar akımını motorun anma akımına getirin. Atınca 95-96 kontağı açılır, 97-98 kapanır. Soğumadan resetlenemez.
- **Kaçak akım rölesi:** fazdan çıkan ve nötrden dönen akımın farkı 30 mA'i geçerse açar. Motorda "gövde kaçağı" arızası oluşturup deneyin. Motorun PE ucu bağlı değilse gövdede tehlikeli dokunma gerilimi uyarısı çıkar.
- **Bobin yanması:** 230 V'luk bobini iki faz arasına (400 V) bağlarsanız ya da AC bobine DC verirseniz bobin yanar.
- **Faz eksikliği:** motor kalkamaz, uğuldar ve ısınır; termik röle korumazsa yanar.
- Sorunlar sekmesindeki bir sorunun üstüne tıklamak ilgili elemanı gösterir.

## Dosya, çıktı ve yazdırma

![Antetli sayfa](../images/antetli-sayfa.png)

- Devreler **.bobin** uzantısıyla kaydedilir. Program her 2 dakikada kaydedilmemiş işinizi yedekler; elektrik kesilirse bir sonraki açılışta geri getirmeyi önerir.
- **Eski EKTS dosyaları (.esc)** açılır. **Farklı kaydet** ile .esc olarak da kaydedebilirsiniz; eski programda olmayan elemanlar ve ikinci sayfalar kaydedilmez, program bunları listeler.
- **Belge bilgileri:** öğrenci adı, numara, sınıf, okul, öğretmen ve tarih; sayfanın sağ altındaki antete yazılır.
- **Resim olarak kaydet:** sayfa antetle birlikte PNG ya da JPG olur.
- **Yazdır** (<kbd>Ctrl</kbd>+<kbd>P</kbd>): A4 ya da A3 yatay, sayfaya sığdırılmış. PDF için yazıcı olarak "Microsoft Print to PDF" seçin.

## Klavye kısayolları

| Tuş | İş |
|---|---|
| <kbd>Ctrl</kbd>+<kbd>N</kbd> / <kbd>O</kbd> / <kbd>S</kbd> | Yeni / Aç / Kaydet |
| <kbd>Ctrl</kbd>+<kbd>Z</kbd> / <kbd>Y</kbd> | Geri al / Yinele |
| <kbd>Ctrl</kbd>+<kbd>X</kbd> / <kbd>C</kbd> / <kbd>V</kbd> / <kbd>D</kbd> | Kes / Kopyala / Yapıştır / Çoğalt |
| <kbd>Ctrl</kbd>+<kbd>A</kbd> | Tümünü seç |
| <kbd>Ctrl</kbd>+<kbd>F</kbd> | Eleman ara |
| <kbd>Ctrl</kbd>+<kbd>P</kbd> | Yazdır |
| <kbd>R</kbd> / <kbd>F</kbd> / <kbd>Del</kbd> | Döndür / Aynala / Sil |
| <kbd>W</kbd> / <kbd>T</kbd> | Kablo aracı / Metin aracı |
| <kbd>F5</kbd> / <kbd>F6</kbd> / <kbd>F10</kbd> | Simülasyon başlat-durdur / duraklat / adım |
| <kbd>F1</kbd> | Yardım |
| Tekerlek / orta tuş / <kbd>Boşluk</kbd>+sürükle | Yakınlaştır / kaydır |
| <kbd>Home</kbd> | Sayfaya sığdır |
