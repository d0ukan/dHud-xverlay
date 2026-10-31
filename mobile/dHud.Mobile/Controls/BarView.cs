namespace dHud.Mobile.Controls;

/// <summary>İnce, düz ilerleme çubuğu (Android'in kalın varsayılan çubuğu yerine). Yüksek değerde rengi uyarıya döner.</summary>
public sealed class BarView : ContentView
{
	private static readonly Color Track = Color.FromArgb("#FF1F2430");
	private static readonly Color Normal = Color.FromArgb("#FF3B82F6");
	private static readonly Color Warn = Color.FromArgb("#FFFACC15");
	private static readonly Color Hot = Color.FromArgb("#FFF87171");

	private readonly Grid _grid;
	private readonly BoxView _fill;
	private double _progress;

	public BarView()
	{
		_fill = new BoxView { Color = Normal };
		_grid = new Grid
		{
			BackgroundColor = Track,
			ColumnSpacing = 0,
			ColumnDefinitions =
			[
				new ColumnDefinition(new GridLength(0.0001, GridUnitType.Star)),
				new ColumnDefinition(new GridLength(1, GridUnitType.Star))
			]
		};
		_grid.Add(_fill);

		HeightRequest = 4;
		Content = new Border
		{
			StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
			Content = _grid
		};
	}

	/// <summary>0 ile 1 arası doluluk.</summary>
	public double Progress
	{
		get => _progress;
		set
		{
			_progress = Math.Clamp(value, 0, 1);
			_grid.ColumnDefinitions[0].Width = new GridLength(Math.Max(_progress, 0.0001), GridUnitType.Star);
			_grid.ColumnDefinitions[1].Width = new GridLength(Math.Max(1 - _progress, 0.0001), GridUnitType.Star);
			_fill.Color = _progress >= 0.9 ? Hot : _progress >= 0.75 ? Warn : Normal;
		}
	}
}
