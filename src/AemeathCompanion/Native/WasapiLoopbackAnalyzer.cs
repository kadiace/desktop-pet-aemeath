using System.Runtime.InteropServices;

namespace AemeathCompanion.Native;

/// <summary>
/// Zero-dependency WASAPI Loopback audio capture + 1024-sample Hann FFT analyzer.
/// Outputs 24 logarithmic frequency bands [0..1] for the pixel-art audio equalizer.
/// Automatically sleeps when inactive to consume 0% CPU while not playing music.
/// </summary>
public sealed class WasapiLoopbackAnalyzer : IDisposable
{
    public const int BandCount = 24;
    private const int FftSize = 1024;
    private const uint AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x00000002;
    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;

    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _peaks = new float[BandCount];
    private readonly float[] _ring = new float[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly double[] _fftReal = new double[FftSize];
    private readonly double[] _fftImag = new double[FftSize];
    private readonly object _lock = new();

    private int _ringPos;
    private volatile bool _active;
    private volatile bool _disposed;
    private readonly AutoResetEvent _wakeEvent = new(false);
    private readonly Thread _worker;

    public WasapiLoopbackAnalyzer()
    {
        for (int i = 0; i < FftSize; i++)
            _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

        _worker = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "Aemeath.WasapiLoopback",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active) _wakeEvent.Set();
    }

    public void CopyLevels(float[] destBands, float[] destPeaks, float dt)
    {
        lock (_lock)
        {
            float decay = _active ? Math.Clamp(dt * 4.5f, 0.02f, 0.35f) : Math.Clamp(dt * 8.0f, 0.05f, 0.5f);
            float peakDecay = Math.Clamp(dt * 1.4f, 0.01f, 0.15f);

            for (int i = 0; i < BandCount; i++)
            {
                if (!_active)
                {
                    _bands[i] = Math.Max(0f, _bands[i] - decay);
                }

                if (_bands[i] >= _peaks[i])
                    _peaks[i] = _bands[i];
                else
                    _peaks[i] = Math.Max(_bands[i], _peaks[i] - peakDecay);

                if (i < destBands.Length) destBands[i] = _bands[i];
                if (i < destPeaks.Length) destPeaks[i] = _peaks[i];
            }
        }
    }

    private void CaptureLoop()
    {
        while (!_disposed)
        {
            if (!_active)
            {
                _wakeEvent.WaitOne(250);
                continue;
            }

            try
            {
                RunWasapiSession();
            }
            catch
            {
                // Audio device may have changed or be unavailable; retry gently
                Thread.Sleep(500);
            }
        }
    }

    private unsafe void RunWasapiSession()
    {
        var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        if (enumeratorType is null)
        {
            Thread.Sleep(1000);
            return;
        }

        var enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(enumeratorType);
        if (enumerator is null) return;

        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        nint mixFormatPtr = 0;

        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device);
            if (hr != 0 || device is null) { Thread.Sleep(500); return; }

            var iidAudioClient = typeof(IAudioClient).GUID;
            hr = device.Activate(ref iidAudioClient, 23 /* CLSCTX_ALL */, 0, out object audioObj);
            if (hr != 0 || audioObj is not IAudioClient client) { Thread.Sleep(500); return; }
            audioClient = client;

            hr = audioClient.GetMixFormat(out mixFormatPtr);
            if (hr != 0 || mixFormatPtr == 0) { Thread.Sleep(500); return; }

            var fmt = Marshal.PtrToStructure<WaveFormatEx>(mixFormatPtr);
            int sampleRate = Math.Max(8000, fmt.nSamplesPerSec);
            int channels = Math.Max(1, (int)fmt.nChannels);
            int bits = fmt.wBitsPerSample;
            bool isFloat = fmt.wFormatTag == 3 || (fmt.wFormatTag == 0xFFFE && bits == 32);

