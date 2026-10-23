import AppKit
import SwiftUI
import ImageIO
import UniformTypeIdentifiers
import YingViewCore

enum Copy {
    static let chinese = Bundle.main.object(forInfoDictionaryKey: "ClientLanguage") as? String == "zh-CN"
    static func t(_ zh: String, _ en: String) -> String { chinese ? zh : en }
    static var brand: String { chinese ? "映窗" : "YingView" }
}

struct DecodedImage: @unchecked Sendable {
    let frames: [CGImage]
    let delays: [Double]
    var width: Int { frames.first?.width ?? 0 }
    var height: Int { frames.first?.height ?? 0 }
}

enum ImageDecoder {
    // Bound simultaneous allocations, including lazy thumbnails and rapid navigation.
    private static let decodeSlots = DispatchSemaphore(value: 2)
    static let identifiers = (CGImageSourceCopyTypeIdentifiers() as? [String]) ?? []
    static let types = identifiers.compactMap { UTType($0) }.filter { $0.conforms(to: .image) }
    static let extensions: Set<String> = Set(types.flatMap { $0.tags[.filenameExtension] ?? [] }.map { $0.lowercased() })
    static func decode(_ url: URL, thumbnail: Bool = false) throws -> DecodedImage {
        decodeSlots.wait()
        defer { decodeSlots.signal() }
        if Task.isCancelled { throw CancellationError() }
        guard let source = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary),
              let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let w = props[kCGImagePropertyPixelWidth] as? NSNumber,
              let h = props[kCGImagePropertyPixelHeight] as? NSNumber else { throw CocoaError(.fileReadCorruptFile) }
        let pixels = w.doubleValue * h.doubleValue
        guard pixels > 0, pixels <= 100_000_000 else { throw CocoaError(.fileReadTooLarge) }
        let type = CGImageSourceGetType(source).map { $0 as String } ?? ""
        let animated = type == UTType.gif.identifier || type == "public.png" || type == "org.webmproject.webp"
        let count = thumbnail || !animated ? 1 : CGImageSourceGetCount(source)
        guard count > 0, count <= 300, thumbnail || pixels * Double(count) <= 100_000_000 else { throw CocoaError(.fileReadTooLarge) }
        var frames: [CGImage] = []; var delays: [Double] = []
        for i in 0..<count {
            if Task.isCancelled { throw CancellationError() }
            let options: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: thumbnail ? 160 : max(w.intValue, h.intValue),
                kCGImageSourceShouldCacheImmediately: true]
            guard let frame = CGImageSourceCreateThumbnailAtIndex(source, i, options as CFDictionary) else { throw CocoaError(.fileReadCorruptFile) }
            frames.append(frame)
            let p = CGImageSourceCopyPropertiesAtIndex(source, i, nil) as? [String: Any] ?? [:]
            let dictionary = (p["{GIF}"] ?? p["{PNG}"] ?? p["{WebP}"]) as? [String: Any] ?? [:]
            let delay = (dictionary["UnclampedDelayTime"] ?? dictionary["DelayTime"]) as? NSNumber
            delays.append(max(delay?.doubleValue ?? 0.1, 0.02))
        }
        return DecodedImage(frames: frames, delays: delays)
    }
    static func load(_ url: URL, thumbnail: Bool = false) async throws -> DecodedImage {
        let worker = Task.detached(priority: thumbnail ? .utility : .userInitiated) { try decode(url, thumbnail: thumbnail) }
        return try await withTaskCancellationHandler(operation: { try await worker.value }, onCancel: { worker.cancel() })
    }
}

enum CanvasAction { case fit, actual, zoomIn, zoomOut }

@MainActor final class ViewerModel: ObservableObject {
    @Published var files: [URL] = []
    @Published var index = 0
    @Published var image: DecodedImage?
    @Published var secondImage: DecodedImage?
    @Published var errorText: String?
    @Published var secondError: String?
    @Published var loading = false
    @Published var revision = 0
    @Published var commandID = 0
    @Published var command: CanvasAction = .fit
    @Published var rotation = 0
    @Published var showFilmstrip = false
    @Published var compare = false { didSet { loadComparison() } }
    @Published var accent: String
    @Published var textSize: Double
    @Published var preset: String
    @Published var isEditingAppearance = false
    private var appearanceBackup: (String, Double, String)?
    private let defaults: UserDefaults
    private var loadTask: Task<Void, Never>?
    private var comparisonTask: Task<Void, Never>?
    private var comparisonGeneration = 0
    private var folder: URL?
    private var explicitSelection = false
    var current: URL? { files.indices.contains(index) ? files[index] : nil }
    var secondURL: URL? { compare && files.count > 1 ? files[(index + 1) % files.count] : nil }
    var accentColor: Color { accent == "purple" ? .purple : accent == "green" ? .green : .blue }
    var scheme: ColorScheme? { preset == "system" ? nil : preset == "light" ? .light : .dark }
    var background: Color { preset == "graphite" ? Color(red: 0.16, green: 0.17, blue: 0.18) : preset == "dark" ? Color(red: 0.055, green: 0.065, blue: 0.085) : Color(nsColor: .windowBackgroundColor) }

