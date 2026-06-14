using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Resonalyze
{
    public enum Chanels : byte
    {
        Mono = 0,
        Left = 1,
        Right = 2,
        Stereo = 3,
        Count = 4
    }

    public sealed class ExponentialSineSweep : IDisposable
    {
        private bool disposed;
        public float[] SweepData
        {
            get; private set;
        } = Array.Empty<float>();

        public float[] InverseFiltere
        {
            get; private set;
        } = Array.Empty<float>();

        public float[] Frequence
        {
            get; private set;
        } = Array.Empty<float>();

        public byte[][] SweepByteData
        {
            get; private set;
        } = Array.Empty<byte[]>();

        public int SampleRate
        {
            get; private set;
        }

        /// <summary>
        /// Number of sweep samples
        /// </summary>
        public int SweepSamples
        {
            get; private set;
        }

        public int ChanelsCount
        {
            get; private set;
        }

        public int BitsPerSample
        {
            get; private set;
        }

        public int Octaves
        {
            get; private set;
        }

        public double DesireDuration
        {
            get; private set;
        }

        public MemoryStream[] memoryStream
        {
            get; private set;
        } = Array.Empty<MemoryStream>();

        public RawSourceWaveStream[] rawSourceWaveStream
        {
            get; private set;
        } = Array.Empty<RawSourceWaveStream>();

        public double CalcDuration(double desireDuration)
        {
            double P2 = Math.Pow(2.0, Octaves);
            double LogP2 = Math.Log(P2);

            double targetLen = SampleRate * desireDuration;

            double constFactor = (Math.PI / P2) / LogP2;

            double M = Math.Max(1, Math.Round((constFactor * targetLen) / (Math.PI * 2)));

            double L = (M * Math.PI * 2) / constFactor;

            return Math.Max(1, (int)Math.Round(L)) / (double)SampleRate;
        }

        public void FillData(int octaves, double desireDuration, int bitsPerSample = 24, int sampleRate = 44100)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExponentialSineSweep));
            }
            if (bitsPerSample != 16 &&
                bitsPerSample != 24)
            {
                throw new Exception("Unsupported");
            }

            Octaves = octaves;
            BitsPerSample = bitsPerSample;
            SampleRate = sampleRate;
            DesireDuration = desireDuration;

            double P2 = Math.Pow(2, octaves);
            double LogP2 = Math.Log(P2);

            double targetLen = sampleRate * desireDuration;

            double constFactor = (Math.PI / P2) / LogP2;

            double M = Math.Max(1, Math.Round((constFactor * targetLen) / (Math.PI * 2)));

            double L = (M * Math.PI * 2) / constFactor;

            int N = Math.Max(1, (int)Math.Round(L));

            SweepSamples = N;

            SweepData = new float[N];
            InverseFiltere = new float[N];
            Frequence = new float[N];

            double ocraveLen = N / (double)octaves;

            for (int i = 0; i < N; i++)
            {
                double logPow = Math.Pow(Math.E, (double)i / N * LogP2);
                Frequence[i] = (float)((sampleRate / 2 / P2) * (L / N) * logPow);

                SweepData[i] = (float)Math.Sin(constFactor * L * logPow) * (float)Math.Min(i / ocraveLen, 1.0);
            }

            for (int i = 0; i < N; i++)
            {
                InverseFiltere[i] = (float)(SweepData[N - i - 1] *
                    Math.Pow(Math.Pow(2, octaves / (double)N), -i) *
                    (octaves * Math.Log(2) / (1 - Math.Pow(2, -octaves))));
            }

            DisposeStreams();
            SweepByteData = new byte[(int)Chanels.Count][];
            memoryStream = new MemoryStream[(int)Chanels.Count];
            rawSourceWaveStream = new RawSourceWaveStream[(int)Chanels.Count];

            for (int c = 0; c < (int)Chanels.Count; c++)
            {
                ChanelsCount = (Chanels)c == Chanels.Mono ? 1 : 2;
                int bytesPerSample = bitsPerSample / 8;
                int maxVal = Int32.MaxValue >> (32 - bitsPerSample);

                SweepByteData[c] = new byte[SweepSamples * bytesPerSample * ChanelsCount];

                for (int n = 0; n < SweepSamples; n++)
                {
                    int sample = (int)(SweepData[n] * maxVal);

                    sample *= (int)Math.Pow(256, (4 - bytesPerSample));

                    var bytes = BitConverter.GetBytes(sample);

                    if ((Chanels)c == Chanels.Mono)
                    {
                        for (int b = 0; b < bytesPerSample; b++)
                        {
                            int byteOffset = n * bytesPerSample;
                            SweepByteData[c][byteOffset + b] = bytes[b + (4 - bytesPerSample)];
                        }
                    }
                    else
                    {
                        for (int b = 0; b < bytesPerSample; b++)
                        {
                            int byteOffset = n * bytesPerSample * ChanelsCount;

                            if ((Chanels)c == Chanels.Left || (Chanels)c == Chanels.Stereo)
                            {
                                SweepByteData[c][byteOffset + b] = bytes[b + (4 - bytesPerSample)];
                            }
                            if ((Chanels)c == Chanels.Right || (Chanels)c == Chanels.Stereo)
                            {
                                SweepByteData[c][byteOffset + bytesPerSample + b] = bytes[b + (4 - bytesPerSample)];
                            }
                        }
                    }
                }

                memoryStream[c] = new MemoryStream(SweepByteData[c]);
                rawSourceWaveStream[c] = new RawSourceWaveStream(memoryStream[c], new WaveFormat(SampleRate, BitsPerSample, ChanelsCount));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DisposeStreams();
            GC.SuppressFinalize(this);
        }

        private void DisposeStreams()
        {
            if(memoryStream != null)
            {
                foreach(var ms in memoryStream)
                {
                    ms?.Dispose();
                }
            }
            if (rawSourceWaveStream != null)
            {
                foreach (var rsws in rawSourceWaveStream)
                {
                    rsws?.Dispose();
                }
            }
        }
    }
}
