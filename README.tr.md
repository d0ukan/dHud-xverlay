<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">Windows için hafif, şeffaf ve her zaman üstte duran donanım overlay'i.<br />Fan kontrolü, oyun FPS sayacı ve Android yardımcı uygulaması ile birlikte.</p>

<p align="center"><a href="README.md">English</a> · <b>Türkçe</b></p>

---

## dHud nedir?

dHud, bilgisayarının durumunu pencerelerin üstünde duran küçük bir overlay'de gösterir: CPU ve GPU sıcaklığı, kullanım, frekans, güç, fan hızı, bellek, ağ ve ping. İşlemciyi ve belleği az kullanacak, işine karışmayacak şekilde tasarlandı (tıklama geçirme, kısayollar, kenara yapışma).

Overlay'e ek olarak dHud şunları da yapabilir:

- kendi çizdiğin eğrilerle **fanlarını kontrol edebilir**,
- tam ekran oyunlarda **FPS sayabilir**,
- her şeyi ev ağında **telefonuna veya tabletine aktarabilir** (Android uygulaması): sıcaklık uyarıları ve tam ekran gösterge paneli ile.

## Hızlı başlangıç

1. [Son sürümden](https://github.com/d0ukan/dHud-xverlay/releases/latest) kurulum dosyasını indir ve çalıştır.
2. dHud'u aç. Overlay görünür, istediğin yere sürükle.
3. Overlay'deki ⚙ simgesine (veya tepsi simgesine çift tıklayarak) **Ayarlar**'ı aç.

> CPU sıcaklığı için dHud'un **yönetici yetkisi** ve ücretsiz [PawnIO](https://pawnio.eu) sürücüsü gerekir. Ayarlar penceresi ikisinden biri eksikse seni uyarır.

## Ne görürsün?

**Overlay'de** her donanım için bir satır vardır. Her satır sıcaklığı (eşiklerine göre yeşil / sarı / kırmızı), kullanımı, frekansı, güç tüketimini, fan hızını ve küçük bir sıcaklık grafiğini gösterebilir.

| Satır | Gösterdiği |
| --- | --- |
| **CPU** | sıcaklık, kullanım, frekans, güç, fan |
| **GPU** | sıcaklık, kullanım, frekans, güç, fan, VRAM |
| **RAM** | kullanım ve kullanılan / toplam bellek |
| **DISK** | sistem sürücüsü: dolu % ve dolu / toplam alan (isteğe bağlı) |
| **CORE** | en yoğun CPU çekirdeği ve tüm çekirdeklerin ortalaması (isteğe bağlı) |
| **NET** | indirme, yükleme ve ping (ping tek başına da gösterilebilir) |

**Düzenler:** dikey (satırlar alt alta, sütunlar hizalı) veya yatay (tek satır, ekranın üstü için ideal). **Kompakt mod** yazıyı ve boşlukları küçültür. Her şey isteğe bağlıdır: overlay'e sağ tık → **Metrics** ile öğeleri anında açıp kapatabilirsin.

Üstteki ince bar isteğe bağlıdır: sağ tıkla → **Bar'ı kapat**, ya da Ayarlar → Görünüm'de *Üstteki monitör bar'ını göster* kutusunu kaldır (aynı değerleri gösteren Sistem widget'ını tercih edersen işe yarar). Ayarlardan, tepsi simgesinden veya Ctrl+Shift+F9 ile geri gelir. Kapalıyken dHud yalnızca bar'ın gerektirdiği değerleri okumayı bırakır.

## Kontroller ve kısayollar

