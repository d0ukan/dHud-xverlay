using dHud.Mobile.Services;

namespace dHud.Mobile.Pages;

/// <summary>CPU ve GPU sıcaklık geçmişini çizen basit çizgi grafiği. En yeni değer sağda.</summary>
public sealed class TempGraph : IDrawable
{
	public static readonly Color CpuColor = Color.FromArgb("#3B82F6");
	public static readonly Color GpuColor = Color.FromArgb("#A78BFA");

	private const float LabelWidth = 40;
	private const float MinSpan = 20;

	public IReadOnlyList<float?> Cpu { get; set; } = [];
	public IReadOnlyList<float?> Gpu { get; set; } = [];

	public void Draw(ICanvas canvas, RectF area)
	{
		var values = Cpu.Concat(Gpu).Where(v => v.HasValue).Select(v => v!.Value).ToList();
		var min = values.Count > 0 ? values.Min() : 30;
		var max = values.Count > 0 ? values.Max() : 70;
		if (max - min < MinSpan)
		{
			var mid = (max + min) / 2;
			min = mid - MinSpan / 2;
			max = mid + MinSpan / 2;
		}

		// Yuvarlak değerlerle kılavuz çizgileri.
		min = MathF.Floor((min - 2) / 5) * 5;
		max = MathF.Ceiling((max + 2) / 5) * 5;

		var plot = new RectF(area.Left + LabelWidth, area.Top + 6, area.Width - LabelWidth - 6, area.Height - 12);

		canvas.FontSize = 11;
		canvas.FontColor = Color.FromArgb("#9CA3AF");
		canvas.StrokeSize = 1;
		for (var i = 0; i <= 4; i++)
		{
			var y = plot.Bottom - plot.Height * i / 4;
			var value = min + (max - min) * i / 4;
			canvas.StrokeColor = Color.FromArgb("#22FFFFFF");
			canvas.DrawLine(plot.Left, y, plot.Right, y);
			canvas.DrawString($"{value:0}°", area.Left, y - 8, LabelWidth - 6, 16,
				HorizontalAlignment.Right, VerticalAlignment.Center);
		}

		DrawSeries(canvas, Cpu, CpuColor, plot, min, max);
		DrawSeries(canvas, Gpu, GpuColor, plot, min, max);
	}

	private static void DrawSeries(ICanvas canvas, IReadOnlyList<float?> series, Color color, RectF plot, float min, float max)
	{
		if (series.Count < 2)
		{
			return;
		}

		canvas.StrokeColor = color;
		canvas.StrokeSize = 2.5f;
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.StrokeLineCap = LineCap.Round;

		var step = plot.Width / (PcLink.HistoryLength - 1);
		var path = new PathF();
		var open = false;
		for (var i = 0; i < series.Count; i++)
		{
			if (series[i] is not float value)
			{
				open = false; // boşluk: çizgi kesilir
				continue;
			}

			var x = plot.Right - (series.Count - 1 - i) * step;
			var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
			if (open)
			{
				path.LineTo(x, y);
			}
			else
			{
				path.MoveTo(x, y);
				open = true;
			}
		}

		canvas.DrawPath(path);
	}
}
