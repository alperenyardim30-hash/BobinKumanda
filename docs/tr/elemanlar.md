# Elemanlar

Bu sayfa programın eleman kataloğundan otomatik üretilir. Etiket sütunu otomatik verilen IEC harf kodudur; klemens sütunu klemens numaralarıdır.

## Besleme

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **DC güç kaynağı** | `G1` | L N + − | 230 V AC'den sabit 24 V DC üretir (sensörler, PLC, DC bobinler). Aşırı yükte çıkışı kapatır. |
| **Üç faz besleme (L1 L2 L3)** | `3~` | L1 L2 L3 | Üç faz şebeke: fazlar arası 400 V, faz-nötr 230 V. |
| **Üç faz + N + PE besleme** | `3~` | L1 L2 L3 N PE | Beş iletkenli TN-S şebeke: L1, L2, L3, N ve PE. |
| **L1 fazı** | `L1` | L1 | 230 V faz iletkeni (eski adı R). Kahverengi renkle gösterilir. |
| **L2 fazı** | `L2` | L2 | 230 V faz iletkeni (eski adı S). Siyah renkle gösterilir. |
| **L3 fazı** | `L3` | L3 | 230 V faz iletkeni (eski adı T). Gri renkle gösterilir. |
| **Nötr (N)** | `N` | N | Nötr iletkeni. Faz ile nötr arası 230 V'tur. Mavi renkle gösterilir. |
| **Koruma iletkeni (PE)** | `PE` | PE | Koruma iletkeni (toprak). Metal gövdeler buna bağlanır. Sarı-yeşil renkle gösterilir. |
| **Kumanda trafosu** | `T1` | 1 2 3 4 | Şebeke gerilimini kumanda gerilimine (24 V) düşürür ve kumanda devresini şebekeden yalıtır. Sekonder kısa devresine karşı sekondere sigorta konmalıdır. |

## Koruma

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Sigorta** | `F1` | 1 2 | Akım anma değerini çok aşınca içindeki tel erir. Atınca "Sigortayı değiştir" ile yenisi takılır. |
| **Sigorta (3 kutuplu)** | `F1` | 1 2 3 4 5 6 | Üç fazlı bıçaklı/buşonlu sigorta takımı. |
| **W otomatı (1 kutuplu)** | `F1` | 1 2 | Aşırı akımda (termik) ve kısa devrede (manyetik, B: 4×, C: 8×, D: 15× anma akımı) açar. Simülasyonda koluna tıklayarak açıp kapatın. |
| **W otomatı (3 kutuplu)** | `F1` | 1 2 3 4 5 6 | Üç fazı birlikte koruyan W otomatı. Bir fazda kısa devre olsa da üç kutup birlikte açar. |
| **Motor koruma şalteri** | `Q1` | 1 2 3 4 5 6 | Motoru hem aşırı yüke (termik) hem kısa devreye (manyetik) karşı korur ve elle açılıp kapanır. Ayar akımını motorun anma akımına getirin. |
| **Termik röle** | `F1` | 1 2 3 4 5 6 | Motor akımını ısıtıcılarıyla ölçer. Akım ayar değerini uzun süre aşarsa açar: 95-96 kontağı kumanda devresini keser. Soğuduktan sonra resetlenir. |
| **Faz koruma rölesi** | `F1` | L1 L2 L3 11 12 14 | Üç faz da varsa ve sıra doğruysa çıkış rölesi çeker (11-14 kapanır). Faz eksik ya da sıra ters ise motorun çalışmasını engeller. |
| **Kaçak akım rölesi (2 kutuplu)** | `F1` | 1 2 N N' | Fazdan giden ve nötrden dönen akım farklıysa (kaçak akım, 30 mA) açar. İnsanı elektrik çarpmasından korur. "Test" düğmesiyle denenir. |
| **Kaçak akım rölesi (4 kutuplu)** | `F1` | 1 2 3 4 5 6 N N' | Üç fazlı devrelerde kaçak akım koruması: L1, L2, L3 ve N akımlarının toplamı sıfır değilse açar. |