| İşlem | Nasıl |
| --- | --- |
| Overlay'i taşı | Sol tıkla ve sürükle (ekran dışına çıkarılamaz, ekran kenarlarına yapışır) |
| Ayarları aç | Overlay'deki ⚙, sağ tık → Settings veya tepsi simgesine çift tık |
| Overlay'i gizle / göster | `Ctrl` + `Shift` + `F9` |
| Tıklama geçirmeyi aç / kapat | `Ctrl` + `Shift` + `F10` (fare tıklamaları alttaki pencereye geçer) |
| Hızlı menü | Overlay'e sağ tık: düzen, kompakt, konumu kilitle, göstergeler, konumu sıfırla |
| Tepsi menüsü | Tepsi simgesine sağ tık: gizle / göster, tıklama geçirme, kilit, güncelleme denetimi, çıkış |

> Overlay *özel tam ekran* (exclusive fullscreen) oyunların üstünde görünemez. Oyunu kenarlıksız / pencereli tam ekran moduna al.

## Ayarlara kısa bakış

| Sekme | İçinde ne var |
| --- | --- |
| **Appearance** (Görünüm) | düzen, kompakt mod, her zaman üstte, arka plan saydamlığı, boyut, dil (English / Türkçe) |
| **Metrics** (Göstergeler) | gösterilecek öğeler (CPU, GPU, kullanım, grafik, frekans, güç, fan, VRAM, RAM, ağ, ping), yenileme hızı |
| **Behavior** (Davranış) | Windows ile başlat, tıklama geçirme, konum kilidi, kısayollar, güncelleme denetimi; ayrıca **Fan kontrolü**, **Oyun FPS** ve **Mobil uygulama** |
| **Alerts** (Uyarılar) | uyarı / kritik sıcaklık eşikleri (CPU ve GPU için ayrı), sıcaklık renkleri, sıcaklık kritik kalınca bildirim |

## Fan kontrolü

*Ayarlar → Behavior → Fan control → Open fan control…* (yönetici yetkisi gerekir).

dHud kontrol edebildiği tüm fanları listeler (CPU fanı, pompa, kasa fanları, GPU fanları). Her biri için şunlardan birini seç:

- **Otomatik**: dHud dokunmaz (BIOS / sürücü kontrolde kalır).
- **Manuel**: sabit bir hız.
- **Eğri**: hız sıcaklığa göre değişir. Eğri editörünü aç, eğriyi şekillendirmek için **noktaları sürükle**, boş bir yere tıklayıp nokta ekle, bir noktaya sağ tıklayıp sil. Yeşil işaretçi şu anki sıcaklığı gösterir. Sıcaklık kaynağını (CPU veya GPU) ve minimum hızı seçebilirsin.

