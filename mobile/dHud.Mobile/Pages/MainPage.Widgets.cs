using System.Globalization;
using dHud.Mobile.Controls;
using dHud.Mobile.Services;

namespace dHud.Mobile.Pages;

// Widget'lar sekmesi: PC'deki widget'ların canlı kartları. PC bu sekme açıkken 2 saniyede bir veri gönderir (bkz. PcLink.SetWatchingAsync).
public partial class MainPage
{
	private static readonly Color Subtle = Color.FromArgb("#9CA3AF");
	private static readonly Color Light = Color.FromArgb("#F1F2F6");
	private static readonly Color Good = Color.FromArgb("#4ADE80");
	private static readonly Color Hot = Color.FromArgb("#F87171");
	private static readonly Color Warn = Color.FromArgb("#FACC15");

	private Border? _mediaCard, _todoCard2, _launchCard, _clipCard, _procCard, _marketCard, _batteryCard;
	private VerticalStackLayout? _mediaBody, _todoBody, _launchBody, _clipBody, _procBody, _marketBody, _batteryBody;
	private LayoutMode? _widgetsMode;
	private string _procSort = "cpu";
	private string _widgetsKey = string.Empty;
	private CancellationTokenSource? _statusCts;

	private static Style Res(string key) => (Style)Application.Current!.Resources[key];

	private Border NewCard(string title, out VerticalStackLayout body)
	{
		body = new VerticalStackLayout { Spacing = 8 };
		var heading = new Label { Text = title, Style = Res("TitleLabel") };
		var stack = new VerticalStackLayout { Spacing = 8, Children = { heading, body } };
		return new Border { Style = Res("CardStyle"), Content = stack };
	}

	/// <summary>Kartları bir kez kurar (içlerini RefreshWidgets doldurur).</summary>
	private void EnsureWidgetCards()
	{
		if (_mediaCard is not null)
		{
			return;
		}

		_mediaCard = NewCard(Loc.T("WMedia"), out _mediaBody!);
		_todoCard2 = NewCard(Loc.T("WTodo"), out _todoBody!);
		_launchCard = NewCard(Loc.T("WLauncher"), out _launchBody!);
		_clipCard = NewCard(Loc.T("WClip"), out _clipBody!);
		_procCard = NewCard(Loc.T("WTasks"), out _procBody!);
		_marketCard = NewCard(Loc.T("WMarket"), out _marketBody!);
		_batteryCard = NewCard(Loc.T("WBattery"), out _batteryBody!);
	}

	/// <summary>Kartları ekran genişliğine göre dizer: dar ekranda tek sütun, geniş ekranda iki sütun.</summary>
	private void ArrangeWidgets(LayoutMode mode)
	{
		EnsureWidgetCards();
		if (_widgetsMode == mode)
		{
			return;
		}

		_widgetsMode = mode;
		WidgetsLayout.Children.Clear();
		WidgetsLayout.ColumnDefinitions.Clear();
		WidgetsLayout.RowDefinitions.Clear();
		View[] order;
		if (mode == LayoutMode.Narrow)
		{
			WidgetsLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			order = [_mediaCard!, _launchCard!, _todoCard2!, _procCard!, _clipCard!, _marketCard!, _batteryCard!];
			for (var i = 0; i < order.Length; i++)
			{
				WidgetsLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				WidgetsLayout.Add(order[i], 0, i);
			}
		}
		else
		{
			WidgetsLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			WidgetsLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			// Sol: medya, başlatıcı, yapılacaklar, pil. Sağ: görev yöneticisi, pano, borsa.
			View[] left = [_mediaCard!, _launchCard!, _todoCard2!, _batteryCard!];
			View[] right = [_procCard!, _clipCard!, _marketCard!];
			var rows = Math.Max(left.Length, right.Length);
			for (var i = 0; i < rows; i++)
			{
				WidgetsLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			}

			for (var i = 0; i < left.Length; i++)
			{
				WidgetsLayout.Add(left[i], 0, i);
			}

			for (var i = 0; i < right.Length; i++)
			{
				WidgetsLayout.Add(right[i], 1, i);
			}
		}
	}

