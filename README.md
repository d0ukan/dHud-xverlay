<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">Windows için hafif, şeffaf ve her zaman üstte duran donanım overlay'i.</p>

---

## Özellikler

- CPU / GPU sıcaklığı, kullanım, frekans, güç, VRAM ve RAM
- Ağ indirme / yükleme hızı ve ping
- Dikey veya tek satır yatay düzen, kompakt görünüm, mini sıcaklık grafiği
- Sıcaklığa göre renk ve kritik sıcaklık bildirimi
- Tıklama geçirme, konum kilidi, kenara yapışma, kısayol tuşları
- Sistem tepsisi ve Windows başlangıcında çalıştırma

## Gereksinimler

- Windows 10 / 11, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- CPU sıcaklığı için yönetici yetkisi ve [PawnIO](https://pawnio.eu) sürücüsü

## Derleme

```bash
dotnet run                 # geliştirme
dotnet publish -c Release  # bin\Release\publish\CpuGpuOverlay.exe
```

## Kullanım

| | |
| --- | --- |
| Taşı | Sol tıkla sürükle |
| Ayarlar | ⚙ simgesi, sağ tık veya tepsi simgesine çift tık |
| `Ctrl+Shift+F9` | Overlay'i gizle / göster |
| `Ctrl+Shift+F10` | Tıklama geçirmeyi aç / kapat |

> Özel tam ekran (exclusive fullscreen) oyunlarda overlay görünmez; kenarlıksız pencere modunu kullanın.

Sensörler [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) ile okunur.
