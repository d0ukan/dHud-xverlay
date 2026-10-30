<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">Windows için hafif, şeffaf ve her zaman üstte duran donanım overlay'i.</p>

<p align="center"><a href="README.md">English</a> · <b>Türkçe</b></p>

---

## Özellikler

- CPU / GPU sıcaklığı, kullanım, frekans, güç, fan hızı (RPM), VRAM ve RAM
- Ağ indirme / yükleme hızı ve ping
- Dikey veya tek satır yatay düzen, kompakt görünüm, mini sıcaklık grafiği
- Sıcaklığa göre renk ve kritik sıcaklık bildirimi
- Tıklama geçirme, konum kilidi, kenara yapışma, kısayol tuşları
- Koyu temalı sistem tepsisi menüsü ve Windows başlangıcında çalıştırma
- İngilizce ve Türkçe arayüz (varsayılan İngilizce, Ayarlar → Görünüm'den değiştirilir)
- GitHub Releases üzerinden otomatik güncelleme

## Gereksinimler

- Windows 10 / 11 (.NET kurulumu gerekmez, release exe'si .NET'i içinde taşır)
- CPU sıcaklığı için yönetici yetkisi ve [PawnIO](https://pawnio.eu) sürücüsü

## Kurulum

[Son sürümden](https://github.com/d0ukan/dHud-xverlay/releases/latest) indir:

- **`dHud-Setup-<sürüm>.exe`**: kurulum dosyası. Kullanıcı klasörüne (`%LocalAppData%\Programs\dHud`) kurulur, yönetici yetkisi gerekmez; Başlat menüsü kısayolu ve kaldırıcı ekler.
- **`dHud-<sürüm>.exe`**: taşınabilir tek dosya, istediğin yerden çalıştır.

## Kullanım

| | |
| --- | --- |
| Taşı | Sol tıkla sürükle |
| Ayarlar | ⚙ simgesi, sağ tık veya tepsi simgesine çift tık |
| `Ctrl+Shift+F9` | Overlay'i gizle / göster |
| `Ctrl+Shift+F10` | Tıklama geçirmeyi aç / kapat |

> Özel tam ekran (exclusive fullscreen) oyunlarda overlay görünmez; kenarlıksız pencere modunu kullanın.

Sensörler [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) ile okunur.

## Lisans

[MIT](LICENSE) © 2026 d0ukan. Sensör okuma LibreHardwareMonitorLib (MPL-2.0) ile yapılır.