    init(defaults: UserDefaults? = nil) {
        self.defaults = defaults ?? (ProcessInfo.processInfo.arguments.contains("--smoke-test") ? UserDefaults(suiteName: "YingViewSmoke-\(UUID().uuidString)")! : .standard)
        accent = self.defaults.string(forKey: "accent") ?? "blue"
        preset = self.defaults.string(forKey: "preset") ?? "dark"
        let stored = self.defaults.double(forKey: "textSize")
        textSize = stored >= 11 && stored <= 20 ? stored : 13
    }
    func perform(_ action: CanvasAction) { command = action; commandID += 1 }
    func fit() { perform(.fit) }
    func rotate() { rotation = (rotation + 1) % 4; fit() }
    func open(_ url: URL) {
        let directory = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
        folder = directory ? url : url.deletingLastPathComponent()
        explicitSelection = false
        do {
            files = try scan(folder!)
            if !directory && !files.contains(url) { files.append(url); files = ViewerLogic.naturalSort(files) }
            index = files.firstIndex(of: url) ?? 0
            select(index)
        } catch {
            files = []; index = 0; select(0)
            errorText = Copy.t("无法读取文件夹，请重新打开。", "Cannot read the folder. Try opening it again.")
        }
    }
    private func scan(_ folder: URL) throws -> [URL] {
        ViewerLogic.naturalSort(try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isRegularFileKey], options: [.skipsHiddenFiles]).filter {
            ImageDecoder.extensions.contains($0.pathExtension.lowercased()) && (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true
        })
    }
    func refresh() {
        let selected = current; let oldIndex = index
        if explicitSelection {
            files = files.filter { FileManager.default.fileExists(atPath: $0.path) }
            select(selected.flatMap { files.firstIndex(of: $0) } ?? min(oldIndex, max(files.count - 1, 0)))
        } else if let folder {
            open(folder)
            select(selected.flatMap { files.firstIndex(of: $0) } ?? min(oldIndex, max(files.count - 1, 0)))
        }
    }
    func chooseImages() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = ImageDecoder.types; panel.allowsMultipleSelection = true
        if panel.runModal() == .OK { openURLs(panel.urls) }
    }
    func openURLs(_ urls: [URL]) {
        guard let first = urls.first else { return }
        if urls.count == 1 { open(first) } else { explicitSelection = true; folder = nil; files = ViewerLogic.naturalSort(urls); select(0) }
    }
    func chooseFolder() {
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false
        if panel.runModal() == .OK, let url = panel.url { open(url) }
    }
    func select(_ position: Int) {
        loadTask?.cancel(); revision += 1
        index = files.isEmpty ? 0 : min(max(position, 0), files.count - 1)
        rotation = 0; image = nil; errorText = nil; fit(); loadComparison()
        guard let url = current else { loading = false; return }
        let expected = revision; loading = true
        loadTask = Task {
            do {
                let decoded = try await ImageDecoder.load(url)
                guard !Task.isCancelled, expected == revision else { return }
                image = decoded; loading = false
            } catch {
                guard !Task.isCancelled, expected == revision else { return }
                loading = false
                errorText = Copy.t("无法打开这张图片。文件可能已移动、损坏，或超出安全解码大小；可继续翻图或刷新。", "Cannot open this image. It may have moved, be damaged, or exceed the decoding limit. Continue browsing or refresh.")
            }
        }
    }
    func loadComparison() {
        comparisonTask?.cancel(); comparisonGeneration += 1; secondImage = nil; secondError = nil
        guard let url = secondURL else { return }
        let expected = comparisonGeneration
        comparisonTask = Task {
            do {
                let decoded = try await ImageDecoder.load(url)
                guard !Task.isCancelled, expected == comparisonGeneration else { return }
                secondImage = decoded
            } catch {
                guard !Task.isCancelled, expected == comparisonGeneration else { return }
                secondError = Copy.t("无法打开对照图片", "Cannot open the comparison image")
            }
        }
    }
    func move(_ delta: Int) { if files.indices.contains(index + delta) { select(index + delta) } }
    @discardableResult func trashCurrent() -> Bool {
        guard let url = current else { return false }
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            files.remove(at: index)
            select(ViewerLogic.deletionResult(remainingCount: files.count, deletedIndex: index).index)
            return true
        } catch { errorText = Copy.t("未能移入废纸篓。请检查文件是否仍在原位置，以及访问权限。", "Could not move this image to Trash. Check its location and access permissions."); return false }
    }
    func trashFromKeyboard() {
        guard !(NSApp.keyWindow?.firstResponder is NSTextView), NSApp.keyWindow?.attachedSheet == nil else { return }
        _ = trashCurrent()
    }
    func beginAppearanceEdit() { if appearanceBackup == nil { appearanceBackup = (accent, textSize, preset) }; isEditingAppearance = true }
    func cancelAppearance() { if let backup = appearanceBackup { accent = backup.0; textSize = backup.1; preset = backup.2 }; appearanceBackup = nil; isEditingAppearance = false }
    func saveAppearance() { defaults.set(accent, forKey: "accent"); defaults.set(textSize, forKey: "textSize"); defaults.set(preset, forKey: "preset"); appearanceBackup = nil; isEditingAppearance = false }
    func setDefaultViewer() {
        let alert = NSAlert(); alert.messageText = Copy.t("将映窗设为默认图片查看器？", "Use YingView as the default image viewer?")
        alert.informativeText = Copy.t("为本机支持的图片格式设置默认应用。macOS 可能会要求逐项确认。", "Set the default app for image formats supported on this Mac. macOS may ask you to confirm changes.")
        alert.addButton(withTitle: Copy.t("设为默认", "Set as Default")); alert.addButton(withTitle: Copy.t("取消", "Cancel"))
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        Task {
            var failures = 0
            for type in ImageDecoder.types {
                do { try await NSWorkspace.shared.setDefaultApplication(at: Bundle.main.bundleURL, toOpen: type) }
                catch { failures += 1 }
            }
            errorText = failures == 0 ? Copy.t("默认图片查看器已设置。", "Default image viewer updated.") : Copy.t("部分格式未能设置。可在 Finder 的“显示简介”中选择“打开方式”，再点“全部更改”。", "Some formats were not updated. In Finder, use Get Info → Open with → Change All.")
        }
    }
}
