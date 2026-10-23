using System.Windows.Media.Imaging;

namespace YingView.Imaging;

/// <summary>A decoded image and, where applicable, its animation timeline.</summary>
public sealed record ImageDocument(
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<int> FrameDelaysMs,
    int Width,
    int Height,
    string Format,
    long EstimatedBytes,
    string? Details = null,
    string? FilePath = null,
    int LoopCount = 0);
