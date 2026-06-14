using OxyPlot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace Resonalyze.Dsp
{
    public class CalibrationFile
    {
        List<DataPoint> Calibration = new List<DataPoint> { };

        public CalibrationFile(string file)
        {
            Calibration.Clear();

            if (!System.IO.File.Exists(file))
            {
                // my microphone
                /*
                string cal = "0,30207 914,29934 1782,29700 2475,29598 3145,29506 3692,29444 4490,29356 5112,29329 5606,29334 6042,29338 6399,29343 6686,29338 7021,29338 7334,29338 7603,29338 7894,29334 8256,29334 8622,29329 8900,29329 9279,29316 9698,29312 10148,29312 10558,29312 10959,29312 11356,29307 11744,29303 12101,29303 12630,29303 13023,29303 13420,29299 13750,29294 14143,29299 14690,29316 15254,29351 15717,29373 16268,29382 16846,29409 17552,29435 18240,29457 18866,29488 19527,29519 20175,29545 20731,29567 21291,29585 21900,29603 22446,29620 23183,29669 23933,29704 24607,29762 25427,29836 26098,29903 26733,29942 27390,29942 27985,29911 28638,29850 29383,29775 30080,29709 30781,29629 31381,29585 32007,29537 32703,29299 33361,28959 34057,28624 34648,28337 35354,28002 35967,27707 36544,27433 37135,27235 37722,27076 38357,26966 39005,26944 39684,26961 40346,27116 40998,27402 41492,27623 41880,27874 42145,28156";
                List<string> pairs = new List<string>();
                string[] rawTexPoints = cal.Split(' ');
                foreach (var rtp in rawTexPoints)
                {
                    string[] pair = rtp.Split(',');
                    int x = int.Parse(pair[0]);
                    int y = 29700 - int.Parse(pair[1]);

                    double frequence = Math.Round(DataHelper.Log10ToFrequence(x / 42145.0f, 20, 20000), 2);
                    double db = Math.Round(y / 1000.0f, 1);

                    pairs.Add(frequence.ToString(CultureInfo.InvariantCulture) + '\t' + db.ToString(CultureInfo.InvariantCulture));
                    Calibration.Add(new DataPoint(frequence, DataHelper.PdBtoADC(db)));
                }
                */
                return;
            }

            string[] lines = System.IO.File.ReadAllLines(file);

            foreach (string line in lines)
            {
                string l = line.Trim();
                l = l.Replace('\t', ' ');
                string[] words = l.Split(' ');
                if (words.Length == 2)
                {
                    double f = 0, db = 0;

                    bool valid =
                        double.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out f) &&
                        double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out db);
                    if (valid)
                    {
                        Calibration.Add(new DataPoint(f, DataHelper.PdBtoADC(db)));
                    }
                }
            }

            Calibration.Sort((l, r) => { return l.X > r.X ? 1 : l.X < r.X ? -1 : 0; });
        }

        public double dBCorrection(double frequence, double smothOctaves = 1.0 / 2.0)
        {
            if (Calibration.Count < 2)
            {
                return 0;
            }

            double a = 2.0;
            double fk = Math.Pow(2.0, smothOctaves * 0.5);
            double halfDeltaFrequence = frequence * (fk - 1);

            int BinarySearchX(double searchedX)
            {
                if (searchedX <= Calibration[0].X)
                    return 0;
                if (searchedX >= Calibration[^1].X)
                    return Calibration.Count - 1;

                int left = 0;
                int right = Calibration.Count - 1;

                while (left <= right)
                {
                    var middle = (left + right) / 2;

                    if (searchedX >= Calibration[middle].X && searchedX < Calibration[middle + 1].X)
                    {
                        return middle;
                    }
                    else if (searchedX < Calibration[middle].X)
                    {
                        right = middle - 1;
                    }
                    else
                    {
                        left = middle + 1;
                    }
                }
                return -1;
            }

            double LanczosKernel(double x)
            {
                if (Math.Abs(x) < 0.00001)
                {
                    return 1.0f;
                }
                if (Math.Abs(x) <= a)
                {
                    return (a * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / a)) / (Math.PI * Math.PI * x * x);
                }
                return 0.0f;
            }

            int corner = BinarySearchX(frequence);

            if (corner < 1)
            {
                double dF = (Calibration[corner + 1].X - frequence) / (Calibration[corner + 1].X - Calibration[corner].X);
                return DataHelper.ADCtoPdB(Calibration[corner].Y * dF + Calibration[corner + 1].Y * (1.0 - dF));
            }

            if (corner >= a)
                halfDeltaFrequence = Math.Max(halfDeltaFrequence, Calibration[corner].X - Calibration[corner - (int)a].X);

            double weightAcc = 0;
            double resAcc = 0;

            void acc(int step, int fInd)
            {
                while ((step < 0 ? fInd > 0 : fInd < Calibration.Count - 1) && (Math.Abs(Calibration[fInd].X - frequence) < halfDeltaFrequence || Math.Abs(fInd - corner) < 3))
                {
                    DataPoint samplePoint = Calibration[fInd];
                    double weight = LanczosKernel((frequence - samplePoint.X) / halfDeltaFrequence * a);
                    resAcc += samplePoint.Y * weight;
                    weightAcc += weight;
                    fInd += step;
                }
            }

            acc(-1, corner);
            acc(1, corner + 1);

            return weightAcc > 0 ? DataHelper.ADCtoPdB(resAcc / weightAcc) : 0;
        }
    }
}