	/// <summary>Widget'lar sekmesini PC'den gelen veriyle çizer; değişmeyen kartlar yeniden kurulmaz.</summary>
	private void RefreshWidgets()
	{
		EnsureWidgetCards();
		if (_tab != Tab.Widgets)
		{
			return;
		}

		var state = App.Link.Widgets;
		if (state is null)
		{
			var wait = App.Link.State == LinkState.Connected && App.Link.PcVersion is null ? Loc.T("WOld") : Loc.T("WWaiting");
			ShowOnly(_mediaBody!, wait);
			_todoBody!.Children.Clear();
			_launchBody!.Children.Clear();
			_clipBody!.Children.Clear();
			_procBody!.Children.Clear();
			_marketBody!.Children.Clear();
			_batteryBody!.Children.Clear();
			_widgetsKey = string.Empty;
			return;
		}

		if (!state.Ctl)
		{
			ShowOnly(_mediaBody!, Loc.T("WOff"));
			return;
		}

		BuildMedia(state.Media);
		BuildTodo();
		BuildLauncher(state.Launch);
		BuildProcesses(state.Proc);
		BuildClipboard(state.Clip);
		BuildMarket(state.Market);
		BuildBattery(state.Battery);
	}

	/// <summary>Tüm kartlar boşalır, ilk kartta bir ileti gösterilir.</summary>
	private void ShowOnly(VerticalStackLayout first, string message)
	{
		foreach (var body in new[] { _todoBody!, _launchBody!, _clipBody!, _procBody!, _marketBody!, _batteryBody! })
		{
			body.Children.Clear();
		}

		first.Children.Clear();
		first.Children.Add(Muted(message));
		_mediaKey = string.Empty;
		_widgetsKey = string.Empty;
	}

	private static Label Muted(string text, double size = 13) =>
		new() { Text = text, FontSize = size, TextColor = Subtle, LineBreakMode = LineBreakMode.WordWrap };

	private static Label Strong(string text, double size = 16) =>
		new() { Text = text, FontSize = size, TextColor = Light, LineBreakMode = LineBreakMode.TailTruncation };

	private static ImageSource? ImageOf(string? base64)
	{
		if (string.IsNullOrEmpty(base64))
		{
			return null;
		}

		try
		{
			var bytes = Convert.FromBase64String(base64);
			return ImageSource.FromStream(() => new MemoryStream(bytes));
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static string FmtBytes(double bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		var i = 0;
		while (bytes >= 1024 && i < units.Length - 1)
		{
			bytes /= 1024;
			i++;
		}

		return (bytes < 10 && i >= 2 ? bytes.ToString("0.0", CultureInfo.CurrentCulture) : bytes.ToString("0", CultureInfo.CurrentCulture)) + " " + units[i];
	}

	private static string FmtRate(double bytesPerSecond) => bytesPerSecond < 1 ? "0" : FormatSpeed(bytesPerSecond);

	private static string FmtClock(double seconds)
	{
		var t = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 172800));
		return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
	}

	/// <summary>Kısa bir durum yazısı gösterir ve 3 saniye sonra gizler.</summary>
	private async void FlashWidgetStatus(string text, bool error = false)
	{
		_statusCts?.Cancel();
		var cts = _statusCts = new CancellationTokenSource();
		WidgetsStatus.Text = text;
		WidgetsStatus.TextColor = error ? Hot : Good;
		WidgetsStatus.IsVisible = true;
		try
		{
			await Task.Delay(3000, cts.Token);
			WidgetsStatus.IsVisible = false;
		}
		catch (OperationCanceledException)
		{
			// Yeni bir ileti geldi.
		}
	}

	// ---- Medya ----

	private string _mediaKey = string.Empty;
	private Image? _coverImage;
	private Label? _mediaTitle, _mediaArtist, _mediaApp, _mediaPos, _mediaLen;
	private ProgressBar? _mediaBar;
	private Button? _mediaPrev, _mediaPlay, _mediaNext;

