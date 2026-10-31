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

	public MainPage()
	{
		InitializeComponent();

		Graph.Drawable = _graph;
		ApplyTexts();

		App.Link.Changed += Refresh;
		Dashboard.SizeChanged += Dashboard_SizeChanged;

		// Tablet şarjdayken (dock/duvar standı) ekran kapanmasın; pilde normal uyku süresi geçerli.
		Battery.Default.BatteryInfoChanged += (_, _) => UpdateKeepAwake();
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
	private void UpdateKeepAwake() =>
		DeviceDisplay.Current.KeepScreenOn = _fullscreen
			|| Battery.Default.PowerSource is BatteryPowerSource.AC or BatteryPowerSource.Usb or BatteryPowerSource.Wireless;

	private void ApplyDensity(bool compact)
	{
		var padding = new Thickness(compact ? 10 : 16);
		foreach (var card in new[] { CpuCard, GpuCard, GraphCard, RamCard, NetCard })
		{
			card.Padding = padding;
		}

		CpuTemp.FontSize = GpuTemp.FontSize = compact ? 32 : 46;
		RamValue.FontSize = compact ? 22 : 30;
		Cards.ColumnSpacing = Cards.RowSpacing = compact ? 8 : 12;
	}

	private void ApplyTexts()
	{
		EmptyTitle.Text = Loc.T("NotPaired.Title");
		EmptyText.Text = Loc.T("NotPaired.Text");
		ScanButton.Text = Loc.T("ScanQr");
		ManualButton.Text = Loc.T("Manual");

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
				for (var i = 0; i < 5; i++)
				{
					Cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				}

				Graph.MinimumHeightRequest = 220;
				Place(CpuCard, 0, 0);
				Place(GpuCard, 1, 0);
				Place(GraphCard, 2, 0);
				Place(RamCard, 3, 0);
				Place(NetCard, 4, 0);
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
			ShowDevice(s.Cpu, CpuName, CpuTemp, CpuLoad, CpuLoadBar, CpuClock, CpuPower, CpuFan);
			ShowDevice(s.Gpu, GpuName, GpuTemp, GpuLoad, GpuLoadBar, GpuClock, GpuPower, GpuFan);
			GpuVram.Text = s.Gpu is { MemoryUsedGb: float vu, MemoryTotalGb: float vt } ? $"{vu:0.0}/{vt:0} GB" : "--";
			ShowMemory(s.Ram?.MemoryUsedGb, s.Ram?.MemoryTotalGb, RamValue, RamBar, decimals: 1);

			NetName.Text = s.Net?.AdapterName ?? string.Empty;
			NetDown.Text = FormatSpeed(s.Net?.DownloadBytesPerSecond);
			NetUp.Text = FormatSpeed(s.Net?.UploadBytesPerSecond);
			NetPing.Text = s.Net?.PingMs is long ms ? $"{ms} ms" : "--";
		}

		_graph.Cpu = link.CpuTemp.ToList();
		_graph.Gpu = link.GpuTemp.ToList();
		CpuLegend.Text = "CPU " + FormatTemp(link.Latest?.Cpu?.Temperature);
		GpuLegend.Text = "GPU " + FormatTemp(link.Latest?.Gpu?.Temperature);
		Graph.Invalidate();
	}

	private static void ShowDevice(DeviceReading? d, Label name, Label temp, Label load, ProgressBar bar,
		Label clock, Label power, Label fan)
	{
		name.Text = d?.Name ?? string.Empty;
		temp.Text = FormatTemp(d?.Temperature);
		temp.TextColor = TempColor(d?.Temperature);
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

	private static void ShowMemory(float? used, float? total, Label label, ProgressBar bar, int decimals)
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
