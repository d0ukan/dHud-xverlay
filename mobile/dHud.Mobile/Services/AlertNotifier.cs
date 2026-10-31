namespace dHud.Mobile.Services;

/// <summary>Sıcaklık uyarısında titreşim ve (uygulama arka plandayken de görünen) sistem bildirimi.</summary>
public static class AlertNotifier
{
	private const string ChannelId = "dhud_alerts";

	public static async Task RequestPermissionAsync()
	{
		try
		{
			await Permissions.RequestAsync<Permissions.PostNotifications>();
		}
		catch (Exception)
		{
			// Bildirim izni alınamadı; uyarı yalnızca uygulama içinde görünür.
		}
	}

	public static void Notify(string title, string text)
	{
		try
		{
			Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(800));
		}
		catch (Exception)
		{
			// Titreşim desteklenmiyor.
		}

#if ANDROID
		try
		{
			var context = Android.App.Application.Context;
			var manager = (Android.App.NotificationManager)context.GetSystemService(Android.Content.Context.NotificationService)!;
			if (OperatingSystem.IsAndroidVersionAtLeast(26))
			{
				manager.CreateNotificationChannel(new Android.App.NotificationChannel(
					ChannelId, "dHud", Android.App.NotificationImportance.High));
			}

			var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!);
			var pending = Android.App.PendingIntent.GetActivity(context, 0, launch,
				Android.App.PendingIntentFlags.Immutable | Android.App.PendingIntentFlags.UpdateCurrent);

			var notification = new AndroidX.Core.App.NotificationCompat.Builder(context, ChannelId)
				.SetSmallIcon(Android.Resource.Drawable.IcDialogAlert)!
				.SetContentTitle(title)!
				.SetContentText(text)!
				.SetStyle(new AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(text))!
				.SetPriority(AndroidX.Core.App.NotificationCompat.PriorityHigh)!
				.SetDefaults(AndroidX.Core.App.NotificationCompat.DefaultAll)!
				.SetAutoCancel(true)!
				.SetContentIntent(pending)!
				.Build()!;
			manager.Notify(1001, notification);
		}
		catch (Exception)
		{
			// Bildirim gösterilemedi (izin yok vb.); uygulama içi uyarı yine görünür.
		}
#endif
	}
}