## Butonlar ve şalterler

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Işıklı buton** | `S1` | 13 14 X1 X2 | Başlatma butonu ile içinde bir sinyal lambası. Lamba ayrı uçlardan (X1-X2) beslenir, genelde kontaktör çekince yanar. |
| **Stop butonu (NC)** | `S1` | 11 12 | Basılınca devreyi açar. Durdurma devrelerinde seri bağlanır. |
| **Start butonu (NO)** | `S1` | 13 14 | Basılı tutulduğu sürece kontağı kapalıdır. Simülasyonda fareyle basılı tutun; Shift ile tıklarsanız basılı kalır. |
| **Jumelli buton (NO + NC)** | `S1` | 11 12 23 24 | Tek basışta üstteki NC kontağı açar, alttaki NO kontağı kapatır. Buton kilitlemesinde kullanılır. |
| **0-1-2 pako şalter** | `Q1` | 1 3 5 2 4 6 8 10 12 | Üç kutbu 1. konumda 2-4-6 uçlarına, 2. konumda 8-10-12 uçlarına bağlar. İki devirli motor ya da iki devre seçimi için. |
| **İleri-geri pako şalter** | `Q1` | 1 3 5 2 4 6 | Motoru elle ileri (1) ve geri (2) döndürür; 2. konumda iki faz yer değiştirir. Kontaktörsüz yön değiştirme için. |
| **Yıldız-üçgen pako şalter** | `Q1` | L1 L2 L3 U1 V1 W1 U2 V2 W2 | Motoru elle önce yıldızda (Y), sonra üçgende (Δ) çalıştırır. Y'de U2-V2-W2 köprülenir. |
| **Acil stop butonu** | `S1` | 11 12 | Basınca kilitlenir ve devreyi açar; yeniden çalıştırmak için çevrilerek (tekrar tıklanarak) bırakılmalıdır. |
| **Sınır anahtarı (değiştirici)** | `B1` | 11 14 12 | Makinenin hareketli parçası dokununca konum değiştirir. Simülasyonda tıklayarak tetikleyin. |
| **Sınır anahtarı (NO + NC)** | `B1` | 11 12 23 24 | Bir NC ve bir NO kontağı olan sınır anahtarı. |
| **Seçici şalter (0-1)** | `S1` | 13 14 21 22 | Kalıcı konumlu anahtar: 0'da 21-22, 1'de 13-14 kapalı. El/otomatik seçimi gibi işlerde kullanılır. |
| **Seçici şalter (1-0-2)** | `S1` | 13 14 23 24 | Üç konumlu anahtar: 1'de 13-14, 2'de 23-24 kapanır, 0'da ikisi de açık. |
| **Üç kutuplu şalter** | `Q1` | 1 2 3 4 5 6 | Elle açılıp kapatılan üç kutuplu şalter. Tıklayarak konum değiştirin. |
| **Üç kutuplu iki konumlu şalter** | `Q1` | 1 2 3 4 5 6 7 8 9 | Her kutup iki konum arasında geçer; ileri-geri pako şalter gibi kullanılabilir. |

## Röleler ve kontaktörler

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Sayıcı** | `K1` | A1 A2 R1 R2 | Sayma bobinine (A1-A2) gelen her darbeyi sayar; ayar değerine ulaşınca kontakları değişir. R1-R2'ye gerilim gelince sıfırlanır. |
| **Flaşör rölesi** | `K1` | A1 A2 | Bobini enerjili kaldıkça kontaklarını ayarlanan sürelerle açıp kapatır (yanıp sönen ikaz lambası). |
| **Kontaktör / röle bobini** | `K1` | A1 A2 | Bobine anma gerilimi uygulanınca kontaktör çeker; aynı etiketli bütün kontaklar konum değiştirir. Bobin uçları A1-A2. |
| **Zaman rölesi (düşmede gecikmeli)** | `KT1` | A1 A2 | Bobin enerjilenince kontaklar hemen değişir; enerji kesildikten sonra ayarlanan süre kadar bekleyip geri döner. |
| **Zaman rölesi (çekmede gecikmeli)** | `KT1` | A1 A2 | Bobin enerjilenince ayarlanan süre kadar bekler, sonra gecikmeli kontakları değiştirir. Enerji kesilince hemen geri döner. |
| **Yıldız-üçgen zaman rölesi** | `KT1` | A1 A2 17 18 28 | Bobin enerjilenince 17-18 (yıldız) kapanır; süre dolunca açılır, 50 ms sonra 17-28 (üçgen) kapanır. Bu bekleme iki kontaktörün aynı anda kapalı kalmasını önler. |

