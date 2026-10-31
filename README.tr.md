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
| **NET** | indirme, yükleme ve ping (ping tek başına da gösterilebilir) |

**Düzenler:** dikey (satırlar alt alta, sütunlar hizalı) veya yatay (tek satır, ekranın üstü için ideal). **Kompakt mod** yazıyı ve boşlukları küçültür. Her şey isteğe bağlıdır: overlay'e sağ tık → **Metrics** ile öğeleri anında açıp kapatabilirsin.

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

**Fan testi ve fan uyarısı:** **Test** düğmesi fanı birkaç saniye %100'de çalıştırır ve hızının gerçekten artıp artmadığını söyler (gevşek kabloyu anlamak için işe yarar). dHud'un kontrol ettiği bir fan dönmeyi bırakırsa bildirim alırsın (telefonuna da gider).

**Sıcaklık kaynağı:** her eğri CPU'yu, GPU'yu veya ikisinin yüksek olanını izleyebilir.

**Güvenlik:**

- Fan kontrolü **varsayılan olarak kapalıdır**, ilk açışta onay ister.
- CPU veya GPU **kritik sıcaklığa** (Ayarlar → Alerts) ulaşırsa dHud'un kontrol ettiği tüm fanlar **%100'e** çıkar.
- Eğrinin sıcaklık sensörü birkaç saniye okunamazsa fan %100'e çıkar.
- Fanlar ani değil kademeli yavaşlar (sürekli inip çıkmasın diye). Eğri sadece yukarı gidebilir: bir nokta solundaki noktadan düşük olamaz.
- Riskli her işlem (kontrolü açma, tüm fanlara hazır ayar uygulama, profil yükleme, fan testi) seçili dilde onay ister.
- dHud kapanırken veya çökerken fanlar BIOS / sürücüye geri verilir. *dHud aniden sonlandırılırsa (örneğin Görev Yöneticisi'nden), fanlar dHud'u yeniden açana veya bilgisayarı yeniden başlatana kadar son hızında kalır.*

Her anakart yazılımla fan kontrolüne izin vermez; o durumda dHud "kontrol edilebilir fan bulunamadı" der.

## Oyun FPS sayacı

*Ayarlar → Behavior → Game FPS.* Varsayılan kapalı, yönetici yetkisi gerekir.

- Önde duran **tam ekran / kenarlıksız** oyunun FPS'ini ölçer; oyuna hiçbir şey enjekte etmez (yalnızca Windows'un kendi kare olaylarını dinler).
- Oyunun bulunduğu monitörün sol üst köşesinde küçük bir sayaç gösterir, yalnızca oyun çalışırken. Renk, kare hızına göre değişir.
- Aynı değer Android uygulamasında da görünür.
- DirectX 10 / 11 / 12 oyunlarında çalışır. DirectX 9 deneyseldir; Vulkan ve OpenGL oyunları ölçülmez.

## Android yardımcı uygulaması

Telefon veya tablette dHud'u **yerel ağında** (aynı Wi-Fi) yansıtan bir Android uygulaması. Aynı koyu temaya sahiptir.

**Ne yapar:** canlı CPU / GPU / RAM / ağ değerleri, 5 dakikalık sıcaklık grafiği, yüksek sıcaklık uyarıları (titreşim ve bildirim), oyun FPS'i ve tabletler için **tam ekran gösterge paneli modu** (şarjdayken ekran kapanmaz).

**Kurulum (bir kez):**

1. [Son sürümden](https://github.com/d0ukan/dHud-xverlay/releases/latest) `dHud-<sürüm>.apk` dosyasını telefona / tablete indir ve kur (Android bu kaynaktan kuruluma izin vermeni ister).
2. PC'de: *Ayarlar → Behavior → Mobil uygulama* → **dHud mobil uygulamasının bağlanmasına izin ver** (Allow the dHud mobile app to connect) kutusunu işaretle. Bir QR kod görünür. Windows Güvenlik Duvarı sorarsa **özel ağlar** için izin ver.
3. Android uygulamasında **QR kodu tara**'ya dokun ve QR'ı okut. Tamam: üstteki nokta yeşile döner.

**Telefondan fan:** uygulamada ayrı bir **Fanlar** sekmesi var. Kullanmak için QR kodun altındaki **Mobil uygulama fan ayarlarını değiştirebilsin** kutusunu işaretle (varsayılan kapalı, yani telefon sadece izler) ve PC'de fan kontrolünü aç. Telefon yalnızca hazır seçenekleri seçebilir: her fan için *Otomatik*, hazır eğri (*Sessiz / Optimal / Max Cold*) veya kayıtlı eğrilerinden biri; ya da tüm fanları tek seferde ayarlayan kayıtlı bir **profil**. Telefondan elle hız verilemez. PC'deki Fan kontrolü penceresinde her fanın **Telefondan değiştirilebilsin** kutusu var; işareti kalkan fan telefondan hiç değiştirilemez (ne tek tek, ne kayıtlı eğriyle, ne profille). PC'deki güvenlik kuralları (acil %100, alt sınır, kademeli yavaşlama) aynen geçerlidir ve PC her değişiklikte bildirim gösterir.

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

dHud telemetri göndermez. Tek internet isteği GitHub'a yapılan güncelleme denetimidir. Mobil bağlantı yerel ağında kalır ve eşleştirme anahtarı ister.

## Katkılar ve lisans

[MIT](LICENSE) © 2026 d0ukan. Sensörler [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) ile okunur; FPS Windows olay izleme ile ([TraceEvent](https://github.com/microsoft/perfview), MIT) ölçülür.
