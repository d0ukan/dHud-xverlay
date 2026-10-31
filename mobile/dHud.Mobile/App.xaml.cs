using dHud.Mobile.Services;

namespace dHud.Mobile;

public partial class App : Application
{
	/// <summary>PC ile tek bağlantı; sayfalar arasında paylaşılır.</summary>
	public static PcLink Link { get; } = new();

	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new NavigationPage(new Pages.MainPage())
		{
			BarBackgroundColor = Color.FromArgb("#141418"),
			BarTextColor = Colors.White
		});
}