            long bufferDuration = 200_000; // 20ms in 100ns units
            hr = audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                bufferDuration,
                0,
                mixFormatPtr,
                0);
            if (hr != 0) { Thread.Sleep(500); return; }

            var iidCapture = typeof(IAudioCaptureClient).GUID;
            hr = audioClient.GetService(ref iidCapture, out object capObj);
            if (hr != 0 || capObj is not IAudioCaptureClient cap) { Thread.Sleep(500); return; }
            captureClient = cap;

            hr = audioClient.Start();
            if (hr != 0) { Thread.Sleep(500); return; }

            while (_active && !_disposed)
            {
                Thread.Sleep(16);

                bool gotData = false;
                while (captureClient.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
                {
                    hr = captureClient.GetBuffer(out nint dataPtr, out uint numFrames, out uint flags, out _, out _);
                    if (hr != 0) break;

                    try
                    {
                        bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || dataPtr == 0;
                        if (silent)
                        {
                            for (uint f = 0; f < numFrames; f++)
                            {
                                _ring[_ringPos] = 0f;
                                _ringPos = (_ringPos + 1) & (FftSize - 1);
                            }
                        }
                        else if (isFloat && bits == 32)
                        {
                            float* src = (float*)dataPtr;
                            for (uint f = 0; f < numFrames; f++)
                            {
                                float sum = 0f;
                                for (int c = 0; c < channels; c++) sum += src[f * channels + c];
                                _ring[_ringPos] = sum / channels;
                                _ringPos = (_ringPos + 1) & (FftSize - 1);
                            }
                        }
                        else if (bits == 16)
                        {
                            short* src = (short*)dataPtr;
                            for (uint f = 0; f < numFrames; f++)
                            {
                                float sum = 0f;
                                for (int c = 0; c < channels; c++) sum += src[f * channels + c] / 32768f;
                                _ring[_ringPos] = sum / channels;
                                _ringPos = (_ringPos + 1) & (FftSize - 1);
                            }
                        }

                        gotData = true;
                    }
                    finally
                    {
                        captureClient.ReleaseBuffer(numFrames);
                    }
                }

                if (gotData)
                {
                    ComputeFftBands(sampleRate);
                }
                else
                {
                    // Loopback emits no packets during total system silence; smoothly decay bars
                    lock (_lock)
                    {
                        for (int i = 0; i < BandCount; i++)
                            _bands[i] *= 0.84f;
                    }
                }
            }

            audioClient.Stop();
        }
        finally
        {
            if (mixFormatPtr != 0) Marshal.FreeCoTaskMem(mixFormatPtr);
            if (captureClient is not null) Marshal.ReleaseComObject(captureClient);
            if (audioClient is not null) Marshal.ReleaseComObject(audioClient);
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private void ComputeFftBands(int sampleRate)
    {
        int start = _ringPos;
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _ring[(start + i) & (FftSize - 1)];
            _fftReal[i] = sample * _window[i];
            _fftImag[i] = 0.0;
        }

        FftInPlace(_fftReal, _fftImag);

        double binHz = sampleRate / (double)FftSize;
        const double minFreq = 45.0;
        const double maxFreq = 13500.0;
        double logRatio = Math.Log(maxFreq / minFreq);

        lock (_lock)
        {
            for (int b = 0; b < BandCount; b++)
            {
                double f0 = minFreq * Math.Exp(logRatio * b / BandCount);
                double f1 = minFreq * Math.Exp(logRatio * (b + 1) / BandCount);
                int bin0 = Math.Clamp((int)Math.Floor(f0 / binHz), 1, FftSize / 2 - 1);
                int bin1 = Math.Clamp((int)Math.Ceiling(f1 / binHz), bin0, FftSize / 2 - 1);

                double sumMag = 0;
                int count = 0;
                for (int k = bin0; k <= bin1; k++)
                {
                    double mag = Math.Sqrt(_fftReal[k] * _fftReal[k] + _fftImag[k] * _fftImag[k]) / (FftSize * 0.25);
                    sumMag += mag;
                    count++;
                }

                double avg = count > 0 ? sumMag / count : 0;
                // Gentle high-frequency tilt compensation + perceptual log curve
                double tilt = 1.0 + (b / (double)BandCount) * 1.4;
                double db = 20.0 * Math.Log10(avg * tilt + 1e-6);
                float normalized = (float)Math.Clamp((db + 54.0) / 48.0, 0.0, 1.0);

                // Smooth attack/release
                float prev = _bands[b];
                _bands[b] = normalized > prev
                    ? prev + (normalized - prev) * 0.62f
                    : prev + (normalized - prev) * 0.22f;
            }
        }
    }

    private static void FftInPlace(double[] real, double[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wLenR = Math.Cos(ang);
            double wLenI = Math.Sin(ang);
            int half = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                double wR = 1.0, wI = 0.0;
                for (int j = 0; j < half; j++)
                {
                    int u = i + j;
                    int v = u + half;
                    double tR = real[v] * wR - imag[v] * wI;
                    double tI = real[v] * wI + imag[v] * wR;
                    real[v] = real[u] - tR;
                    imag[v] = imag[u] - tI;
                    real[u] += tR;
                    imag[u] += tI;
                    double nextR = wR * wLenR - wI * wLenI;
                    wI = wR * wLenI + wI * wLenR;
                    wR = nextR;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        _wakeEvent.Set();
        _worker.Join(400);
        _wakeEvent.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out nint ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(uint shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, nint pFormat, nint audioSessionGuid);
        int GetBufferSize(out uint pNumBufferFrames);
        int GetStreamLatency(out long phnsLatency);
        int GetCurrentPadding(out uint pNumPaddingFrames);
        int IsFormatSupported(uint shareMode, nint pFormat, out nint ppClosestMatch);
        int GetMixFormat(out nint ppDeviceFormat);
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(nint eventHandle);
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(out nint ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        int ReleaseBuffer(uint numFramesRead);
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }
}
