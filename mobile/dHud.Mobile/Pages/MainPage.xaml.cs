using dHud.Mobile.Controls;
using dHud.Mobile.Services;

namespace dHud.Mobile.Pages;

public partial class MainPage : ContentPage
{
	private const double WideLayoutWidth = 700;
	private const double CompactHeight = 600;
	private const double MinFitScale = 0.6;   // bundan fazla küçültme; yine sığmazsa kaydırma açılır
	private bool _fitting;

	private enum LayoutMode
	{
		Narrow,
		Wide,
		CompactWide
	}

	private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);

	private readonly TempGraph _graph = new();
	private IDispatcherTimer? _updateTimer;
	private AppUpdate? _update;
	private bool _fullscreen;
	private LayoutMode? _mode;
	private static double _tempFontSize = 40;

	public MainPage()
	{
		InitializeComponent();

		Graph.Drawable = _graph;
		ApplyTexts();

		App.Link.Changed += Refresh;
		App.Link.AlertReceived += OnAlert;
		App.Link.CommandResult += OnCommandResult;

		// Ekran koruyucu: herhangi bir dokunuş (karartılmışken karartma katmanı dahil) paneli uyandırır.
		var wakeTap = new TapGestureRecognizer();
		wakeTap.Tapped += (_, _) => Wake();
		Root.GestureRecognizers.Add(wakeTap);
		var wakeDimTap = new TapGestureRecognizer();
		wakeDimTap.Tapped += (_, _) => Wake();
		SaverDim.GestureRecognizers.Add(wakeDimTap);
		Dashboard.SizeChanged += Dashboard_SizeChanged;

		// Tablet şarjdayken (dock/duvar standı) ekran kapanmasın; pilde normal uyku süresi geçerli.
		try
		{
			Battery.Default.BatteryInfoChanged += (_, _) => UpdateKeepAwake();
		}
		catch (Exception)
		{
			// Pil bilgisi okunamayan cihazlarda yalnızca tam ekran modu ekranı açık tutar.
		}

		UpdateKeepAwake();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		var pairing = PairingStore.Load();
		if (pairing is not null && App.Link.State == LinkState.Idle)
		{
			App.Link.Start(pairing);
		}

		Refresh();
		StartUpdateChecks();
		_ = AlertNotifier.RequestPermissionAsync();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		// Dar: dikey telefon. Alçak geniş: yatay telefon. Geniş: tablet.
		var mode = width < WideLayoutWidth ? LayoutMode.Narrow
			: height < CompactHeight ? LayoutMode.CompactWide
			: LayoutMode.Wide;
		if (mode != _mode)
		{
			_mode = mode;
			ArrangeCards(mode);
		}
	}

	private void Dashboard_SizeChanged(object? sender, EventArgs e) => FitToViewport();

	/// <summary>
	/// Geniş düzenlerde kartlar görünür alanı tam doldurur (grafik kalan yüksekliği alır) ve kaydırma gerekmez.
	/// Dar düzende içerik kendi boyunda kalır ve kaydırılır.
	/// </summary>
	private void FitToViewport()
	{
		var width = Dashboard.Width;
		var height = Dashboard.Height;
		if (_fitting || _mode is null || width <= 0 || height <= 0)
		{
			return;
		}

		_fitting = true;
		try
		{
			// Önce ölçeksiz doğal boyutu ölç; sığmıyorsa tüm paneli küçültüp görünür alana tam oturt (her şey tek ekranda görünsün).
			Cards.Scale = 1;
			Cards.WidthRequest = -1;
			Cards.HeightRequest = -1;
			Cards.MinimumHeightRequest = 0;
			var natural = Cards.Measure(width, double.PositiveInfinity).Height;
			var scale = FitMath.Scale(height, natural, MinFitScale);

			Cards.AnchorX = 0;
			Cards.AnchorY = 0;
			Cards.Scale = scale;
			Cards.WidthRequest = width / scale;                    // küçülünce genişlik de ekranı doldurur
			Cards.HeightRequest = Math.Max(height / scale, natural); // artan boşluğu grafik alır
			Dashboard.Orientation = FitMath.Fits(height, natural, scale) ? ScrollOrientation.Neither : ScrollOrientation.Vertical;
		}
		finally
		{
			_fitting = false;
		}
	}

	/// <summary>Şarj olurken veya tam ekran modunda ekran açık kalır.</summary>
	private void UpdateKeepAwake()
	{
		var charging = false;
		try
		{
			charging = Battery.Default.PowerSource is BatteryPowerSource.AC or BatteryPowerSource.Usb or BatteryPowerSource.Wireless;
		}
		catch (Exception)
		{
			// Pil bilgisi okunamadı; şarj yokmuş gibi davran.
		}

		DeviceDisplay.Current.KeepScreenOn = _fullscreen || charging;
	}

	private void ApplyDensity(bool compact)
	{
		var padding = new Thickness(compact ? 10 : 14);
		foreach (var card in new[] { CpuCard, GpuCard, GraphCard, RamCard, NetCard, FanCard })
		{
			card.Padding = padding;
			if (card.Content is VerticalStackLayout stack)
			{
				stack.Spacing = compact ? 4 : 8;   // kart içi aralık: alçak ekranda her şey sığsın
			}
		}

		_tempFontSize = compact ? 30 : 40;
		RamValue.FontSize = compact ? 20 : 26;
		Cards.ColumnSpacing = Cards.RowSpacing = compact ? 8 : 12;
	}

	private void ApplyTexts()
	{
		EmptyTitle.Text = Loc.T("NotPaired.Title");
		EmptyText.Text = Loc.T("NotPaired.Text");
		ScanButton.Text = Loc.T("ScanQr");
		ManualButton.Text = Loc.T("Manual");
		AlertDismissButton.Text = Loc.T("Ok");

		GraphTitle.Text = Loc.T("Graph");
		foreach (var caption in new[] { CpuUsageCaption, GpuUsageCaption })
		{
			caption.Text = Loc.T("Usage");
		}

		CpuClockCaption.Text = GpuClockCaption.Text = Loc.T("Clock");
		CpuPowerCaption.Text = GpuPowerCaption.Text = Loc.T("Power");
		CpuFanCaption.Text = GpuFanCaption.Text = Loc.T("Fan");
		CpuCoreCaption.Text = Loc.T("CoreMax");
		DiskCaption.Text = Loc.T("Disk");
		GpuVramCaption.Text = Loc.T("Vram");
		NetDownCaption.Text = Loc.T("Download");
		NetUpCaption.Text = Loc.T("Upload");
		NetPingCaption.Text = Loc.T("Ping");
	}

	/// <summary>Ekran boyutuna göre kart yerleşimi: tek sütun / iki sütun / üç sütun.</summary>
	private void ArrangeCards(LayoutMode mode)
	{
		Cards.ColumnDefinitions.Clear();
		Cards.RowDefinitions.Clear();
		ApplyDensity(mode != LayoutMode.Wide);   // telefon (dikey/yatay) sıkı; tablet ferah

		switch (mode)
		{
			case LayoutMode.Wide:
				// CPU | GPU / grafik (kalan yükseklik) / RAM | NET
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Star));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Place(CpuCard, 0, 0);
				Place(GpuCard, 0, 1);
				Place(GraphCard, 1, 0, columnSpan: 2);
				Place(RamCard, 2, 0);
				Place(NetCard, 2, 1);
				break;

			case LayoutMode.CompactWide:
				// CPU | GPU | RAM   üst sıra
				// grafik (2 sütun) | NET   alt sıra (kalan yükseklik)
				for (var i = 0; i < 3; i++)
				{
					Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				}

				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Star));
				Place(CpuCard, 0, 0);
				Place(GpuCard, 0, 1);
				Place(RamCard, 0, 2);
				Place(GraphCard, 1, 0, columnSpan: 2);
				Place(NetCard, 1, 2);
				break;

			default:
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				// CPU / GPU / grafik (kalan yükseklik) / RAM / NET: hepsi tek ekranda; sığmazsa FitToViewport küçültür.
				for (var i = 0; i < 5; i++)
				{
					Cards.RowDefinitions.Add(new RowDefinition(i == 2 ? GridLength.Star : GridLength.Auto));
				}

				Place(CpuCard, 0, 0);
				Place(GpuCard, 1, 0);
				Place(GraphCard, 2, 0);
				Place(RamCard, 3, 0);
				Place(NetCard, 4, 0);
				break;
		}

		// Fan sekmesi: geniş ekranda profiller solda, fanlar sağda; dar ekranda alt alta.
		FanLayout.ColumnDefinitions.Clear();
		FanLayout.RowDefinitions.Clear();
		if (mode == LayoutMode.Narrow)
		{
			FanLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			FanLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			FanLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			Place(ProfileCard, 0, 0);
			Place(FanCard, 1, 0);
		}
		else
		{
			FanLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
			FanLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
			FanLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			Place(ProfileCard, 0, 0);
			Place(FanCard, 0, 1);
		}

		// Grafiğin en küçük yüksekliği: doğal boyut buna göre ölçülür, artan yükseklik grafiğe verilir.
		Graph.MinimumHeightRequest = mode switch
		{
			LayoutMode.Wide => 120,
			LayoutMode.CompactWide => 70,
			_ => 90
		};

		FitToViewport();
	}

	private static void Place(View view, int row, int column, int columnSpan = 1)
	{
		Grid.SetRow(view, row);
		Grid.SetColumn(view, column);
		Grid.SetColumnSpan(view, columnSpan);
	}

	// ---- Veri ----

	private void Refresh()
	{
		var paired = PairingStore.Load() is not null;
		EmptyState.IsVisible = !paired;
		Dashboard.IsVisible = paired && !_fansTab;
		FanPage.IsVisible = paired && _fansTab;
		TabBar.IsVisible = paired && !_fullscreen;
		TabPanelButton.Text = Loc.T("TabPanel");
		TabFansButton.Text = Loc.T("TabFans");
		FullscreenButton.IsVisible = paired;

		var link = App.Link;
		StatusDot.Color = link.State switch
		{
			LinkState.Connected => Color.FromArgb("#4ADE80"),
			LinkState.Connecting => Color.FromArgb("#FACC15"),
			LinkState.Offline or LinkState.Rejected => Color.FromArgb("#F87171"),
			_ => Color.FromArgb("#9CA3AF")
		};
		StatusLabel.Text = link.State switch
		{
			LinkState.Connected => Loc.T("Connected", link.Latest?.Pc ?? PairingStore.Load()?.Name ?? string.Empty),
			LinkState.Connecting => Loc.T("Connecting"),
			LinkState.Offline => Loc.T("Offline"),
			LinkState.Rejected => Loc.T("Rejected"),
			_ => string.Empty
		};

		if (link.Latest is { } s)
		{
			FpsLabel.IsVisible = s.Fps is not null;
			if (s.Fps is float fps)
			{
				FpsLabel.Text = $"{fps:0} FPS";
				FpsLabel.TextColor = fps >= 60 ? Color.FromArgb("#4ADE80") : fps >= 30 ? Color.FromArgb("#FACC15") : Color.FromArgb("#F87171");
			}

			ShowDevice(s.Cpu, CpuName, CpuTemp, CpuLoad, CpuLoadBar, CpuClock, CpuPower, CpuFan);
			ShowDevice(s.Gpu, GpuName, GpuTemp, GpuLoad, GpuLoadBar, GpuClock, GpuPower, GpuFan);
			GpuVram.Text = s.Gpu is { MemoryUsedGb: float vu, MemoryTotalGb: float vt } ? $"{vu:0.0}/{vt:0} GB" : "--";
			ShowMemory(s.Ram?.MemoryUsedGb, s.Ram?.MemoryTotalGb, RamValue, RamBar, decimals: 1);
			CpuCore.Text = s.Cpu?.CoreMaxLoad is float coreMax ? $"{coreMax:0}%" : "--";
			DiskValue.Text = s.Disk is { } disk ? $"{disk.UsedPercent:0}%  ·  {disk.UsedGb:0}/{disk.TotalGb:0} GB" : "--";
			DiskBar.Progress = Math.Clamp((s.Disk?.UsedPercent ?? 0) / 100.0, 0, 1);

			NetName.Text = s.Net?.AdapterName ?? string.Empty;
			NetDown.Text = FormatSpeed(s.Net?.DownloadBytesPerSecond);
			NetUp.Text = FormatSpeed(s.Net?.UploadBytesPerSecond);
			NetPing.Text = s.Net?.PingMs is long ms ? $"{ms} ms" : "--";
			RefreshFans(s);
		}

		_graph.Cpu = link.CpuTemp.ToList();
		_graph.Gpu = link.GpuTemp.ToList();
		CpuLegend.Text = "CPU " + FormatTemp(link.Latest?.Cpu?.Temperature);
		GpuLegend.Text = "GPU " + FormatTemp(link.Latest?.Gpu?.Temperature);
		Graph.Invalidate();
	}

	private static void ShowDevice(DeviceReading? d, Label name, Label temp, Label load, BarView bar,
		Label clock, Label power, Label fan)
	{
		name.Text = d?.Name ?? string.Empty;
		SetTemperature(temp, d?.Temperature);
		load.Text = d?.Load is float l ? $"{l:0}%" : "--%";
		bar.Progress = Math.Clamp((d?.Load ?? 0) / 100.0, 0, 1);
		clock.Text = d?.ClockMhz switch
		{
			float v when v >= 1000 => $"{v / 1000:0.0} GHz",
			float v => $"{v:0} MHz",
			_ => "--"
		};
		power.Text = d?.PowerWatts is float w ? $"{w:0} W" : "--";
		fan.Text = d?.FanRpm is float f ? $"{f:0} RPM" : "--";
	}

	private static void ShowMemory(float? used, float? total, Label label, BarView bar, int decimals)
	{
		if (used is float u && total is float t && t > 0)
		{
			label.Text = string.Format("{0:F" + decimals + "} / {1:0.#} GB", u, t);
			bar.Progress = Math.Clamp(u / t, 0, 1);
		}
		else
		{
			label.Text = "--";
			bar.Progress = 0;
		}
	}

	/// <summary>Büyük sayı + küçük "°C" birimi.</summary>
	private static void SetTemperature(Label label, float? t)
	{
		var color = TempColor(t);
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = t is float v && v > 0 ? $"{v:0}" : "--", FontSize = _tempFontSize, FontAttributes = FontAttributes.Bold, TextColor = color },
				new Span { Text = "°C", FontSize = _tempFontSize * 0.42, TextColor = Color.FromArgb("#7B8494") }
			}
		};
	}

	private static string FormatTemp(float? t) => t is float v && v > 0 ? $"{v:0}°C" : "--°C";

	private static Color TempColor(float? t) => t switch
	{
		null or <= 0 => Color.FromArgb("#F1F2F6"),
		>= 80 => Color.FromArgb("#F87171"),
		>= 60 => Color.FromArgb("#FACC15"),
		_ => Color.FromArgb("#4ADE80")
	};

	private static string FormatSpeed(double? bytesPerSecond)
	{
		if (bytesPerSecond is not double v)
		{
			return "--";
		}

		string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
		var i = 0;
		while (v >= 1000 && i < units.Length - 1)
		{
			v /= 1000;
			i++;
		}

		return (v < 10 && i > 0 ? $"{v:0.0}" : $"{v:0}") + " " + units[i];
	}

	// ---- Fan sekmesi ----

	private bool _fansTab;
	private string _fanKey = string.Empty;
	private string _profileKey = string.Empty;
	private readonly Dictionary<string, (Label Detail, Label Mode)> _fanLabels = [];

	private static readonly (string Key, string Label)[] FanPresets =
	[
		("quiet", "FanQuiet"), ("optimal", "FanOptimal"), ("max", "FanMax")
	];

	private void ShowTab(bool fans)
	{
		_fansTab = fans;
		Dashboard.IsVisible = !fans && PairingStore.Load() is not null;
		FanPage.IsVisible = fans && PairingStore.Load() is not null;
		TabPanelButton.Style = TabStyle(!fans);
		TabFansButton.Style = TabStyle(fans);
	}

	private static Style TabStyle(bool active) =>
		(Style)Application.Current!.Resources[active ? "PrimaryButton" : "SecondaryButton"];

	private void TabPanelButton_Clicked(object? sender, EventArgs e) => ShowTab(false);

	private void TabFansButton_Clicked(object? sender, EventArgs e) => ShowTab(true);

	private void RefreshFans(PcSnapshot s)
	{
		var fans = s.Fans ?? [];
		var ctl = s.FanCtl;

		FanTitle.Text = Loc.T("Fans");
		FanEmergency.IsVisible = ctl?.Emergency == true;
		FanEmergency.Text = Loc.T("FanEmergency");
		FanEnableLabel.Text = Loc.T("FanEnable");
		_updatingFanSwitch = true;
		FanEnableSwitch.IsToggled = ctl?.Enabled == true;
		_updatingFanSwitch = false;
		FanStatusLabel.Text = ctl is null || !ctl.Remote ? Loc.T("FanRemoteOff")
			: !ctl.Enabled ? Loc.T("FanCtlOff")
			: Loc.T("FanTapHint");
		FanStatusLabel.TextColor = ctl is { Remote: true, Enabled: true } ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#FACC15");

		// Hazır eğriler: tüm fanlara tek dokunuşla
		ProfileTitle.Text = Loc.T("Presets");
		PresetHint.Text = Loc.T("PresetHint");
		ProfileSubTitle.Text = Loc.T("Profiles");
		if (PresetRows.Children.Count == 0)
		{
			foreach (var (presetKey, labelKey) in FanPresets)
			{
				var key2 = presetKey;
				var button = new Button
				{
					Text = Loc.T(labelKey + "Short"),
					Style = TabStyle(true),
					HeightRequest = 40,
					Padding = new Thickness(16, 0),
					Margin = new Thickness(0, 0, 8, 8)
				};
				button.Clicked += async (_, _) => await ApplyPresetToAllAsync(key2);
				PresetRows.Children.Add(button);
			}
		}

		// Profiller (PC'de kaydedilmiş tüm-fan ayarları)
		var profiles = ctl?.Profiles ?? [];
		ProfileHint.Text = profiles.Count == 0 ? Loc.T("NoProfiles") : Loc.T("ProfileHint");
		var profileKey = string.Join("|", profiles);
		if (profileKey != _profileKey)
		{
			_profileKey = profileKey;
			ProfileRows.Children.Clear();
			foreach (var name in profiles)
			{
				var profileName = name;
				var button = new Button
				{
					Text = name,
					Style = TabStyle(false),
					HeightRequest = 40,
					Padding = new Thickness(16, 0),
					Margin = new Thickness(0, 0, 8, 8)
				};
				button.Clicked += async (_, _) => await ApplyProfileAsync(profileName);
				ProfileRows.Children.Add(button);
			}
		}

		// Fanlar
		var key = string.Join("|", fans.Select(f => f.Id));
		if (key != _fanKey)
		{
			_fanKey = key;
			_fanLabels.Clear();
			FanRows.Children.Clear();
			foreach (var fan in fans)
			{
				FanRows.Children.Add(BuildFanRow(fan));
			}
		}

		foreach (var fan in fans)
		{
			if (!_fanLabels.TryGetValue(fan.Id, out var labels))
			{
				continue;
			}

			var rpm = fan.Rpm is float r ? $"{r:0} RPM" : "-- RPM";
			var pct = fan.Pct is float p ? $"{p:0}%" : "--%";
			labels.Detail.Text = $"{rpm} · {pct}" + (fan.Stalled ? " · " + Loc.T("FanStalled") : string.Empty);
			labels.Detail.TextColor = fan.Stalled ? Color.FromArgb("#F87171") : Color.FromArgb("#9CA3AF");
			labels.Mode.Text = (fan.Remote ? string.Empty : "🔒 ") + fan.Mode switch
			{
				"manual" => Loc.T("FanModeManual", fan.Manual),
				"curve" => Loc.T("FanModeCurve", fan.Curve ?? string.Empty),
				_ => Loc.T("FanAuto")
			};
		}
	}

	private View BuildFanRow(FanInfo fan)
	{
		var name = new Label { Text = fan.Name, FontAttributes = FontAttributes.Bold, FontSize = 14, TextColor = Color.FromArgb("#F1F2F6"), LineBreakMode = LineBreakMode.TailTruncation };
		var detail = new Label { FontSize = 12 };
		var mode = new Label { FontSize = 12, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#60A5FA") };
		var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(0, 8), MinimumHeightRequest = 48 };
		row.Add(new VerticalStackLayout { Children = { name, detail } }, 0, 0);
		row.Add(mode, 1, 0);

		var tap = new TapGestureRecognizer();
		var id = fan.Id;
		tap.Tapped += async (_, _) => await EditFanAsync(id);
		row.GestureRecognizers.Add(tap);
		_fanLabels[fan.Id] = (detail, mode);
		return row;
	}

	private bool _updatingFanSwitch;

	/// <summary>Fan kontrolünü PC'de açar/kapatır. Açmadan önce PC'deki uyarının aynısı sorulur; kapatmak her zaman güvenlidir.</summary>
	private async void FanEnableSwitch_Toggled(object? sender, ToggledEventArgs e)
	{
		if (_updatingFanSwitch)
		{
			return;
		}

		var ctl = App.Link.Latest?.FanCtl;
		var wanted = e.Value;
		if (ctl is null || !ctl.Remote)
		{
			await DisplayAlert("dHud", Loc.T("FanRemoteOff"), Loc.T("Ok"));
			Revert(ctl?.Enabled == true);
			return;
		}

		if (wanted && !await DisplayAlert(Loc.T("FanEnable"), Loc.T("FanEnableConfirm"), Loc.T("Yes"), Loc.T("No")))
		{
			Revert(false);
			return;
		}

		await SendAsync(new { cmd = "fanControl", enabled = wanted });

		void Revert(bool value)
		{
			_updatingFanSwitch = true;
			FanEnableSwitch.IsToggled = value;
			_updatingFanSwitch = false;
		}
	}

	/// <summary>PC'nin fan değişikliğine izin verip vermediğini denetler; vermiyorsa nedenini gösterir.</summary>
	private async Task<bool> CanControlFansAsync()
	{
		var ctl = App.Link.Latest?.FanCtl;
		if (ctl is null || !ctl.Remote)
		{
			await DisplayAlert("dHud", Loc.T("FanRemoteOff"), Loc.T("Ok"));
			return false;
		}

		if (!ctl.Enabled)
		{
			await DisplayAlert("dHud", Loc.T("FanCtlOff"), Loc.T("Ok"));
			return false;
		}

		return true;
	}

	/// <summary>Bir fan için yalnızca hazır seçenekler: otomatik, hazır eğriler ve PC'de kaydedilmiş eğriler.</summary>
	private async Task EditFanAsync(string id)
	{
		var fan = App.Link.Latest?.Fans?.FirstOrDefault(f => f.Id == id);
		if (fan is null || !await CanControlFansAsync())
		{
			return;
		}

		if (!fan.Remote)
		{
			await DisplayAlert(fan.Name, Loc.T("FanLocked"), Loc.T("Ok"));
			return;
		}

		var saved = App.Link.Latest?.FanCtl?.Curves ?? [];
		var choices = new List<string> { Loc.T("FanAuto") };
		choices.AddRange(FanPresets.Select(p => Loc.T(p.Label)));
		choices.AddRange(saved.Select(name => Loc.T("FanSaved", name)));

		var action = await DisplayActionSheet(fan.Name, Loc.T("Cancel"), null, [.. choices]);
		if (action is null || action == Loc.T("Cancel"))
		{
			return;
		}

		if (action != Loc.T("FanAuto") && fan.Pump
			&& !await DisplayAlert(fan.Name, Loc.T("FanPumpWarn"), Loc.T("Yes"), Loc.T("No")))
		{
			return;
		}

		if (action == Loc.T("FanAuto"))
		{
			await SendAsync(new { cmd = "fan", id, mode = "auto" });
		}
		else if (FanPresets.FirstOrDefault(p => Loc.T(p.Label) == action) is { Key: not null } preset)
		{
			await SendAsync(new { cmd = "fan", id, preset = preset.Key });
		}
		else if (saved.FirstOrDefault(name => Loc.T("FanSaved", name) == action) is { } curve)
		{
			await SendAsync(new { cmd = "fan", id, saved = curve });
		}
	}

	private async Task ApplyPresetToAllAsync(string preset)
	{
		var label = Loc.T(FanPresets.First(p => p.Key == preset).Label + "Short");
		if (!await CanControlFansAsync()
			|| !await DisplayAlert(Loc.T("Presets"), Loc.T("PresetConfirm", label), Loc.T("Yes"), Loc.T("No")))
		{
			return;
		}

		await SendAsync(new { cmd = "fanAll", preset });
	}

	private async Task ApplyProfileAsync(string name)
	{
		if (!await CanControlFansAsync()
			|| !await DisplayAlert(Loc.T("Profiles"), Loc.T("ProfileConfirm", name), Loc.T("Yes"), Loc.T("No")))
		{
			return;
		}

		await SendAsync(new { cmd = "profile", name });
	}

	private async Task SendAsync(object command)
	{
		if (!await App.Link.SendCommandAsync(command))
		{
			await DisplayAlert("dHud", Loc.T("FanSendFailed"), Loc.T("Ok"));
		}
	}

	private async void OnCommandResult(string cmd, bool ok, string? error)
	{
		if (ok)
		{
			return;
		}

		var key = error switch
		{
			"disabled" => "FanRemoteOff",
			"fanctl" => "FanCtlOff",
			"blocked" => "FanLocked",
			_ => "FanSendFailed"
		};
		await DisplayAlert("dHud", Loc.T(key), Loc.T("Ok"));
	}

	// ---- Eşleştirme ----

	private async void ScanButton_Clicked(object? sender, EventArgs e) => await Navigation.PushAsync(new ScanPage());

	private async void ManualButton_Clicked(object? sender, EventArgs e)
	{
		var address = await DisplayPromptAsync("dHud", Loc.T("ManualAddress"), keyboard: Keyboard.Text);
		if (string.IsNullOrWhiteSpace(address))
		{
			return;
		}

		var code = await DisplayPromptAsync("dHud", Loc.T("ManualCode"), keyboard: Keyboard.Text);
		if (PairingStore.FromManual(address, code ?? string.Empty) is { } info)
		{
			Pair(info);
		}
	}

	public static void Pair(PairingInfo info)
	{
		PairingStore.Save(info);
		App.Link.Start(info);
	}

	private async void MenuButton_Clicked(object? sender, EventArgs e)
	{
		var saver = Loc.T(SaverEnabled ? "SaverOn" : "SaverOff");
		var action = await DisplayActionSheet(Loc.T("Menu"), Loc.T("Cancel"), null,
			saver, Loc.T("Repair"), Loc.T("CheckUpdate"), Loc.T("Forget"));

		if (action == saver)
		{
			Preferences.Set(SaverKey, !SaverEnabled);
		}
		else if (action == Loc.T("Repair"))
		{
			await Navigation.PushAsync(new ScanPage());
		}
		else if (action == Loc.T("Forget"))
		{
			App.Link.Stop();
			PairingStore.Clear();
			Refresh();
		}
		else if (action == Loc.T("CheckUpdate"))
		{
			await CheckForUpdateAsync(manual: true);
		}
	}

	// ---- Tam ekran gösterge paneli ----

	private void FullscreenButton_Clicked(object? sender, EventArgs e)
	{
		_fullscreen = !_fullscreen;
		if (_fullscreen)
		{
			ShowTab(false);
		}

		TabBar.IsVisible = !_fullscreen;
		if (_fullscreen && SaverEnabled)
		{
			StartSaver();
		}
		else
		{
			StopSaver();
		}

		MenuButton.IsVisible = TitleBox.IsVisible = !_fullscreen;
		UpdateKeepAwake();

#if ANDROID
		if (Platform.CurrentActivity?.Window is { } window)
		{
			AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, !_fullscreen);
			var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
			if (controller is not null)
			{
				controller.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
				if (_fullscreen)
				{
					controller.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
				}
				else
				{
					controller.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
				}
			}
		}
