namespace AemeathCompanion.Engine;

/// <summary>
/// Volume-preserving Squash &amp; Stretch ("뽀잉~") spring animator for tactile click feedback.
/// Phase 1 (0..75ms): fast ease-out squish down (wide &amp; flat like soft mochi).
/// Phase 2 (75..560ms): underdamped harmonic spring rebound (tall stretch -> soft squish -> micro-bounce -> rest).
/// </summary>
public sealed class BoingAnimator
{
    public const double SquishPeakSeconds = 0.075;
    public const double TotalDurationSeconds = 0.56;
    private const double SquishAmplitude = 0.18;
    private const double DampingRate = 4.6;
    private const double AngularFrequency = 23.0;

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

        double amp = SquishAmplitude * intensity;
        double deltaY;

        if (elapsedSeconds <= SquishPeakSeconds)
        {
            // Fast cubic ease-out squish down
            double p = elapsedSeconds / SquishPeakSeconds;
            double easeOut = 1.0 - Math.Pow(1.0 - p, 3.0);
            deltaY = -amp * easeOut;
        }
        else
        {
            // Underdamped spring rebound with smooth fade over the final 80ms
            double u = elapsedSeconds - SquishPeakSeconds;
            double spring = -amp * Math.Exp(-DampingRate * u) * Math.Cos(AngularFrequency * u);

            double remain = TotalDurationSeconds - elapsedSeconds;
            double tailFade = remain < 0.08 ? Math.Clamp(remain / 0.08, 0.0, 1.0) : 1.0;
            deltaY = spring * tailFade;
        }

        double scaleY = Math.Clamp(1.0 + deltaY, 0.72, 1.24);
        // Volume-preserving horizontal counter-scale: area stays roughly constant
        double scaleX = Math.Clamp(1.0 / Math.Pow(scaleY, 0.85), 0.80, 1.30);
        return (scaleX, scaleY);
    }
}
