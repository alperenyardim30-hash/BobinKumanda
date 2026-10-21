using System.Numerics;

namespace KumandaLab.Core.Simulation;

/// <summary>Kısmi pivotlamalı yoğun LU çözücü (karmaşık). Devrelerde düğüm sayısı birkaç yüzü geçmediği için yeterince hızlı.</summary>
internal sealed class ComplexLu
{
    private readonly int n;
    private readonly Complex[] a;
    private readonly int[] perm;

    public ComplexLu(int size, Complex[] matrix)
    {
        n = size;
        a = matrix;
        perm = new int[n];
        Factor();
    }

    public static Complex[] NewMatrix(int size) => new Complex[size * size];

    private void Factor()
    {
        for (int i = 0; i < n; i++)
        {
            perm[i] = i;
        }
        for (int k = 0; k < n; k++)
        {
            int pivot = k;
            double best = a[k * n + k].Magnitude;
            for (int i = k + 1; i < n; i++)
            {
                double m = a[i * n + k].Magnitude;
                if (m > best)
                {
                    best = m;
                    pivot = i;
                }
            }
            if (pivot != k)
            {
                for (int j = 0; j < n; j++)
                {
                    (a[k * n + j], a[pivot * n + j]) = (a[pivot * n + j], a[k * n + j]);
                }
                (perm[k], perm[pivot]) = (perm[pivot], perm[k]);
            }
            Complex diag = a[k * n + k];
            if (diag == Complex.Zero)
            {
                // gmin sayesinde olmamalı; yine de sıfıra bölmeyi önle
                diag = a[k * n + k] = new Complex(1e-15, 0);
            }
            for (int i = k + 1; i < n; i++)
            {
                Complex f = a[i * n + k];
                if (f == Complex.Zero)
                {
                    continue;
                }
                f /= diag;
                a[i * n + k] = f;
                int rowI = i * n, rowK = k * n;
                for (int j = k + 1; j < n; j++)
                {
                    Complex v = a[rowK + j];
                    if (v != Complex.Zero)
                    {
                        a[rowI + j] -= f * v;
                    }
                }
            }
        }
    }

    public Complex[] Solve(Complex[] rhs)
    {
        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = rhs[perm[i]];
        }
        for (int i = 0; i < n; i++)
        {
            Complex s = x[i];
            int row = i * n;
            for (int j = 0; j < i; j++)
            {
                s -= a[row + j] * x[j];
            }
            x[i] = s;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            Complex s = x[i];
            int row = i * n;
            for (int j = i + 1; j < n; j++)
            {
                s -= a[row + j] * x[j];
            }
            x[i] = s / a[row + i];
        }
        return x;
    }
}

internal sealed class RealLu
{
    private readonly int n;
    private readonly double[] a;
    private readonly int[] perm;

    public RealLu(int size, double[] matrix)
    {
        n = size;
        a = matrix;
        perm = new int[n];
        Factor();
    }

    private void Factor()
    {
        for (int i = 0; i < n; i++)
        {
            perm[i] = i;
        }
        for (int k = 0; k < n; k++)
        {
            int pivot = k;
            double best = Math.Abs(a[k * n + k]);
            for (int i = k + 1; i < n; i++)
            {
                double m = Math.Abs(a[i * n + k]);
                if (m > best)
                {
                    best = m;
                    pivot = i;
                }
            }
            if (pivot != k)
            {
                for (int j = 0; j < n; j++)
                {
                    (a[k * n + j], a[pivot * n + j]) = (a[pivot * n + j], a[k * n + j]);
                }
                (perm[k], perm[pivot]) = (perm[pivot], perm[k]);
            }
            double diag = a[k * n + k];
            if (diag == 0)
            {
                diag = a[k * n + k] = 1e-15;
            }
            for (int i = k + 1; i < n; i++)
            {
                double f = a[i * n + k];
                if (f == 0)
                {
                    continue;
                }
                f /= diag;
                a[i * n + k] = f;
                int rowI = i * n, rowK = k * n;
                for (int j = k + 1; j < n; j++)
                {
                    double v = a[rowK + j];
                    if (v != 0)
                    {
                        a[rowI + j] -= f * v;
                    }
                }
            }
        }
    }

    public double[] Solve(double[] rhs)
    {
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = rhs[perm[i]];
        }
        for (int i = 0; i < n; i++)
        {
            double s = x[i];
            int row = i * n;
            for (int j = 0; j < i; j++)
            {
                s -= a[row + j] * x[j];
            }
            x[i] = s;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            int row = i * n;
            for (int j = i + 1; j < n; j++)
            {
                s -= a[row + j] * x[j];
            }
            x[i] = s / a[row + i];
        }
        return x;
    }
}
