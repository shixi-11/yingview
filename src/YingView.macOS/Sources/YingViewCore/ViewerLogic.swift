import Foundation

public enum ViewerLogic {
    /// FileManager enumeration and file-open events can use different URL bases
    /// or /var versus /private/var spellings for the same macOS file.
    public static func sameFile(_ lhs: URL, _ rhs: URL) -> Bool {
        lhs.standardizedFileURL.resolvingSymlinksInPath().path == rhs.standardizedFileURL.resolvingSymlinksInPath().path
    }

    public static func containScale(imageWidth: Double, imageHeight: Double, viewWidth: Double, viewHeight: Double, quarterTurns: Int = 0) -> Double {
        guard imageWidth.isFinite, imageHeight.isFinite, viewWidth.isFinite, viewHeight.isFinite,
              imageWidth > 0, imageHeight > 0, viewWidth > 0, viewHeight > 0 else { return 1 }
        let swapped = abs(quarterTurns % 2) == 1
        return min(viewWidth / (swapped ? imageHeight : imageWidth), viewHeight / (swapped ? imageWidth : imageHeight))
    }

    /// Points per source pixel: one source pixel maps to one physical display pixel.
    public static func actualScale(backingScale: Double) -> Double {
        backingScale.isFinite && backingScale > 0 ? 1 / backingScale : 1
    }

    public static func naturalLess(_ lhs: String, _ rhs: String) -> Bool {
        lhs.localizedStandardCompare(rhs) == .orderedAscending
    }

    public static func naturalSort(_ urls: [URL]) -> [URL] {
        urls.sorted { naturalLess($0.lastPathComponent, $1.lastPathComponent) }
    }

    public static func deletionResult(remainingCount: Int, deletedIndex: Int) -> (index: Int, isEmpty: Bool) {
        guard remainingCount > 0 else { return (0, true) }
        return (min(max(deletedIndex, 0), remainingCount - 1), false)
    }
}
