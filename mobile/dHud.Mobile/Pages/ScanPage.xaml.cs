using BarcodeScanning;
using dHud.Mobile.Services;

namespace dHud.Mobile.Pages;

public partial class ScanPage : ContentPage
{
	private bool _handled;

	public ScanPage()
	{
		InitializeComponent();
		HintLabel.Text = Loc.T("ScanHint");
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (await Methods.AskForRequiredPermissionAsync())
		{
			Camera.CameraEnabled = true;
		}
		else
		{
			await DisplayAlert("dHud", Loc.T("NoCamera"), Loc.T("Ok"));
			await Navigation.PopAsync();
		}
	}

	protected override void OnDisappearing()
	{
		Camera.CameraEnabled = false;
		base.OnDisappearing();
	}

	private async void Camera_OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
	{
		if (_handled)
		{
			return;
		}

		var text = e.BarcodeResults.Select(b => b.DisplayValue).FirstOrDefault(v => !string.IsNullOrEmpty(v));
		if (text is null)
		{
			return;
		}

		var info = PairingStore.Parse(text);
		if (info is null)
		{
			// Başka bir QR kod okundu; taramaya devam et.
			return;
		}

		_handled = true;
		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			MainPage.Pair(info);
			await Navigation.PopAsync();
		});
	}
}
