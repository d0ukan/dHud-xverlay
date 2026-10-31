using dHud.Mobile.Controls;
using dHud.Mobile.Services;

namespace dHud.Mobile.Pages;

public partial class MainPage : ContentPage
{
	private const double WideLayoutWidth = 700;
	private const double CompactHeight = 600;

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
	private void FitToViewport() =>
		Cards.MinimumHeightRequest = _mode == LayoutMode.Narrow ? 0 : Dashboard.Height;

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
		ApplyDensity(mode == LayoutMode.CompactWide);

		switch (mode)
		{
			case LayoutMode.Wide:
				// CPU | GPU / grafik (kalan yükseklik) / RAM | NET
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Star));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Place(CpuCard, 0, 0);
				Place(GpuCard, 0, 1);
				Place(GraphCard, 1, 0, columnSpan: 2);
				Place(RamCard, 2, 0);
				Place(NetCard, 2, 1);
				Place(FanCard, 3, 0, columnSpan: 2);
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
				Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				Place(CpuCard, 0, 0);
				Place(GpuCard, 0, 1);
				Place(RamCard, 0, 2);
				Place(GraphCard, 1, 0, columnSpan: 2);
				Place(NetCard, 1, 2);
				Place(FanCard, 2, 0, columnSpan: 3);
				break;

			default:
				Cards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				for (var i = 0; i < 6; i++)
				{
					Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				}

				Graph.MinimumHeightRequest = 220;
				Place(CpuCard, 0, 0);
				Place(GpuCard, 1, 0);
				Place(FanCard, 2, 0);
				Place(GraphCard, 3, 0);
				Place(RamCard, 4, 0);
				Place(NetCard, 5, 0);
				break;
		}

		if (mode != LayoutMode.Narrow)
		{
			Graph.MinimumHeightRequest = mode == LayoutMode.Wide ? 200 : 90;
		}

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
		Dashboard.IsVisible = paired;
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

	// ---- Fanlar ----

	private string _fanKey = string.Empty;
	private readonly Dictionary<string, (Label Detail, Label Mode)> _fanLabels = [];

	private static readonly (string Key, string Label)[] FanPresets =
	[
		("quiet", "FanQuiet"), ("optimal", "FanOptimal"), ("max", "FanMax")
	];

	private void RefreshFans(PcSnapshot s)
	{
		var fans = s.Fans ?? [];
		FanCard.IsVisible = fans.Count > 0;
		FanTitle.Text = Loc.T("Fans");
		FanAllButton.Text = Loc.T("FanAll");
		FanAllButton.IsVisible = s.FanCtl is { Remote: true, Enabled: true };
		FanEmergency.IsVisible = s.FanCtl?.Emergency == true;
		FanEmergency.Text = Loc.T("FanEmergency");

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
			labels.Mode.Text = fan.Mode switch
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
		var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(0, 8), MinimumHeightRequest = 44 };
		row.Add(new VerticalStackLayout { Children = { name, detail } }, 0, 0);
		row.Add(mode, 1, 0);

		var tap = new TapGestureRecognizer();
		var id = fan.Id;
		tap.Tapped += async (_, _) => await EditFanAsync(id);
		row.GestureRecognizers.Add(tap);
		_fanLabels[fan.Id] = (detail, mode);
		return row;
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

	private async Task EditFanAsync(string id)
	{
		var fan = App.Link.Latest?.Fans?.FirstOrDefault(f => f.Id == id);
		if (fan is null || !await CanControlFansAsync())
		{
			return;
		}

		var choices = new List<string> { Loc.T("FanAuto"), Loc.T("FanManual") };
		choices.AddRange(FanPresets.Select(p => Loc.T(p.Label)));
		var action = await DisplayActionSheet(fan.Name, Loc.T("Cancel"), null, [.. choices]);
		if (action is null || action == Loc.T("Cancel"))
		{
			return;
		}

		if (action == Loc.T("FanAuto"))
		{
			await SendAsync(new { cmd = "fan", id, mode = "auto" });
			return;
		}

		// Pompa veya sabit hız: sıcaklığa tepki vermediği için önce onay.
		var risky = fan.Pump || action == Loc.T("FanManual");
		if (risky && !await DisplayAlert(fan.Name, Loc.T(fan.Pump ? "FanPumpWarn" : "FanManualWarn"), Loc.T("Yes"), Loc.T("No")))
		{
			return;
		}

		if (action == Loc.T("FanManual"))
		{
			string[] steps = ["30", "40", "50", "60", "70", "80", "90", "100"];
			var picked = await DisplayActionSheet(Loc.T("FanPercent"), Loc.T("Cancel"), null, steps.Select(x => x + "%").ToArray());
			if (picked is null || picked == Loc.T("Cancel"))
			{
				return;
			}

			await SendAsync(new { cmd = "fan", id, mode = "manual", percent = int.Parse(picked.TrimEnd('%')) });
			return;
		}

		var preset = FanPresets.First(p => Loc.T(p.Label) == action).Key;
		await SendAsync(new { cmd = "fan", id, mode = "curve", preset });
	}

	private async void FanAllButton_Clicked(object? sender, EventArgs e)
	{
		if (!await CanControlFansAsync())
		{
			return;
		}

		var choices = FanPresets.Select(p => Loc.T(p.Label)).Append(Loc.T("FanAllAuto")).ToArray();
		var action = await DisplayActionSheet(Loc.T("FanAll"), Loc.T("Cancel"), null, choices);
		if (action is null || action == Loc.T("Cancel"))
		{
			return;
		}

		if (!await DisplayAlert(Loc.T("FanAll"), Loc.T("FanAllConfirm", action), Loc.T("Yes"), Loc.T("No")))
		{
			return;
		}

		if (action == Loc.T("FanAllAuto"))
		{
			await SendAsync(new { cmd = "fanAll", mode = "auto" });
		}
		else
		{
			var preset = FanPresets.First(p => Loc.T(p.Label) == action).Key;
			await SendAsync(new { cmd = "fanAll", mode = "curve", preset });
		}
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
		var action = await DisplayActionSheet(Loc.T("Menu"), Loc.T("Cancel"), null,
			Loc.T("Repair"), Loc.T("CheckUpdate"), Loc.T("Forget"));

		if (action == Loc.T("Repair"))
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

	// ---- Sıcaklık uyarısı ----

	private void OnAlert(string text)
	{
		AlertLabel.Text = text;
		AlertBanner.IsVisible = true;
		AlertNotifier.Notify(Loc.T("AlertTitle"), text);
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