## Kontaklar

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Üç kutuplu kapalı kontak** | `K1` | 1 2 3 4 5 6 | Üç kutuplu normalde kapalı kontak. |
| **Ana kontaklar (3 kutup, NO)** | `K1` | 1 2 3 4 5 6 | Kontaktörün güç devresindeki ana kontakları (1-2, 3-4, 5-6). Motoru şebekeye bağlar. |
| **Değiştirici kontak** | `K1` | 11 12 14 | Ortak uç (11) dinlenmede 12'ye, bobin çekince 14'e bağlanır. |
| **Normalde kapalı kontak (NC)** | `K1` | 21 22 | Bobin çekmediğinde kapalıdır, çekince açılır. Karşılıklı kilitleme için kullanılır. Numaraları 11-12, 21-22... |
| **Normalde açık kontak (NO)** | `K1` | 13 14 | Bağlı olduğu bobin (aynı etiket) çekince kapanır. Mühürleme ve kilitleme devrelerinde kullanılır. Numaraları 13-14, 23-24... |
| **Termik röle kontakları (95-96, 97-98)** | `F1` | 95 96 97 98 | Termik rölenin kontakları. 95-96 (NC) kontaktör bobinine seri bağlanır; 97-98 (NO) arıza lambasını yakar. Etiketi termik röleyle aynı olmalı (F2). |
| **Termik kontağı 95-96 (NC)** | `F1` | 95 96 | Termik rölenin NC kontağı: kontaktör bobinine seri bağlanır, termik atınca açılır ve motoru durdurur. Etiketi termik röleyle aynı olmalı (F2). |
| **Termik kontağı 97-98 (NO)** | `F1` | 97 98 | Termik rölenin NO kontağı: termik atınca kapanır, arıza lambasını ya da kornayı çalıştırır. |

## Sensörler

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Şamandıra (seviye şalteri)** | `B1` | 11 12 14 | Depodaki sıvı seviyesi üst değere çıkınca kontağını değiştirir, alt değere inince geri döner. Simülasyonda seviyeyi sağdaki kaydırıcıyla ayarlayın. |
| **Fotosel (ışık sensörü)** | `B1` | 11 12 14 | Işık şiddeti ayarın altına inince (hava kararınca) tetikler; sokak ve bahçe aydınlatmasında kullanılır. |
| **Basınç şalteri** | `B1` | 11 12 14 | Basınç ayar değerine çıkınca kontağını değiştirir, ayar farkı kadar düşünce geri döner (kompresör, hidrofor). |
| **Kapasitif sensör** | `B1` | BN + BK BU − | Metal olmayan cisimleri (sıvı, plastik, tahıl) da algılar. Bağlantısı endüktif sensörle aynıdır. |
| **Endüktif sensör** | `B1` | BN + BK BU − | Metal bir cisim yaklaşınca çıkış verir. 3 kablolu, 24 V DC: kahverengi (+), mavi (−), siyah (çıkış). PNP çıkış yükü +'ya, NPN çıkış −'ye bağlar. Simülasyonda tıklayarak cisim yaklaştırın. |
| **Termostat** | `B1` | 11 12 14 | Isıtma modunda sıcaklık ayarın altındayken tetiklidir (ısıtıcı çalışır), ayarı geçince bırakır. Soğutma modunda tersidir. |

