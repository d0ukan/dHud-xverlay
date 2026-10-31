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
		["AlertTitle"] = ("dHud temperature alert", "dHud sıcaklık uyarısı"),
		["Yes"] = ("Yes", "Evet"),
		["No"] = ("No", "Hayır"),
		["Fans"] = ("FANS", "FANLAR"),
		["FanAll"] = ("All fans", "Tüm fanlar"),
		["FanAllAuto"] = ("Auto (give back to BIOS)", "Otomatik (BIOS'a geri ver)"),
		["FanAllConfirm"] = ("Apply \"{0}\" to all fans (pumps are skipped)?", "\"{0}\" tüm fanlara uygulansın mı (pompalar hariç)?"),
		["FanAuto"] = ("Auto", "Otomatik"),
		["FanManual"] = ("Manual speed…", "Manuel hız…"),
		["FanQuiet"] = ("Curve: Quiet", "Eğri: Sessiz"),
		["FanOptimal"] = ("Curve: Optimal", "Eğri: Optimal"),
		["FanMax"] = ("Curve: Max Cold", "Eğri: Max Cold"),
		["FanPercent"] = ("Fan speed", "Fan hızı"),
		["FanModeManual"] = ("Manual {0}%", "Manuel {0}%"),
		["FanModeCurve"] = ("Curve · {0}", "Eğri · {0}"),
		["FanStalled"] = ("not spinning!", "dönmüyor!"),
		["FanEmergency"] = ("TOO HOT – fans at 100%", "ÇOK SICAK – fanlar %100"),
		["FanManualWarn"] = ("A fixed speed ignores temperature. Too low can overheat the PC. Continue?",
			"Sabit hız sıcaklığa bakmaz. Çok düşük olursa PC ısınabilir. Devam edilsin mi?"),
		["FanPumpWarn"] = ("This looks like a water-cooling pump. A slow pump can overheat the CPU. Continue?",
			"Bu bir sıvı soğutma pompası gibi görünüyor. Yavaş pompa CPU'yu ısıtabilir. Devam edilsin mi?"),
		["FanRemoteOff"] = ("Fan changes from the phone are off. Turn on \"Let the mobile app change fan settings\" in dHud on your PC (Settings → Behavior → Mobile app).",
			"Telefondan fan değişikliği kapalı. PC'deki dHud'da \"Mobil uygulama fan ayarlarını değiştirebilsin\" seçeneğini aç (Ayarlar → Behavior → Mobil uygulama)."),
		["FanCtlOff"] = ("Fan control is off on the PC. Turn it on in dHud's Fan control window first.",
			"PC'de fan kontrolü kapalı. Önce dHud'un Fan kontrolü penceresinden aç."),
		["FanSendFailed"] = ("The change could not be applied.", "Değişiklik uygulanamadı."),
		["Ok"] = ("OK", "Tamam")
	};

	public static string T(string key, params object[] args)
	{
		var text = Texts.TryGetValue(key, out var pair) ? (Turkish ? pair.Tr : pair.En) : key;
		return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
	}
}
