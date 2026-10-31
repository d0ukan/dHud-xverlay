using System.Globalization;

namespace dHud.Mobile.Services;

/// <summary>Telefonun diline göre Türkçe veya İngilizce metin (varsayılan İngilizce).</summary>
public static class Loc
{
	private static readonly bool Turkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

	private static readonly Dictionary<string, (string En, string Tr)> Texts = new()
	{
		["NotPaired.Title"] = ("Connect to your PC", "PC'ne bağlan"),
		["NotPaired.Text"] = ("Open dHud on your PC, go to Settings → Behavior → Mobile app and scan the QR code.",
			"PC'deki dHud'da Ayarlar → Behavior → Mobil uygulama bölümünü aç ve QR kodu tara."),
		["ScanQr"] = ("Scan QR code", "QR kodu tara"),
		["Manual"] = ("Enter manually", "Elle gir"),
		["ManualAddress"] = ("PC address (e.g. 192.168.0.26)", "PC adresi (örn. 192.168.0.26)"),
		["ManualCode"] = ("Pairing code (the t= value in the QR link)", "Eşleştirme kodu"),
		["Connecting"] = ("Connecting…", "Bağlanıyor…"),
		["Connected"] = ("Connected · {0}", "Bağlı · {0}"),
		["Offline"] = ("PC not reachable – retrying…", "PC'ye ulaşılamıyor – yeniden deneniyor…"),
		["Rejected"] = ("Pairing is no longer valid. Scan the QR code again.", "Eşleştirme geçersiz. QR kodu yeniden tara."),
		["Usage"] = ("Usage", "Kullanım"),
		["Clock"] = ("Clock", "Frekans"),
		["Power"] = ("Power", "Güç"),
		["Fan"] = ("Fan", "Fan"),
		["Vram"] = ("VRAM", "VRAM"),
		["Download"] = ("Download", "İndirme"),
		["Upload"] = ("Upload", "Yükleme"),
		["Ping"] = ("Ping", "Ping"),
		["Graph"] = ("Temperature history", "Sıcaklık geçmişi"),
		["Menu"] = ("Menu", "Menü"),
		["Cancel"] = ("Cancel", "Vazgeç"),
		["Repair"] = ("Scan QR code again", "QR kodu yeniden tara"),
		["Forget"] = ("Forget this PC", "Bu PC'yi unut"),
		["CheckUpdate"] = ("Check for updates", "Güncellemeleri denetle"),
		["UpToDate"] = ("dHud Mobile is up to date ({0}).", "dHud Mobile güncel ({0})."),
		["UpdateAvail"] = ("Update available: {0}", "Güncelleme var: {0}"),
		["Install"] = ("Install", "Kur"),
		["Downloading"] = ("Downloading…", "İndiriliyor…"),
		["AllowInstall"] = ("Allow dHud to install updates in the next screen, then try again.",
			"Sonraki ekranda dHud'un güncelleme kurmasına izin ver, sonra tekrar dene."),
		["UpdateFailed"] = ("Update failed: {0}", "Güncelleme başarısız: {0}"),
		["ScanHint"] = ("Point the camera at the QR code shown in dHud on your PC.", "Kamerayı PC'deki dHud'un gösterdiği QR koda tut."),
		["BadQr"] = ("This is not a dHud QR code.", "Bu bir dHud QR kodu değil."),
		["NoCamera"] = ("Camera permission is needed to scan the QR code.", "QR kodu taramak için kamera izni gerekli."),
		["Ok"] = ("OK", "Tamam")
	};

	public static string T(string key, params object[] args)
	{
		var text = Texts.TryGetValue(key, out var pair) ? (Turkish ? pair.Tr : pair.En) : key;
		return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
	}
}
