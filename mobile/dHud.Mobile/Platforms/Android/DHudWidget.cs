using Android.App;
using Android.Appwidget;
using Android.Content;
using AColor = Android.Graphics.Color;
using Android.Widget;
using dHud.Mobile.Services;

namespace dHud.Mobile;

/// <summary>
/// Ana ekran widget'ı: CPU / GPU sıcaklığı ve FPS. Uygulama açıkken canlı güncellenir; kapalıyken Android'in izin verdiği
/// aralıkta (en sık ~30 dk) veya ⟳ düğmesine dokununca PC'ye kısa bir bağlantı kurup yeniler.
/// </summary>
[BroadcastReceiver(Label = "dHud", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/dhud_widget")]
public class DHudWidget : AppWidgetProvider
{
	private static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);

	public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
	{
		base.OnUpdate(context, appWidgetManager, appWidgetIds);
		if (context is null || appWidgetManager is null || appWidgetIds is null || appWidgetIds.Length == 0)
		{
			return;
		}

		// Önce son bilinen değerleri hemen göster, sonra PC'den tazesini almayı dene.
		Render(context, appWidgetManager, appWidgetIds);

		var pending = GoAsync();
		_ = Task.Run(async () =>
		{
			try
			{
				if (await WidgetFetcher.FetchAsync() is { } snapshot)
				{
					WidgetCache.Save(snapshot);
					Render(context, appWidgetManager, appWidgetIds);
				}
			}
			catch (Exception)
			{
				// Ağ yok / PC kapalı: eski değerler kalır.
			}
			finally
			{
				pending?.Finish();
			}
		});
	}

	/// <summary>Var olan tüm widget'ları son önbellekle yeniden çizer.</summary>
	public static void UpdateAll(Context context)
	{
		var manager = AppWidgetManager.GetInstance(context);
		if (manager is null)
		{
			return;
		}

		var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(DHudWidget))));
		if (ids is { Length: > 0 })
		{
			Render(context, manager, ids);
		}
	}

	/// <summary>Kullanıcı widget'ı yeniden boyutlandırınca (küçük/orta/büyük düzen arasında) yeniden çizer.</summary>
	public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Android.OS.Bundle? newOptions)
	{
		base.OnAppWidgetOptionsChanged(context, appWidgetManager, appWidgetId, newOptions);
		if (context is not null && appWidgetManager is not null)
		{
			Render(context, appWidgetManager, [appWidgetId]);
		}
	}

	/// <summary>Widget'ın şu anki boyutuna (dp) göre düzen: 1 satır küçük, 2 satır orta, 3+ satır büyük.</summary>
	private static WidgetSize SizeOf(AppWidgetManager manager, int id)
	{
		var options = manager.GetAppWidgetOptions(id);
		var width = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0;
		var height = options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;
		return WidgetLayout.Choose(width, height);
	}

	private static void Render(Context context, AppWidgetManager manager, int[] ids)
	{
		var data = WidgetCache.Load();
		var age = data is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(data.Timestamp);
		var fresh = age < Stale;
		var gray = AColor.Rgb(0x7B, 0x84, 0x94);

		foreach (var id in ids)
		{
			var size = SizeOf(manager, id);
			var layout = size switch
			{
				WidgetSize.Small => Resource.Layout.widget_dhud_small,
				WidgetSize.Large => Resource.Layout.widget_dhud_large,
				_ => Resource.Layout.widget_dhud
			};
			var views = new RemoteViews(context.PackageName, layout);

			// Üç düzende ortak: sıcaklıklar ve ⟳ düğmesi. Diğer görünümler yalnızca o düzende bulunur (olmayan kimliğe yazmak widget'ı bozar).
			SetTemp(views, Resource.Id.widget_cpu, data?.Cpu, fresh);
			SetTemp(views, Resource.Id.widget_gpu, data?.Gpu, fresh);

			if (size != WidgetSize.Small)
			{
				views.SetTextViewText(Resource.Id.widget_fps, fresh && data?.Fps is float fps ? $"{fps:0}" : "--");
				views.SetTextColor(Resource.Id.widget_fps, fresh && data?.Fps is float f ? FpsColor(f) : gray);
				views.SetTextViewText(Resource.Id.widget_title, string.IsNullOrEmpty(data?.Pc) ? "dHud" : $"dHud · {data!.Pc}");
				views.SetTextViewText(Resource.Id.widget_status,
					data is null ? Loc.T("WidgetNoData")
					: fresh ? Loc.T("WidgetUpdated", WidgetBridge.Describe(data))
					: Loc.T("WidgetStale", WidgetBridge.Describe(data)));
			}

			if (size == WidgetSize.Large)
			{
				SetLoad(views, Resource.Id.widget_cpu_load, data?.CpuLoad, fresh, gray);
				SetLoad(views, Resource.Id.widget_gpu_load, data?.GpuLoad, fresh, gray);
				SetLoad(views, Resource.Id.widget_ram, data?.Ram, fresh, AColor.Rgb(0xF1, 0xF2, 0xF6));
				SetLoad(views, Resource.Id.widget_disk, data?.Disk, fresh, AColor.Rgb(0xF1, 0xF2, 0xF6));
				views.SetProgressBar(Resource.Id.widget_ram_bar, 100, fresh ? WidgetLayout.Bar(data?.Ram) : 0, false);
				views.SetProgressBar(Resource.Id.widget_disk_bar, 100, fresh ? WidgetLayout.Bar(data?.Disk) : 0, false);
			}

			// Widget'a dokununca uygulama açılır.
			var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
			if (launch is not null)
			{
				views.SetOnClickPendingIntent(Resource.Id.widget_root,
					PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
			}

			// ⟳ düğmesi: bu widget'ı yenile.
			var refresh = new Intent(context, typeof(DHudWidget));
			refresh.SetAction(AppWidgetManager.ActionAppwidgetUpdate);
			refresh.PutExtra(AppWidgetManager.ExtraAppwidgetIds, new[] { id });
			views.SetOnClickPendingIntent(Resource.Id.widget_refresh,
				PendingIntent.GetBroadcast(context, id, refresh, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));

			manager.UpdateAppWidget(id, views);
		}
	}

	/// <summary>Yük/doluluk yüzdesi; yüksekse sarı/kırmızı, bayatsa gri.</summary>
	private static void SetLoad(RemoteViews views, int viewId, float? percent, bool fresh, AColor normal)
	{
		var show = fresh && percent is >= 0;
		views.SetTextViewText(viewId, show ? WidgetLayout.Percent(percent) : "--%");
		views.SetTextColor(viewId, !show ? AColor.Rgb(0x7B, 0x84, 0x94) : WidgetLayout.Level(percent) switch
		{
			2 => AColor.Rgb(0xF8, 0x71, 0x71),
			1 => AColor.Rgb(0xFA, 0xCC, 0x15),
			_ => normal
		});
	}

	private static void SetTemp(RemoteViews views, int viewId, float? temp, bool fresh)
	{
		var show = fresh && temp is > 0;
		views.SetTextViewText(viewId, show ? $"{temp:0}°" : "--°");
		views.SetTextColor(viewId, show ? TempColor(temp!.Value) : AColor.Rgb(0x7B, 0x84, 0x94));
	}

	private static AColor TempColor(float t) => t switch
	{
		>= 80 => AColor.Rgb(0xF8, 0x71, 0x71),
		>= 60 => AColor.Rgb(0xFA, 0xCC, 0x15),
		_ => AColor.Rgb(0x4A, 0xDE, 0x80)
	};

	private static AColor FpsColor(float fps) => fps switch
	{
		>= 60 => AColor.Rgb(0x4A, 0xDE, 0x80),
		>= 30 => AColor.Rgb(0xFA, 0xCC, 0x15),
		_ => AColor.Rgb(0xF8, 0x71, 0x71)
	};
}