## Lambalar ve yükler

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Vantilatör (fan)** | `M1` | X1 X2 | Gerilim gelince hızlanarak dönen küçük bir fazlı motor (havalandırma, pano soğutma). |
| **Isıtıcı (rezistans)** | `E1` | X1 X2 | Elektrik enerjisini ısıya çeviren dirençli yük. Gücü özelliklerden ayarlanır. |
| **Korna / buzzer / siren** | `P1` | X1 X2 | Gerilim gelince ses verir. Korna, buzzer, siren ve zil olarak seçilebilir; ses Görünüm sekmesinden kapatılabilir. |
| **Sinyal lambası** | `H1` | X1 X2 | Gerilim uygulanınca yanar. Rengi ve anma gerilimi özelliklerden değiştirilebilir. |
| **Selenoid valf** | `Y1` | A1 A2 | Bobini enerjilenince vanayı açar (su, hava, gaz). Bobin gerilimi kontaktör bobini gibi seçilir. |

## Motorlar

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Bir fazlı motor** | `M1` | U1 U2 Z1 Z2 | Ana sargı (U1-U2) ve kondansatörlü yardımcı sargı (Z1-Z2). Yön için yardımcı sargının uçları değiştirilir. |
| **Bir fazlı motor (santrifüj anahtarlı)** | `M1` | U1 U2 Z1 Z2 | Hızlanınca santrifüj anahtar yardımcı sargıyı devreden çıkarır. |
| **Ayrı sargılı iki devirli motor** | `M1` | 1U 1V 1W 2U 2V 2W PE | İki ayrı yıldız sargılı motor: 1U-1V-1W düşük, 2U-2V-2W yüksek devir. İki sargı aynı anda beslenmemelidir. |
| **Üç fazlı motor** | `M1` | U V W PE | Üç fazlı asenkron motor. Fazların sırası değişince ters döner; bir faz eksikse kalkamaz ve ısınır. |
| **Yıldız-üçgen motor (6 uçlu)** | `M1` | U1 V1 W1 W2 U2 V2 PE | Klemens kutusu açık motor. Yıldız için W2-U2-V2 birleştirilir; üçgen için U1-W2, V1-U2, W1-V2 bağlanır. |
| **Frenli motor** | `M1` | U V W PE B1 B2 | Fren bobini (B1-B2) enerjilenmeden motor dönemez. Bobin motorla birlikte beslenir; enerji kesilince fren motoru hemen durdurur (vinç, asansör). |
| **Dahlander motor (Δ/YY)** | `M1` | 1U 1V 1W 2U 2V 2W PE | Tek sargılı iki devirli motor. Düşük devir: 1U-1V-1W beslenir (üçgen). Yüksek devir: 2U-2V-2W beslenir ve 1U-1V-1W birbirine köprülenir (çift yıldız). |
| **DC motor** | `M1` | A1 + A2 − | Doğru akım motoru. Hızı gerilimle değişir; kutuplar (A1 +, A2 −) yer değiştirince ters döner. |

## Hazır sistemler

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **Kapı sistemi 1** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d | Motorla açılıp kapanan sürgülü kapı. Kapı tam açık ve tam kapalı konumda sınır anahtarlarına basar. |
| **Kapı sistemi 2** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d | Yukarı doğru açılan kapı. İki ucunda sınır anahtarı vardır. |
| **Asansör (5 durak)** | `SYS1` | U V W S1a S1b S1c S1d S2a S2b S2c S2d S3a S3b S3c S3d S4a S4b S4c S4d S5a S5b S5c S5d | Beş duraklı asansör. Kabin bir kata gelince o katın sınır anahtarına basar. |

## Diğer

| Eleman | Etiket | Klemensler | Açıklama |
|---|---|---|---|
| **PE barası** | `PE` | 1 2 3 4 5 6 | Bütün uçları birbirine bağlı toprak barası. Motor ve pano gövdelerinin koruma iletkenleri burada toplanır. |
| **Sayfa bağlantı oku** | `1` | 1 | Aynı adı taşıyan bütün oklar, başka sayfada olsalar bile, aynı iletkendir. Uzun kabloları ve sayfalar arası bağlantıları kısaltır. |
| **Klemens** | `X1` | 1 2 | Sıra klemensi: iki ucu iç bağlıdır. Pano ile dışarıdaki cihazların bağlantı noktası. Numarası özelliklerden verilir (X1:3). |

Toplam 71 eleman.
