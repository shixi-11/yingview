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
    @Published var showSelection = false
    @Published var photoSelection = PhotoSelectionState()
    @Published var clientMode = false
    @Published var clientName = ""
    @Published var favoritesOnly = false
    @Published var photoBusy = false
    @Published var photoStatus: String?
    @Published var photoLists: [String: [String]] = [:] { didSet { favoriteCacheKey = nil } }
    private var favoriteCacheKey: String?
    private var favoriteCache: Set<String> = []
    @Published var comparisonURL: URL?
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
    var secondURL: URL? { guard compare && files.count > 1 else { return nil }; return comparisonURL ?? files[(index + 1) % files.count] }
    var accentColor: Color { accent == "purple" ? .purple : accent == "green" ? .green : .blue }
    var scheme: ColorScheme? { preset == "system" ? nil : preset == "light" ? .light : .dark }
    var background: Color { preset == "graphite" ? Color(red: 0.16, green: 0.17, blue: 0.18) : preset == "dark" ? Color(red: 0.055, green: 0.065, blue: 0.085) : Color(nsColor: .windowBackgroundColor) }

    init(defaults: UserDefaults? = nil) {
        self.defaults = defaults ?? (ProcessInfo.processInfo.arguments.contains("--smoke-test") ? UserDefaults(suiteName: "YingViewSmoke-\(UUID().uuidString)")! : .standard)
        accent = self.defaults.string(forKey: "accent") ?? "blue"
        preset = self.defaults.string(forKey: "preset") ?? "dark"
        let stored = self.defaults.double(forKey: "textSize")
        textSize = stored >= 11 && stored <= 20 ? stored : 13
        if let data = self.defaults.dictionary(forKey: "photoSelections.v1"), let lists = data["lists"] as? [String: [String]], data["version"] as? Int == 1 { photoLists = lists }
    }
    func perform(_ action: CanvasAction) { command = action; commandID += 1 }
    func fit() { perform(.fit) }
    func rotate() { rotation = (rotation + 1) % 4; fit() }
    func open(_ url: URL) {
        guard !photoBusy else { return }
        photoSelection.replace([]); comparisonURL = nil; compare = false;
        let directory = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
        folder = directory ? url : url.deletingLastPathComponent()
        explicitSelection = false
        do {
            files = try scan(folder!)
            if !directory && !files.contains(where: { ViewerLogic.sameFile($0, url) }) { files.append(url.standardizedFileURL); files = ViewerLogic.naturalSort(files) }
            index = files.firstIndex(where: { ViewerLogic.sameFile($0, url) }) ?? 0
            select(index)
        } catch {
            files = []; index = 0; select(0)
            errorText = Copy.t("无法读取文件夹，请重新打开。", "Cannot read the folder. Try opening it again.")
        }
    }
    private func scan(_ folder: URL) throws -> [URL] {
        ViewerLogic.naturalSort(try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isRegularFileKey], options: [.skipsHiddenFiles]).filter {
            ImageDecoder.extensions.contains($0.pathExtension.lowercased()) && (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true
        }.map(\.standardizedFileURL))
    }
    func refresh() {
        guard !photoBusy else { return }
        let savedSelection = photoSelection; let oldComparison = comparisonURL; let wasComparing = compare
        let selected = current; let oldIndex = index
        if explicitSelection {
            files = files.filter { FileManager.default.fileExists(atPath: $0.path) }
            select(selected.flatMap { previous in files.firstIndex(where: { ViewerLogic.sameFile($0, previous) }) } ?? min(oldIndex, max(files.count - 1, 0)))
        } else if let folder {
            open(folder)
            select(selected.flatMap { previous in files.firstIndex(where: { ViewerLogic.sameFile($0, previous) }) } ?? min(oldIndex, max(files.count - 1, 0)))
        }
        photoSelection = savedSelection; photoSelection.retain(files)
        if let reference = oldComparison, let left = current, !ViewerLogic.sameFile(left, reference), files.contains(where: { ViewerLogic.sameFile($0, reference) }) {
            comparisonURL = reference; compare = wasComparing; loadComparison()
        } else if oldComparison != nil { comparisonURL = nil; compare = false; secondImage = nil; secondError = nil }
    }
    func chooseImages() {
        guard !photoBusy else { return }
        let panel = NSOpenPanel(); panel.allowedContentTypes = ImageDecoder.types; panel.allowsMultipleSelection = true
        if panel.runModal() == .OK { openURLs(panel.urls) }
    }
    func openURLs(_ urls: [URL]) {
        guard !photoBusy else { return }
        photoSelection.replace([]); comparisonURL = nil; compare = false;
        guard let first = urls.first else { return }
        if urls.count == 1 { open(first) } else { explicitSelection = true; folder = nil; files = ViewerLogic.naturalSort(urls); select(0) }
    }
    func chooseFolder() {
        guard !photoBusy else { return }
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false
        if panel.runModal() == .OK, let url = panel.url { open(url) }
    }
    func select(_ position: Int) {
        loadTask?.cancel(); revision += 1
        index = files.isEmpty ? 0 : min(max(position, 0), files.count - 1)
        if let reference = comparisonURL, current.map({ ViewerLogic.sameFile($0, reference) }) == true { comparisonURL = nil; compare = false }
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
        guard !clientMode, !photoBusy, let url = current else { return false }
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            files.remove(at: index)
            select(ViewerLogic.deletionResult(remainingCount: files.count, deletedIndex: index).index)
            return true
        } catch { errorText = Copy.t("未能移入废纸篓。请检查文件是否仍在原位置，以及访问权限。", "Could not move this image to Trash. Check its location and access permissions."); return false }
    }
    func trashFromKeyboard() {
        guard !clientMode, !(NSApp.keyWindow?.firstResponder is NSTextView), NSApp.keyWindow?.attachedSheet == nil else { return }
        if showSelection { trashSelection() } else { _ = trashCurrent() }
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
    var photoListKey: String { clientMode ? "client:" + clientName.trimmingCharacters(in: .whitespacesAndNewlines).precomposedStringWithCanonicalMapping : "favorites" }
    var favoriteIDs: Set<String> {
        let key = photoListKey
        if favoriteCacheKey != key { favoriteCache = Set(photoLists[key] ?? []); favoriteCacheKey = key }
        return favoriteCache
    }
    var selectionFiles: [URL] { files.filter { !favoritesOnly || favoriteIDs.contains(PhotoSelectionState.identity($0)) } }
    var selectedPhotos: [URL] { files.filter { photoSelection.selected.contains(PhotoSelectionState.identity($0)) } }
    func selectPhoto(_ url: URL, command: Bool, shift: Bool) { photoSelection.select(url, ordered: selectionFiles, toggle: command, range: shift) }
    func setFavorites(_ urls: [URL], favorite: Bool) {
        guard !photoBusy else { return }
        var lists: [String: [String]] = [:]
        if let data = defaults.dictionary(forKey: "photoSelections.v1") {
            guard data["version"] as? Int == 1, let saved = data["lists"] as? [String: [String]] else { photoStatus = Copy.t("收藏清单无法读取，原数据未被覆盖。", "The favorite list could not be read. Existing data was not overwritten."); return }
            lists = saved
        }
        var ids = Set(lists[photoListKey] ?? [])
        for url in urls { if favorite { ids.insert(PhotoSelectionState.identity(url)) } else { ids.remove(PhotoSelectionState.identity(url)) } }
        lists[photoListKey] = ids.sorted(); defaults.set(["version": 1, "lists": lists] as [String: Any], forKey: "photoSelections.v1"); photoLists = lists
        if favoritesOnly { photoSelection.retain(selectionFiles) }
    }
    func selectAllPhotos() {
        if let editor = NSApp.keyWindow?.firstResponder as? NSTextView { editor.selectAll(nil); return }
        photoSelection.replace(selectionFiles)
    }
    func compareSelectedPhotos() {
        let pair = selectedPhotos; guard pair.count == 2, !photoBusy else { return }
        comparisonURL = pair[1]; select(files.firstIndex(of: pair[0])!); compare = true; showSelection = false
    }
    func previewPhoto(_ url: URL) { guard !photoBusy, let i = files.firstIndex(of: url) else { return }; compare = false; comparisonURL = nil; select(i); showSelection = false }
    func trashSelection() {
        let targets = selectedPhotos
        guard !clientMode, !photoBusy, !targets.isEmpty, !(NSApp.keyWindow?.firstResponder is NSTextView) else { return }
        let alert = NSAlert(); alert.messageText = Copy.t("将所选 \(targets.count) 张照片移到废纸篓？", "Move \(targets.count) selected photos to Trash?")
        alert.informativeText = Copy.t("可以从系统废纸篓恢复。", "They can be restored from the system Trash.")
        alert.addButton(withTitle: Copy.t("取消", "Cancel")); alert.addButton(withTitle: Copy.t("移到废纸篓", "Move to Trash"))
        guard alert.runModal() == .alertSecondButtonReturn else { return }
        photoBusy = true
        Task {
            let result = await Task.detached { () -> ([URL], Int) in
                var moved: [URL] = []; var failed = 0
                for url in targets { do { try FileManager.default.trashItem(at: url, resultingItemURL: nil); moved.append(url) } catch { failed += 1 } }
                return (moved, failed)
            }.value
            let old = current; let oldIndex = index
            let removed = Set(result.0.map(PhotoSelectionState.identity)); files.removeAll { removed.contains(PhotoSelectionState.identity($0)) }
            photoSelection.retain(files)
            if let reference = comparisonURL, removed.contains(PhotoSelectionState.identity(reference)) { comparisonURL = nil; compare = false }
            photoBusy = false
            select(old.flatMap { url in files.firstIndex(where: { ViewerLogic.sameFile($0, url) }) } ?? min(oldIndex, max(files.count - 1, 0)))
            photoStatus = Copy.t("已移到废纸篓 \(result.0.count) 张；失败 \(result.1) 张。", "Moved \(result.0.count) to Trash; \(result.1) failed.")
        }
    }
    func exportPhotoFavorites() {
        let chosen = files.filter { favoriteIDs.contains(PhotoSelectionState.identity($0)) }; guard !chosen.isEmpty else { return }
        let panel = NSSavePanel(); panel.allowedContentTypes = [.plainText]; panel.nameFieldStringValue = Copy.t("选片清单.txt", "photo-selection.txt")
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try (chosen.map { $0.lastPathComponent.replacingOccurrences(of: "\n", with: "\\n") }.joined(separator: "\n") + "\n").write(to: url, atomically: true, encoding: .utf8); photoStatus = Copy.t("收藏文件名清单已导出。", "Favorite filenames exported.") }
        catch { photoStatus = Copy.t("未能导出清单。", "Could not export the selection.") }
    }

}
