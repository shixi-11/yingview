using System.Collections;

namespace YingView.Core;

/// <summary>Ordinal natural ordering: digit runs compare by numeric value without integer conversion.</summary>
public sealed class NaturalStringComparer : IComparer<string?>, IComparer
{
    public static NaturalStringComparer Ordinal { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                while (i < x.Length && x[i] == '0') i++;
                while (j < y.Length && y[j] == '0') j++;
                var xSignificant = i;
                var ySignificant = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var xLength = i - xSignificant;
                var yLength = j - ySignificant;
                var result = xLength.CompareTo(yLength);
                if (result != 0) return result;
                for (var k = 0; k < xLength; k++)
                {
                    result = x[xSignificant + k].CompareTo(y[ySignificant + k]);
                    if (result != 0) return result;
                }
                // Numerically equal runs continue; ordinal fallback below distinguishes leading zeroes.
                continue;
            }

            var charResult = x[i].CompareTo(y[j]);
            if (charResult != 0) return charResult;
            i++;
            j++;
        }

        var naturalResult = (x.Length - i).CompareTo(y.Length - j);
        return naturalResult != 0 ? naturalResult : string.CompareOrdinal(x, y);
    }

    int IComparer.Compare(object? x, object? y) => Compare(x as string, y as string);
}
