namespace TexFury;

/// <summary>Compression quality metrics comparing original and decompressed pixels.</summary>
public sealed class QualityMetrics
{
    public double PsnrRgb { get; }
    public double PsnrRgba { get; }
    public double Ssim { get; }

    public QualityMetrics(double psnrRgb, double psnrRgba, double ssim)
    {
        PsnrRgb = psnrRgb;
        PsnrRgba = psnrRgba;
        Ssim = ssim;
    }

    public override string ToString() =>
        $"QualityMetrics(PSNR_RGB={PsnrRgb:F2}dB, PSNR_RGBA={PsnrRgba:F2}dB, SSIM={Ssim:F4})";
}