	private void BuildMedia(MediaInfo? media)
	{
		if (_mediaTitle is null || _mediaBody!.Children.Count == 0 || _mediaBody.Children[0] is Label)
		{
			// İlk kurulum (veya ileti göstermiş olabilir): kalıcı denetimleri bir kez oluştur, sonra yalnızca metinleri güncelle.
			_mediaBody!.Children.Clear();
			_coverImage = new Image { WidthRequest = 84, HeightRequest = 84, Aspect = Aspect.AspectFill, BackgroundColor = Color.FromArgb("#1F2430") };
			_mediaTitle = Strong("", 17);
			_mediaTitle.FontAttributes = FontAttributes.Bold;
			_mediaArtist = Strong("", 14);
			_mediaApp = Muted("", 12);
			_mediaApp.LineBreakMode = LineBreakMode.TailTruncation;
			var texts = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2, Children = { _mediaTitle, _mediaArtist, _mediaApp } };
			var top = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 12 };
			top.Add(new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Content = _coverImage }, 0, 0);
			top.Add(texts, 1, 0);

			_mediaBar = new ProgressBar { ProgressColor = Color.FromArgb("#3B82F6") };
			_mediaPos = Muted("0:00", 12);
			_mediaLen = Muted("", 12);
			_mediaLen.HorizontalTextAlignment = TextAlignment.End;
			var times = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)] };
			times.Add(_mediaPos, 0, 0);
			times.Add(_mediaLen, 1, 0);

			_mediaPrev = MediaButton("⏮", "WMediaPrev", "previous");
			_mediaPlay = MediaButton("⏯", "WMediaPlay", "toggle");
			_mediaNext = MediaButton("⏭", "WMediaNext", "next");
			var buttons = new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center, Children = { _mediaPrev, _mediaPlay, _mediaNext } };
			_mediaBody.Children.Add(top);
			_mediaBody.Children.Add(_mediaBar);
			_mediaBody.Children.Add(times);
			_mediaBody.Children.Add(buttons);
			_mediaKey = string.Empty;
		}

		if (media is null)
		{
			_mediaTitle!.Text = Loc.T("WMediaIdle");
			_mediaArtist!.Text = string.Empty;
			_mediaApp!.Text = string.Empty;
			_coverImage!.Source = null;
			_mediaBar!.Progress = 0;
			_mediaPos!.Text = string.Empty;
			_mediaLen!.Text = string.Empty;
			_mediaPrev!.IsEnabled = _mediaPlay!.IsEnabled = _mediaNext!.IsEnabled = false;
			_mediaKey = string.Empty;
			return;
		}

		_mediaTitle!.Text = media.Title.Length == 0 ? "—" : media.Title;
		_mediaArtist!.Text = media.Artist;
		_mediaApp!.Text = media.App;
		_mediaPlay!.Text = media.Playing ? "⏸" : "▶";
		_mediaPrev!.IsEnabled = media.Prev;
		_mediaNext!.IsEnabled = media.Next;
		_mediaPlay.IsEnabled = media.Play;
		_mediaBar!.Progress = media.Len > 0 ? Math.Clamp(media.Pos / media.Len, 0, 1) : 0;
		_mediaPos!.Text = FmtClock(media.Pos);
		_mediaLen!.Text = media.Len > 0 ? FmtClock(media.Len) : string.Empty;

		if (_mediaKey != media.CoverKey)
		{
			// Kapak yalnızca parça değişince yeniden okunur; yeni kapak henüz gelmediyse eskisi silinir.
			_mediaKey = media.CoverKey;
			_coverImage!.Source = App.Link.Images.TryGetValue("cover:" + media.CoverKey, out var cover) ? ImageOf(cover) : null;
		}
	}

	private Button MediaButton(string glyph, string tipKey, string action)
	{
		var button = new Button { Text = glyph, Style = Res("SecondaryButton"), WidthRequest = 64, HeightRequest = 46, FontSize = 20 };
		AutomationProperties.SetHelpText(button, Loc.T(tipKey));
		button.Clicked += async (_, _) => await App.Link.SendCommandAsync(new { cmd = "media", action });
		return button;
	}

	// ---- Yapılacaklar (özet; ayrıntı Görevler sekmesinde) ----

	private void BuildTodo()
	{
		var state = App.Link.Tasks;
		var todos = state is { Enabled: true } ? (state.Todos ?? []).Where(t => !t.Done).Take(6).ToList() : [];
		var key = "todo:" + string.Join("|", todos.Select(t => $"{t.Id}:{t.Text}")) + (state is null ? "n" : state.Enabled ? "y" : "x");
		if (_todoBody!.Children.Count > 0 && _todoKey == key)
		{
			return;
		}

		_todoKey = key;
		_todoBody.Children.Clear();
		if (state is null)
		{
			_todoBody.Children.Add(Muted(Loc.T("TasksOldPc")));
			return;
		}

		if (!state.Enabled)
		{
			_todoBody.Children.Add(Muted(Loc.T("TasksOff")));
			return;
		}

		if (todos.Count == 0)
		{
			_todoBody.Children.Add(Muted(Loc.T("TodoEmpty")));
		}

		foreach (var todo in todos)
		{
			var check = new CheckBox { IsChecked = false, Color = Color.FromArgb("#3B82F6"), VerticalOptions = LayoutOptions.Center };
			var id = todo.Id;
			check.CheckedChanged += async (_, e) => await App.Link.SendCommandAsync(new { cmd = "todoToggle", id, done = e.Value });
			var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], MinimumHeightRequest = 40 };
			row.Add(check, 0, 0);
			row.Add(Strong(todo.Text, 15), 1, 0);
			((Label)row.Children[1]).VerticalOptions = LayoutOptions.Center;
			_todoBody.Children.Add(row);
		}

		var open = new Button { Text = Loc.T("WTodoOpen"), Style = Res("SecondaryButton"), HeightRequest = 38, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(16, 0) };
		open.Clicked += (_, _) => ShowTab(Tab.Tasks);
		_todoBody.Children.Add(open);
	}

	private string _todoKey = string.Empty;

	// ---- Başlatıcı ----

	private string _launchKey = string.Empty;

	private void BuildLauncher(LaunchState? launch)
	{
		var items = launch?.Items ?? [];
		var key = string.Join("|", items.Select(i => $"{i.Id}:{i.Name}:{App.Link.Images.ContainsKey("icon:" + i.Id)}"));
		if (_launchBody!.Children.Count > 0 && _launchKey == key)
		{
			return;
		}

		_launchKey = key;
		_launchBody.Children.Clear();
		if (items.Count == 0)
		{
			_launchBody.Children.Add(Muted(Loc.T("WLauncherEmpty")));
			return;
		}

		var wrap = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start };
		foreach (var item in items)
		{
			var icon = new Image { WidthRequest = 40, HeightRequest = 40, HorizontalOptions = LayoutOptions.Center };
			if (App.Link.Images.TryGetValue("icon:" + item.Id, out var png))
			{
				icon.Source = ImageOf(png);
			}

			var name = new Label { Text = item.Name, FontSize = 12, TextColor = Light, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
			var tile = new VerticalStackLayout { Spacing = 4, Children = { icon, name } };
			var button = new Border
			{
				Style = Res("CardStyle"),
				BackgroundColor = Color.FromArgb("#1A1F2B"),
				Padding = new Thickness(8, 10),
				WidthRequest = 96,
				Margin = new Thickness(0, 0, 8, 8),
				Content = tile
			};
			var id = item.Id;
			var title = item.Name;
			var tap = new TapGestureRecognizer();
			tap.Tapped += async (_, _) =>
			{
				if (await App.Link.SendCommandAsync(new { cmd = "launch", id }))
				{
					FlashWidgetStatus(Loc.T("WLaunching", title));
				}
			};
			button.GestureRecognizers.Add(tap);
			wrap.Children.Add(button);
		}

		_launchBody.Children.Add(wrap);
		_launchBody.Children.Add(Muted(Loc.T("WLauncherHint"), 12));
	}

	// ---- Görev yöneticisi ----

	private void BuildProcesses(ProcState? proc)
	{
		_procBody!.Children.Clear();
		if (proc is null || proc.Rows is null)
		{
			_procBody.Children.Add(Muted(Loc.T("WTasksWait")));
			return;
		}

		// Sistem toplamları
		var parts = new List<string>();
		if (proc.CpuLoad is float cpu) parts.Add($"CPU {cpu:0}%");
		if (proc.RamUsedGb is float used && proc.RamTotalGb is float total) parts.Add($"RAM {used:0.0}/{total:0} GB");
		if (proc.GpuLoad is float gpu) parts.Add($"GPU {gpu:0}%");
		if (proc.VramUsedGb is float vu && proc.VramTotalGb is float vt) parts.Add($"VRAM {vu:0.0}/{vt:0} GB");
		if (parts.Count > 0)
		{
			_procBody.Children.Add(Muted(string.Join("  ·  ", parts), 12));
		}

		// Sıralama düğmeleri
		var sorts = new HorizontalStackLayout { Spacing = 6 };
		foreach (var (code, label) in new[] { ("cpu", "CPU"), ("ram", "RAM"), ("disk", "DISK"), ("net", "NET") })
		{
			var button = new Button { Text = label, Style = TabStyle(_procSort == code), HeightRequest = 36, Padding = new Thickness(12, 0), FontSize = 12 };
			var chosen = code;
			button.Clicked += (_, _) =>
			{
				_procSort = chosen;
				BuildProcesses(App.Link.Widgets?.Proc);
			};
			sorts.Children.Add(button);
		}

		_procBody.Children.Add(sorts);

		IEnumerable<ProcInfo> ordered = _procSort switch
		{
			"ram" => proc.Rows.OrderByDescending(r => r.Ram),
			"disk" => proc.Rows.OrderByDescending(r => r.Disk),
			"net" => proc.Rows.OrderByDescending(r => r.Net),
			_ => proc.Rows.OrderByDescending(r => r.Cpu)
		};

		var grid = new Grid { RowSpacing = 4, ColumnSpacing = 6 };
		grid.ColumnDefinitions =
		[
			new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(56)), new ColumnDefinition(new GridLength(68)),
			new ColumnDefinition(new GridLength(78)), new ColumnDefinition(new GridLength(78))
		];
		string[] headers = [Loc.T("WTasksApp"), "CPU", "RAM", "DISK", "NET"];
		for (var c = 0; c < headers.Length; c++)
		{
			var header = Muted(headers[c], 11);
			header.HorizontalTextAlignment = c == 0 ? TextAlignment.Start : TextAlignment.End;
			grid.Add(header, c, 0);
		}

		var rowIndex = 1;
		foreach (var row in ordered.Take(8))
		{
			grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var name = Strong(row.C > 1 ? $"{row.N} ×{row.C}" : row.N, 14);
			grid.Add(name, 0, rowIndex);
			AddCell(grid, 1, rowIndex, row.Cpu >= 0.05 ? $"{row.Cpu:0.0}%" : "0%", _procSort == "cpu", row.Cpu >= 50);
			AddCell(grid, 2, rowIndex, FmtBytes(row.Ram), _procSort == "ram", false);
			AddCell(grid, 3, rowIndex, FmtRate(row.Disk), _procSort == "disk", false);
			AddCell(grid, 4, rowIndex, proc.Net ? FmtRate(row.Net) : "--", _procSort == "net", false);
			rowIndex++;
		}

		_procBody.Children.Add(grid);
		if (!proc.Net)
		{
			_procBody.Children.Add(Muted(Loc.T("WTasksNet"), 11));
		}
	}

	private static void AddCell(Grid grid, int column, int row, string text, bool sorted, bool hot)
	{
		var label = new Label
		{
			Text = text,
			FontSize = 12,
			HorizontalTextAlignment = TextAlignment.End,
			LineBreakMode = LineBreakMode.NoWrap,
			FontAttributes = sorted ? FontAttributes.Bold : FontAttributes.None,
			TextColor = hot ? Hot : sorted ? Light : Subtle
		};
		grid.Add(label, column, row);
	}

	// ---- Pano ----

	private string _clipKey = string.Empty;

	private void BuildClipboard(ClipState? clip)
	{
		var items = clip?.Items ?? [];
		var key = $"{clip?.On}|" + string.Join("|", items.Select(i => $"{i.K}:{App.Link.Images.ContainsKey("clip:" + i.K)}"));
		if (_clipBody!.Children.Count > 0 && _clipKey == key)
		{
			return;
		}

		_clipKey = key;
		_clipBody.Children.Clear();
		if (clip is null || !clip.On)
		{
			_clipBody.Children.Add(Muted(Loc.T("WClipOff")));
		}
		else if (items.Count == 0)
		{
			_clipBody.Children.Add(Muted(Loc.T("WClipEmpty")));
		}
		else
		{
			foreach (var item in items)
			{
				_clipBody.Children.Add(ClipRow(item));
			}

			_clipBody.Children.Add(Muted(Loc.T("WClipHint"), 11));
		}

		var send = new Button { Text = Loc.T("WClipSend"), Style = Res("SecondaryButton"), HeightRequest = 40, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(16, 0) };
		send.Clicked += async (_, _) =>
		{
			string? text = null;
			try
			{
				text = await Clipboard.Default.GetTextAsync();
			}
			catch (Exception)
			{
				// Android bazı sürümlerde arka plandayken panoyu vermez.
			}

			if (string.IsNullOrWhiteSpace(text))
			{
				FlashWidgetStatus(Loc.T("WClipEmptyPhone"), error: true);
				return;
			}

			if (await App.Link.SendCommandAsync(new { cmd = "clipSend", text = text.Length > 1000 ? text[..1000] : text }))
			{
				FlashWidgetStatus(Loc.T("WClipSent"));
			}
		};
		_clipBody.Children.Add(send);
	}

	private View ClipRow(ClipItem item)
	{
		View content;
		if (item.T is null)
		{
			var image = new Image { HeightRequest = 48, WidthRequest = 90, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Start };
			if (App.Link.Images.TryGetValue("clip:" + item.K, out var jpeg))
			{
				image.Source = ImageOf(jpeg);
			}

			content = new HorizontalStackLayout { Spacing = 10, Children = { image, new Label { Text = Loc.T("WClipImage", item.W, item.H), FontSize = 12, TextColor = Subtle, VerticalOptions = LayoutOptions.Center } } };
		}
		else
		{
			var oneLine = string.Join(' ', item.T.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
			content = new Label { Text = oneLine, FontSize = 14, TextColor = Light, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
		}

		var row = new Border { BackgroundColor = Color.FromArgb("#1A1F2B"), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(10, 8), Content = content };
		if (item.T is { } text)
		{
			var tap = new TapGestureRecognizer();
			tap.Tapped += async (_, _) =>
			{
				await Clipboard.Default.SetTextAsync(text);
				FlashWidgetStatus(Loc.T("WClipCopied"));
			};
			row.GestureRecognizers.Add(tap);
		}

		return row;
	}

	// ---- Borsa ----

	private void BuildMarket(List<MarketInfo>? market)
	{
		_marketBody!.Children.Clear();
		if (market is null)
		{
			_marketBody.Children.Add(Muted(Loc.T("WMarketOff")));
			return;
		}

		foreach (var quote in market)
		{
			var name = Strong(MarketName(quote.S), 15);
			name.FontAttributes = FontAttributes.Bold;
			var price = Strong(quote.Price is double p ? p.ToString(p >= 1000 ? "#,##0.00" : p >= 10 ? "0.00" : "0.0000", CultureInfo.CurrentCulture) : "…", 15);
			price.HorizontalTextAlignment = TextAlignment.End;
			var change = new Label
			{
				Text = quote.Chg is double c ? $"{(c >= 0 ? "▲" : "▼")} {Math.Abs(c):0.00}%" : "--",
				FontSize = 12,
				HorizontalTextAlignment = TextAlignment.End,
				TextColor = quote.Chg is double d ? d >= 0 ? Good : Hot : Subtle
			};
			var right = new VerticalStackLayout { Children = { price, change } };
			var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], MinimumHeightRequest = 44 };
			row.Add(name, 0, 0);
			((Label)row.Children[0]).VerticalOptions = LayoutOptions.Center;
			row.Add(right, 1, 0);
			_marketBody.Children.Add(row);
		}

		if (market.Count == 0)
		{
			_marketBody.Children.Add(Muted(Loc.T("WMarketOff")));
		}
	}

	/// <summary>"BTC-USD" → "BTC/USD", "USDTRY=X" → "USD/TRY" (PC'deki widget'la aynı).</summary>
	internal static string MarketName(string symbol)
	{
		if (symbol.EndsWith("=X", StringComparison.OrdinalIgnoreCase) && symbol.Length == 8)
		{
			return $"{symbol[..3]}/{symbol[3..6]}";
		}

		return symbol.Contains('-') && !symbol.StartsWith('^') ? symbol.Replace('-', '/') : symbol;
	}

	// ---- Pil ----

	private void BuildBattery(WidgetBattery? battery)
	{
		_batteryBody!.Children.Clear();
		if (battery?.Pc is { } pc)
		{
			var status = pc.Charging ? Loc.T("WBatteryCharging") : pc.Mins is int m and > 0 ? Loc.T("WBatteryLeft", m >= 60 ? Loc.T("WBatteryHM", m / 60, m % 60) : Loc.T("MinShort", m)) : string.Empty;
			_batteryBody.Children.Add(BatteryRow(Loc.T("WBatteryPc"), pc.Pct, pc.Charging ? "⚡" : null));
			if (status.Length > 0)
			{
				_batteryBody.Children.Add(Muted(status, 12));
			}
		}
		else
		{
			_batteryBody.Children.Add(Muted(Loc.T("WBatteryNone")));
		}

		var devices = battery?.Devices ?? [];
		if (devices.Count == 0)
		{
			_batteryBody.Children.Add(Muted(Loc.T("WBatteryNoDevices"), 12));
		}

		foreach (var device in devices)
		{
			_batteryBody.Children.Add(BatteryRow(device.N, device.P, null));
		}
	}

	private static View BatteryRow(string name, int percent, string? badge)
	{
		var bar = new BarView { Progress = Math.Clamp(percent, 0, 100) / 100.0 };
		var label = Strong(name, 14);
		var value = new Label { Text = $"{badge} {percent}%".Trim(), FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = percent <= 20 ? Hot : percent <= 40 ? Warn : Good, HorizontalTextAlignment = TextAlignment.End };
		var top = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
		top.Add(label, 0, 0);
		top.Add(value, 1, 0);
		return new VerticalStackLayout { Spacing = 4, Children = { top, bar } };
	}

	// ---- Sürüm eşitleme ----

	/// <summary>PC'nin dHud sürümü telefondakinden yeniyse hemen güncelleme aranır (PC ve telefon aynı sürümde kalsın).</summary>
	private async void OnPcVersion(string pcVersion)
	{
		if (!Version.TryParse(pcVersion, out var pc) || !Version.TryParse(AppInfo.Current.VersionString, out var mine))
		{
			return;
		}

		if (pc > mine)
		{
			await CheckForUpdateAsync(manual: false);
			if (_update is not null)
			{
				UpdateLabel.Text = Loc.T("PcNewer", pcVersion) + "  " + Loc.T("UpdateAvail", _update.Tag);
			}
		}
		else if (pc < mine)
		{
			FlashWidgetStatus(Loc.T("PhoneNewer", mine.ToString(), pcVersion), error: true);
		}
	}

	/// <summary>Widget komutlarının (medya, başlatıcı, pano) hata kodlarını okunur iletiye çevirir.</summary>
	private string? WidgetErrorText(string cmd, string? error)
	{
		if (cmd is not ("media" or "launch" or "clipSend"))
		{
			return null;
		}

		return error switch
		{
			"disabled" => Loc.T("WOff"),
			"nomedia" => Loc.T("WNoMedia"),
			"missing" => Loc.T("WMissing"),
			_ => Loc.T("WFailed")
		};
	}
}
