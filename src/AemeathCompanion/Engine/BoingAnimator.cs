namespace AemeathCompanion.Engine;

/// <summary>
/// Single-cycle volume-preserving Squash &amp; Stretch ("뽀잉~") animator for tactile click feedback.
/// Phase 1 (0..75ms): smooth squish down (wide &amp; soft).
/// Phase 2 (75..195ms): single joyful upward stretch ("뽀잉!").
/// Phase 3 (195..340ms): smooth settle directly back to (1.0, 1.0) with no extra bounce.
/// </summary>
public sealed class BoingAnimator
{
    public const double SquishPeakSeconds = 0.075;
    public const double StretchPeakSeconds = 0.195;
    public const double TotalDurationSeconds = 0.34;
    private const double SquishDepth = 0.15;
    private const double StretchHeight = 0.11;

    private double _elapsed = TotalDurationSeconds;
    private double _intensity = 1.0;

    public bool IsActive => _elapsed < TotalDurationSeconds;
    public double ScaleX { get; private set; } = 1.0;
    public double ScaleY { get; private set; } = 1.0;

    public void Trigger(double intensity = 1.0)
    {
        _intensity = Math.Clamp(intensity, 0.3, 1.4);
        _elapsed = 0.0;
        (ScaleX, ScaleY) = Evaluate(_elapsed, _intensity);
    }

    public void Update(double dt)
    {
        if (!IsActive)
        {
            ScaleX = 1.0;
            ScaleY = 1.0;
            return;
        }

        _elapsed += Math.Max(0.0, dt);
        if (_elapsed >= TotalDurationSeconds)
        {
            _elapsed = TotalDurationSeconds;
            ScaleX = 1.0;
            ScaleY = 1.0;
            return;
        }

        (ScaleX, ScaleY) = Evaluate(_elapsed, _intensity);
    }

    public static (double ScaleX, double ScaleY) Evaluate(double elapsedSeconds, double intensity = 1.0)
    {
        if (elapsedSeconds <= 0.0 || elapsedSeconds >= TotalDurationSeconds)
            return (1.0, 1.0);

        double squish = -SquishDepth * intensity;
        double stretch = StretchHeight * intensity;
        double deltaY;

        if (elapsedSeconds <= SquishPeakSeconds)
        {
            double p = elapsedSeconds / SquishPeakSeconds;
            deltaY = squish * SmoothCosine(p);
        }
        else if (elapsedSeconds <= StretchPeakSeconds)
        {
            double p = (elapsedSeconds - SquishPeakSeconds) / (StretchPeakSeconds - SquishPeakSeconds);
            deltaY = squish + (stretch - squish) * SmoothCosine(p);
        }
        else
        {
            double p = (elapsedSeconds - StretchPeakSeconds) / (TotalDurationSeconds - StretchPeakSeconds);
            deltaY = stretch * (1.0 - SmoothCosine(p));
        }

        double scaleY = Math.Clamp(1.0 + deltaY, 0.76, 1.22);
        // Volume-preserving horizontal counter-scale
        double scaleX = Math.Clamp(1.0 / Math.Pow(scaleY, 0.85), 0.82, 1.26);
        return (scaleX, scaleY);
    }

    private static double SmoothCosine(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return 0.5 - 0.5 * Math.Cos(Math.PI * t);
    }
}