#endif
	}

	// ---- Ekran koruyucu (tam ekran panel) ----

	private const string SaverKey = "screensaver";
	private static readonly TimeSpan SaverIdle = TimeSpan.FromMinutes(3);
	private static readonly (double X, double Y)[] SaverShifts = [(0, 0), (10, 6), (-8, 10), (6, -10), (-10, -4), (8, 8)];

	private IDispatcherTimer? _saverTimer;
	private DateTime _lastTouch = DateTime.UtcNow;
	private int _saverTick;
	private bool _dimmed;

	private static bool SaverEnabled => Preferences.Get(SaverKey, true);

	/// <summary>Tam ekran panelde: içerik dakikada bir birkaç piksel kayar (ekranda iz kalmasın), 3 dakika dokunulmazsa kararır.</summary>
	private void StartSaver()
	{
		_lastTouch = DateTime.UtcNow;
		if (_saverTimer is null)
		{
			_saverTimer = Dispatcher.CreateTimer();
			_saverTimer.Interval = TimeSpan.FromSeconds(30);
			_saverTimer.Tick += (_, _) => SaverTick();
		}

		_saverTimer.Start();
	}

	private void StopSaver()
	{
		_saverTimer?.Stop();
		Wake();
		Dashboard.TranslationX = Dashboard.TranslationY = 0;
	}

	private void SaverTick()
	{
		if (!_fullscreen || !SaverEnabled)
		{
			StopSaver();
			return;
		}

		// 30 sn'de bir çağrılır; her ikinci çağrıda (dakikada bir) kaydır.
		if (++_saverTick % 2 == 0)
		{
			var (x, y) = SaverShifts[_saverTick / 2 % SaverShifts.Length];
			_ = Dashboard.TranslateTo(x, y, 2000, Easing.CubicInOut);
		}

		if (!_dimmed && DateTime.UtcNow - _lastTouch >= SaverIdle)
		{
			_dimmed = true;
			SaverDim.IsVisible = true;
			SaverDim.InputTransparent = false; // karartılmışken ilk dokunuş yalnızca uyandırır
			_ = SaverDim.FadeTo(0.7, 1500);
		}
	}

	/// <summary>Ekrana dokunuldu (veya uyarı geldi): karartmayı kaldırır, boşta sayacını sıfırlar.</summary>
	private void Wake()
	{
		_lastTouch = DateTime.UtcNow;
		if (!_dimmed)
		{
			return;
		}

		_dimmed = false;
		SaverDim.InputTransparent = true;
		SaverDim.Opacity = 0;
		SaverDim.IsVisible = false;
	}

	// ---- Sıcaklık uyarısı ----

	private void OnAlert(string text, string kind)
	{
		Wake();
		AlertLabel.Text = text;
		AlertBanner.IsVisible = true;
		AlertNotifier.Notify(Loc.T(kind switch { "reminder" => "ReminderTitle", "summary" => "SummaryTitle", _ => "AlertTitle" }), text);
	}

	private void AlertDismissButton_Clicked(object? sender, EventArgs e) => AlertBanner.IsVisible = false;

	// ---- Güncelleme ----

	/// <summary>Açılışta bir kez, ardından uygulama açık kaldığı sürece 12 saatte bir yeni sürüm arar.</summary>
	private void StartUpdateChecks()
	{
		if (_updateTimer is not null)
		{
			return;
		}

		_updateTimer = Dispatcher.CreateTimer();
		_updateTimer.Interval = UpdateCheckInterval;
		_updateTimer.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
		_updateTimer.Start();
		_ = CheckForUpdateAsync(manual: false);
	}

	private async Task CheckForUpdateAsync(bool manual)
	{
		try
		{
			_update = await AppUpdater.CheckAsync();
		}
		catch (Exception ex)
		{
			if (manual)
			{
				await DisplayAlert("dHud", Loc.T("UpdateFailed", ex.Message), Loc.T("Ok"));
			}

			return;
		}

		UpdateBanner.IsVisible = _update is not null;
		if (_update is not null)
		{
			UpdateLabel.Text = Loc.T("UpdateAvail", _update.Tag);
			InstallButton.Text = Loc.T("Install");
		}
		else if (manual)
		{
			await DisplayAlert("dHud", Loc.T("UpToDate", AppInfo.Current.VersionString), Loc.T("Ok"));
		}
	}

	private async void InstallButton_Clicked(object? sender, EventArgs e)
	{
		if (_update is null)
		{
			return;
		}

		InstallButton.IsEnabled = false;
		InstallButton.Text = Loc.T("Downloading");
		try
		{
			var path = await AppUpdater.DownloadAsync(_update, null);
			if (!AppUpdater.Install(path))
			{
				await DisplayAlert("dHud", Loc.T("AllowInstall"), Loc.T("Ok"));
			}
		}
		catch (Exception ex)
		{
			await DisplayAlert("dHud", Loc.T("UpdateFailed", ex.Message), Loc.T("Ok"));
		}
		finally
		{
			InstallButton.IsEnabled = true;
			InstallButton.Text = Loc.T("Install");
		}
	}
}
