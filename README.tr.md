<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">Windows için hafif, şeffaf ve her zaman üstte duran donanım overlay'i.</p>

<p align="center"><a href="README.md">English</a> · <b>Türkçe</b></p>

---

## Özellikler

- CPU / GPU sıcaklığı, kullanım, frekans, güç, VRAM ve RAM
- Ağ indirme / yükleme hızı ve ping
- Dikey veya tek satır yatay düzen, kompakt görünüm, mini sıcaklık grafiği
- Sıcaklığa göre renk ve kritik sıcaklık bildirimi
- Tıklama geçirme, konum kilidi, kenara yapışma, kısayol tuşları
- Koyu temalı sistem tepsisi menüsü ve Windows başlangıcında çalıştırma
- GitHub Releases üzerinden otomatik güncelleme

## Gereksinimler

- Windows 10 / 11 (.NET kurulumu gerekmez, release exe'si .NET'i içinde taşır)
- CPU sıcaklığı için yönetici yetkisi ve [PawnIO](https://pawnio.eu) sürücüsü

## Derleme

```bash
dotnet run                 # geliştirme
dotnet publish -c Release  # bin\Release\publish\CpuGpuOverlay.exe (tek dosya, .NET dahil)
```

## Sürüm yayınlama ve güncelleme

Sürüm numarası `CpuGpuOverlay.csproj` içindeki `Version` değeridir (ilk sürüm: `1.0.0`). Yayın için:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Etiket push'lanınca GitHub Actions (`.github/workflows/release.yml`) tek dosyalık `CpuGpuOverlay.exe` dosyasını
derleyip Release olarak yayınlar. Uygulama açılışta (ayarlardan kapatılabilir) ve tepsi menüsünden
**Güncellemeleri denetle** ile en son Release'e bakar; yeni sürüm varsa onay alıp indirir, kendini değiştirir ve yeniden başlar.

## Kullanım

| | |
| --- | --- |
| Taşı | Sol tıkla sürükle |
| Ayarlar | ⚙ simgesi, sağ tık veya tepsi simgesine çift tık |
| `Ctrl+Shift+F9` | Overlay'i gizle / göster |
| `Ctrl+Shift+F10` | Tıklama geçirmeyi aç / kapat |

> Özel tam ekran (exclusive fullscreen) oyunlarda overlay görünmez; kenarlıksız pencere modunu kullanın.

Sensörler [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) ile okunur.
