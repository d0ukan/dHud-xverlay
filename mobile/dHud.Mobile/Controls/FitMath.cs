namespace dHud.Mobile.Controls;

/// <summary>Panelin ekrana sığdırılması için saf hesap (MAUI'ye bağımlı değil; PC test projesinde de çalışır).</summary>
public static class FitMath
{
	/// <summary>
	/// İçerik doğal yüksekliğiyle ekrana sığmıyorsa onu sığdıracak ölçek (en çok 1, en az <paramref name="minScale"/>).
	/// Geçersiz değerlerde 1 döner.
	/// </summary>
	public static double Scale(double viewportHeight, double naturalHeight, double minScale)
	{
		if (!double.IsFinite(viewportHeight) || !double.IsFinite(naturalHeight) || viewportHeight <= 0 || naturalHeight <= 0)
		{
			return 1;
		}

		return Math.Clamp(viewportHeight / naturalHeight, Math.Min(minScale, 1), 1);
	}

	/// <summary>Ölçekli içerik görünür alana tam sığıyor mu (kaydırma gerekmez)?</summary>
	public static bool Fits(double viewportHeight, double naturalHeight, double scale) => naturalHeight * scale <= viewportHeight + 1;
}
