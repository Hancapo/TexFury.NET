namespace TexFury;

/// <summary>Downsampling filter for mipmap generation and image resizing.</summary>
public enum MipFilter
{
    /// <summary>Simple average. Fast, correct for exact 2:1 downscale.</summary>
    Box = 0,

    /// <summary>Bilinear interpolation.</summary>
    Triangle = 1,

    /// <summary>Gaussian-like smoothing (B=1, C=0).</summary>
    CubicBSpline = 2,

    /// <summary>Sharp cubic interpolation.</summary>
    CatmullRom = 3,

    /// <summary>Balanced sharpness/smoothness (B=1/3, C=1/3). Best general-purpose.</summary>
    Mitchell = 4,

    /// <summary>Nearest-neighbor. No interpolation.</summary>
    Point = 5,
}