**Hazır ayarlar:** *Sessiz*, *Optimal* ve *Max Soğutma* sana ince ayar yapabileceğin bir başlangıç eğrisi verir. Fan penceresinin üstündeki düğmeler hazır ayarı **tüm fanlara tek seferde** uygular (GPU fanları GPU'yu, CPU fanı CPU'yu, diğerleri CPU ve GPU'nun yüksek olanını izler). Yavaş pompa CPU'yu ısıtabileceği için *Pompayı da dahil et* kutusunu işaretlemezsen pompalar atlanır.

**Kendi eğrilerin ve profiller:** herhangi bir eğriyi adla kaydet (*Kayıtlı eğrilerim*) ve her fanda kullan. **Tüm fanların** ayarını bir *profil* olarak ("Oyun", "Gece"…) kaydet ve tek tıkla yükle.

**Fan testi ve fan uyarısı:** **Test** düğmesi fanı birkaç saniye %100'de çalıştırır ve hızının gerçekten artıp artmadığını söyler (gevşek kabloyu anlamak için işe yarar). dHud'un kontrol ettiği bir fan dönmeyi bırakırsa bildirim alırsın (telefonuna da gider). Mesaj yağmasın diye aynı anda duran fanlar tek mesajda bildirilir, aynı fan en fazla 6 saatte bir, genel olarak da en fazla 30 dakikada bir mesaj gelir; %40'ın altındaki hızlar yok sayılır (birçok fan orada kendiliğinden durur). Uyarıyı Fan kontrolü penceresindeki **Bir fan dönmeyi bırakırsa bildir** kutusuyla kapatabilirsin.

**Sıcaklık kaynağı:** her eğri CPU'yu, GPU'yu veya ikisinin yüksek olanını izleyebilir.

**Güvenlik:**

- Fan kontrolü **varsayılan olarak kapalıdır**, ilk açışta onay ister.
- CPU veya GPU **kritik sıcaklığa** (Ayarlar → Alerts) ulaşırsa dHud'un kontrol ettiği tüm fanlar **%100'e** çıkar.
- Eğrinin sıcaklık sensörü birkaç saniye okunamazsa fan %100'e çıkar.
- Fanlar ani değil kademeli yavaşlar (sürekli inip çıkmasın diye). Eğri sadece yukarı gidebilir: bir nokta solundaki noktadan düşük olamaz.
- Riskli her işlem (kontrolü açma, tüm fanlara hazır ayar uygulama, profil yükleme, fan testi) seçili dilde onay ister.
- dHud kapanırken veya çökerken fanlar BIOS / sürücüye geri verilir. *dHud aniden sonlandırılırsa (örneğin Görev Yöneticisi'nden), küçük bir yardımcı süreç (aynı dHud.exe, `--fan-watchdog` ile başlar, penceresi yok) bunu fark eder ve kontrol ettiği fanları güvenli sabit %70'e çıkarır. dHud bir sonraki açılışta fanları geri devralır (veya otomatiğe verir) ve sana haber verir. Yardımcı yalnızca dHud fan kontrol ederken çalışır; normal kapanışta hiçbir şey yapmaz.*

Her anakart yazılımla fan kontrolüne izin vermez; o durumda dHud "kontrol edilebilir fan bulunamadı" der.

## Oyun FPS sayacı

*Ayarlar → Behavior → Game FPS.* Varsayılan kapalı, yönetici yetkisi gerekir.

- Önde duran **tam ekran / kenarlıksız** oyunun FPS'ini ölçer; oyuna hiçbir şey enjekte etmez (yalnızca Windows'un kendi kare olaylarını dinler).
- Oyunun bulunduğu monitörün sol üst köşesinde küçük bir sayaç gösterir, yalnızca oyun çalışırken. Renk, kare hızına göre değişir.
- Aynı değer Android uygulamasında da görünür.
- **Oyun özeti:** en az 2 dakikalık bir oyun oturumu bitince sağ alt köşede küçük bir kart çıkar: ortalama FPS, %1 düşük FPS, en düşük / en yüksek FPS ve en yüksek CPU / GPU sıcaklığı. *Oyun bitince özet kartı göster* kutusuyla kapatılır.
- DirectX 10 / 11 / 12 oyunlarında çalışır. DirectX 9 deneyseldir; Vulkan ve OpenGL oyunları ölçülmez.

## Profiller ve yeni bilgisayara taşıma

*Ayarlar → Profiller.*

- **Profiller:** tüm ayarlarını (görünüm, metrikler, widget'lar, uyarılar, fan eğrileri ...) bir ada kaydet ve tek tıkla geç; tepsi simgesinden de (**Profiller** alt menüsü). Notlar ve telefon eşleştirmesi profile dahil değildir.
- **Yedekle / başka bilgisayara taşı:** *Her şeyi dışa aktar…* tek bir `.dhudprofile` dosyası yazar. Yeni bilgisayara dHud'u kur ve *İçe aktar…*'ı seç – kaldığın yerden aynen devam edersin. Notlarını, yapılacakları, gün sayaçlarını ve başlatıcı öğelerini; istersen telefon eşleştirme anahtarını da ekleyebilirsin (telefonun eşleşmiş kalır).
- **Güvenlik:** içe aktarma fan kontrolünü kendiliğinden açmaz (Fan kontrolü penceresinden yeniden aç) ve telefon anahtarı yalnızca kutuyu işaretlersen dosyadan alınır. Başlatıcı yolları ve fan sensör adları eski bilgisayara aittir, taşıdıktan sonra kontrol et.

## Widget'lar

*Ayarlar → Widget'lar.* Masaüstünde istediğin yere sürükleyebileceğin küçük koyu kartlar. Hepsi varsayılan kapalıdır ve neredeyse hiç CPU kullanmaz (saat dakikada bir uyanır, kronometre yalnızca çalışırken yenilenir). Her widget'ın kendi bölümü vardır: **görünürlük** (kart saydamlığı), **hep üstte**, **konumu kilitle**, **ekran kenarına yapış** ve hızlı **şuraya taşı** düğmeleri (köşeler / orta). Widget'lar ince bir **ızgaraya** oturur (yaklaşık 20 piksellik hücreler, ekranın genişliğinde 96 sütun; her çözünürlükte benzer görünür ve komşular birbirine yakın durur), macOS'taki masaüstü widget'ları gibi: sürüklerken oturacağı hücreler vurgulanır, bırakınca widget en yakın boş hücrelere yerleşir. Widget'lar birbirinin ya da üstteki bar'ın üstüne binmez ve ekranın dışına çıkarılamaz. Serbest yerleşim için bir widget'ta *Izgaraya otur* kutusunu kaldır (yine de üst üste binmez). Ayarlar → Widget'lar'da her widget küçük bir karttır, aç/kapat anahtarı vardır; kartta **Ayarlar**'a tıklayınca görünürlük, hep üstte, kilit ve taşıma düğmeleri açılır.

- **Saat:** saat ve tarih; isteğe bağlı saniye ve ikinci saat dilimi (sağ tık).
- **Yapışkan notlar:** istediğin kadar (en fazla 30), **klasör sekmeleri** gibi üst üste dizilir: her notun başlık şeridi (ilk satırı) bir sonrakinin üstünden görünür; bir sekmeye tıklayınca o not öne gelir, bir sekmeyi sürüklemek tüm yığını taşır. Her notun ayrı yüzmesini istersen *Notları klasör sekmesi gibi üst üste diz* kutusunu kaldır. Yazdıkça kaydedilir; her notun kendi rengi, boyutu ve *hep üstte tut* düğmesi vardır. Tepsi menüsünden veya Ayarlar'dan yeni not açılır; hepsi silinmeden gizlenebilir.
- **Kronometre:** başlat / duraklat, tur ve sıfırla.
- **Sistem özeti:** CPU, GPU, VRAM, RAM, disk ve ağ değerleri ince çubuklarla tek büyük kartta (overlay'den bağımsız).
- **Yapılacaklar listesi:** Enter ile iş ekle, tik at, bitenleri temizle.
- **Gün sayacı:** herhangi bir tarih için "12 gün kaldı" (tatil, sınav, doğum günü…).
- **Hızlı başlatıcı:** en sevdiğin uygulama, dosya ve klasörler simge olarak. Öğeler Windows Gezgini üzerinden açılır; dHud yönetici olarak çalışsa bile uygulamalar normal (yönetici olmayan) yetkiyle başlar.
- **Pano geçmişi:** son 10 kopyalanan metin; birine tıklayınca tekrar kopyalanır. Yalnızca bellekte tutulur, diske yazılmaz, widget kapalıyken hiçbir şey toplanmaz ve parola yöneticilerinin gizli işaretlediği kopyalar atlanır.
- **Ses ve mikrofon:** varsayılan aygıtlar için ses kaydırıcısı, sessize alma ve mikrofonu kapatma düğmesi.
- **Takvim:** bir `.ics` dosyasından veya bağlantısından önümüzdeki 7 gün (tekrarlayan etkinlikler dahil). Hesap girişi yok: bir dosya ya da takviminin verdiği gizli iCal adresi kullanılır.
- **Hava durumu:** seçtiğin şehir için şimdiki sıcaklık, hissedilen, rüzgâr ve 3 günlük tahmin (°C veya °F). İnternet kullanan tek widget'tır (bkz. *Gizlilik*), varsayılan kapalıdır ve şehir seçene kadar hiçbir şey yapmaz. (İndirilen tek şey daha var: bir takvim *bağlantısı* girersen o indirilir, girmezsen hiçbir şey olmaz.)
- **Zamanlayıcı, hatırlatıcı ve pomodoro:** hızlı zamanlayıcılar (5 / 10 / 15 / 25 / 45 dk veya kendi süren), saate göre hatırlatıcı (saat geçtiyse yarın) ve pomodoro (25 dk odak, 5 dk mola, 4 turdan sonra uzun mola). Süre dolunca bildirim ve ses alırsın; istersen telefona da bildirim gider.

Notlar yazdıkça kaydedilir (saniyenin kesri içinde; ayrıca nottan çıkınca, dHud kapanırken ve Windows kapanırken tekrar); dosya güvenli şekilde değiştirilir ve bir `.bak` kopyası tutulur, yani elektrik kesilse bile yarım dosya kalmaz. Notlar ve hatırlatıcılar yalnızca `%AppData%\dHud\widgets.json` dosyasında saklanır. Overlay gibi widget'lar da özel tam ekran oyunların üstünde görünemez.

## Android yardımcı uygulaması

Telefon veya tablette dHud'u **yerel ağında** (aynı Wi-Fi) yansıtan bir Android uygulaması. Aynı koyu temaya sahiptir.

**Ne yapar:** canlı CPU / GPU / RAM / disk / ağ değerleri (ve en yoğun CPU çekirdeği), 5 dakikalık sıcaklık grafiği, yüksek sıcaklık uyarıları (titreşim ve bildirim), zamanlayıcı / hatırlatıcı bildirimleri ve oyun bitince kısa bir özet, oyun FPS'i ve tabletler için **tam ekran gösterge paneli modu** (şarjdayken ekran kapanmaz). Tam ekran modunda ekranı korumak için içerik birkaç pikseli yavaşça kayar ve 3 dakika dokunulmazsa kararır (uyandırmak için dokun); uygulama menüsünden (⋮) kapatılabilir. Ayrıca **ana ekran widget'ı** ekleyebilirsin (CPU / GPU sıcaklığı ve FPS): ana ekrana uzun bas → Widget'lar → dHud. Uygulama açıkken canlı, kapalıyken yaklaşık 30 dakikada bir (Android sınırı) veya ⟳'ya dokununca yenilenir.

**Kurulum (bir kez):**

1. [Son sürümden](https://github.com/d0ukan/dHud-xverlay/releases/latest) `dHud-<sürüm>.apk` dosyasını telefona / tablete indir ve kur (Android bu kaynaktan kuruluma izin vermeni ister).
2. PC'de: *Ayarlar → Behavior → Mobil uygulama* → **dHud mobil uygulamasının bağlanmasına izin ver** (Allow the dHud mobile app to connect) kutusunu işaretle. Bir QR kod görünür. Windows Güvenlik Duvarı sorarsa **özel ağlar** için izin ver.
3. Android uygulamasında **QR kodu tara**'ya dokun ve QR'ı okut. Tamam: üstteki nokta yeşile döner.

**Telefondan fan:** uygulamada ayrı bir **Fanlar** sekmesi var. Kullanmak için QR kodun altındaki **Mobil uygulama fan ayarlarını değiştirebilsin** kutusunu işaretle (varsayılan kapalı, yani telefon sadece izler) ve PC'de fan kontrolünü aç. Telefon yalnızca hazır seçenekleri seçebilir: her fan için *Otomatik*, hazır eğri (*Sessiz / Optimal / Max Cold*) veya kayıtlı eğrilerinden biri; ya da tüm fanları tek seferde ayarlayan kayıtlı bir **profil**. Telefondan elle hız verilemez. Fanlar sekmesinde ayrıca PC'deki fan kontrolünü açıp kapatan bir **Fan kontrolü** anahtarı vardır (açarken PC'dekiyle aynı onay sorulur). PC'deki Fan kontrolü penceresinde her fanın **Telefondan değiştirilebilsin** kutusu var; işareti kalkan fan telefondan hiç değiştirilemez (ne tek tek, ne kayıtlı eğriyle, ne profille). PC'deki güvenlik kuralları (acil %100, alt sınır, kademeli yavaşlama) aynen geçerlidir ve PC her değişiklikte bildirim gösterir.

PC'nin IP adresi değişirse uygulama PC'yi kendiliğinden yeniden bulur. Uygulama yeni sürümleri de denetler ve tek dokunuşla güncelleme sunar. QR kodun altındaki **Eşleştirmeyi sıfırla** (Reset pairing) düğmesi tüm cihazların bağlantısını keser.

Yalnızca eşleştirme anahtarını bilen cihazlar bağlanabilir; internete hiçbir şey gönderilmez. dHud yerel ağında TCP `47821` ve UDP `47822` portlarını kullanır.

## Kurulum

[Son sürümden](https://github.com/d0ukan/dHud-xverlay/releases/latest) indir:

- **`dHud-Setup-<sürüm>.exe`**: kurulum dosyası. Kullanıcı klasörüne (`%LocalAppData%\Programs\dHud`) kurulur, yönetici yetkisi gerekmez; Başlat menüsü kısayolu ve kaldırıcı ekler.
- **`dHud-<sürüm>.exe`**: taşınabilir tek dosya, istediğin yerden çalıştır.
- **`dHud-<sürüm>.apk`**: Android uygulaması.

dHud açılışta ve çalıştığı sürece birkaç saatte bir (Ayarlar → Behavior'dan değiştirilir) GitHub'da yeni sürüm arar, seni bilgilendirir ve tek tıkla günceller.

**Gereksinimler:** Windows 10 / 11 (.NET kurulumu gerekmez), uygulama için Android 7.0+.

> Uygulama henüz kod imzalı olmadığından Windows SmartScreen ilk açılışta uyarabilir. **Ek bilgi → Yine de çalıştır**'a tıkla.

## Sorun giderme

| Sorun | Çözüm |
| --- | --- |
| CPU sıcaklığı `--` gösteriyor | dHud'u yönetici olarak çalıştır ve [PawnIO](https://pawnio.eu) sürücüsünü kur (Ayarlar durumu gösterir). |
| Oyunda overlay görünmüyor | Özel tam ekran overlay'leri gizler. Oyunu kenarlıksız / pencereli tam ekrana al. |
| Fan listesi boş | Yönetici olarak çalıştır. Bazı anakartlar yazılımla fan kontrolüne izin vermez. |
| FPS bir şey göstermiyor | *Game FPS*'i aç, dHud'u yönetici çalıştır ve tam ekran / kenarlıksız bir DirectX oyunu kullan. |
| Telefon bağlanamıyor | PC ile aynı Wi-Fi'de misin? *Mobil uygulama* kutusu işaretli mi? dHud'a Windows Güvenlik Duvarı'nda (özel ağ) izin ver. *Eşleştirmeyi sıfırla* (Reset pairing) deyip yeniden tara. |
| Monitör değişince overlay ekran dışında kaldı | Overlay menüsü → **Reset position**. |

## Gizlilik

dHud telemetri göndermez. Tek internet istekleri GitHub'a yapılan güncelleme denetimi ve yalnızca isteğe bağlı hava durumu widget'ını açarsan open-meteo.com'a yapılan hava durumu sorgusudur (aradığın şehri ve koordinatlarını alır; hesap ve anahtar yok). Mobil bağlantı yerel ağında kalır ve eşleştirme anahtarı ister.

## Katkılar ve lisans

[MIT](LICENSE) © 2026 d0ukan. Sensörler [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) ile okunur; FPS Windows olay izleme ile ([TraceEvent](https://github.com/microsoft/perfview), MIT) ölçülür.
