// VoxScript.Native/Parakeet/MelSpectrogram.cs
namespace VoxScript.Native.Parakeet;

/// <summary>
/// Computes 80-dim log-mel spectrogram from 16kHz mono float32 samples.
/// Matches NeMo AudioToMelSpectrogramPreprocessor defaults for parakeet-tdt.
/// n_fft=512, hop_length=160, win_length=400, n_mels=80, f_min=0, f_max=8000.
/// </summary>
public static class MelSpectrogram
{
    private const int NFft = 512;
    private const int HopLength = 160;
    private const int WinLength = 400;
    private const int NMels = 80;
    private const double FMin = 0.0;
    private const double FMax = 8000.0;
    private const int SampleRate = 16000;

    private static readonly float[] HannWindow = BuildHannWindow(WinLength);
    private static readonly float[,] MelFilterbank = BuildMelFilterbank();

    public static float[,] Compute(float[] samples, int sampleRate = 16000,
        int nFft = NFft, int hopLength = HopLength, int nMels = NMels)
    {
        int numFrames = (samples.Length - WinLength) / HopLength + 1;
        if (numFrames <= 0) numFrames = 1;

        var melSpec = new float[NMels, numFrames];

        for (int frame = 0; frame < numFrames; frame++)
        {
            int start = frame * HopLength;
            // Extract windowed frame
            var windowed = new float[NFft];
            for (int i = 0; i < WinLength && start + i < samples.Length; i++)
                windowed[i] = samples[start + i] * HannWindow[i];

            // FFT (real)
            var spectrum = RealFft(windowed);

            // Power spectrum: |X|^2
            var power = new float[NFft / 2 + 1];
            for (int k = 0; k <= NFft / 2; k++)
                power[k] = (float)(spectrum[k].Real * spectrum[k].Real
                          + spectrum[k].Imaginary * spectrum[k].Imaginary);

            // Apply mel filterbank
            for (int m = 0; m < NMels; m++)
            {
                float sum = 0f;
                for (int k = 0; k <= NFft / 2; k++)
                    sum += MelFilterbank[m, k] * power[k];
                melSpec[m, frame] = MathF.Log(MathF.Max(sum, 1e-10f));
            }
        }
        return melSpec;
    }

    private static System.Numerics.Complex[] RealFft(float[] input)
    {
        int n = input.Length;
        var complex = input.Select(x => new System.Numerics.Complex(x, 0)).ToArray();
        Fft(complex);
        return complex;
    }

    private static void Fft(System.Numerics.Complex[] a)
    {
        int n = a.Length;
        if (n <= 1) return;

        var even = new System.Numerics.Complex[n / 2];
        var odd  = new System.Numerics.Complex[n / 2];
        for (int i = 0; i < n / 2; i++) { even[i] = a[2 * i]; odd[i] = a[2 * i + 1]; }

        Fft(even);
        Fft(odd);

        for (int k = 0; k < n / 2; k++)
        {
            var t = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * k / n) * odd[k];
            a[k]       = even[k] + t;
            a[k + n/2] = even[k] - t;
        }
    }

    private static float[] BuildHannWindow(int length)
    {
        var w = new float[length];
        for (int i = 0; i < length; i++)
            w[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1))));
        return w;
    }

    private static float[,] BuildMelFilterbank()
    {
        static double HzToMel(double hz) => 2595.0 * Math.Log10(1 + hz / 700);
        static double MelToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

        double melMin = HzToMel(FMin);
        double melMax = HzToMel(FMax);
        var melPoints = new double[NMels + 2];
        for (int i = 0; i < NMels + 2; i++)
            melPoints[i] = melMin + i * (melMax - melMin) / (NMels + 1);

        var freqPoints = melPoints.Select(MelToHz).ToArray();
        var binPoints = freqPoints.Select(f => (int)Math.Floor(f / SampleRate * NFft)).ToArray();

        var filterbank = new float[NMels, NFft / 2 + 1];
        for (int m = 1; m <= NMels; m++)
        {
            for (int k = 0; k <= NFft / 2; k++)
            {
                if (k < binPoints[m - 1] || k > binPoints[m + 1])
                    filterbank[m - 1, k] = 0f;
                else if (k <= binPoints[m])
                {
                    int denom = binPoints[m] - binPoints[m - 1];
                    filterbank[m - 1, k] = denom == 0 ? 1f
                        : (float)((k - binPoints[m - 1]) / (double)denom);
                }
                else
                {
                    int denom = binPoints[m + 1] - binPoints[m];
                    filterbank[m - 1, k] = denom == 0 ? 1f
                        : (float)((binPoints[m + 1] - k) / (double)denom);
                }
            }
        }
        return filterbank;
    }
}
