import AppKit
import SwiftUI
import ImageIO
import UniformTypeIdentifiers
import YingViewCore

@MainActor enum SmokeTest {
    enum Failure: Error { case assertion(String) }
    static func require(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        if !condition() { throw Failure.assertion(message) }
    }
    static func awaitLoad(_ model: ViewerModel) async throws {
        for _ in 0..<500 {
            if !model.loading { return }
            try await Task.sleep(nanoseconds: 20_000_000)
        }
        throw Failure.assertion("Image decode timed out")
    }
    static func fixture(at url: URL, width: Int, height: Int) throws {
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { throw Failure.assertion("Fixture context") }
        context.setFillColor(NSColor.systemTeal.cgColor); context.fill(CGRect(x: 0, y: 0, width: Double(width), height: Double(height)))
        context.setFillColor(NSColor.systemOrange.cgColor); context.fill(CGRect(x: 0, y: 0, width: Double(width) / 3, height: Double(height) / 2))
        guard let image = context.makeImage(), let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil) else { throw Failure.assertion("Fixture destination") }
        CGImageDestinationAddImage(destination, image, nil)
        try require(CGImageDestinationFinalize(destination), "Fixture write")
    }
    static func render(_ view: NSView, to url: URL) throws {
        view.layoutSubtreeIfNeeded(); view.displayIfNeeded()
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw Failure.assertion("Native bitmap allocation") }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try require((bitmap.colorAt(x: bitmap.pixelsWide / 2, y: bitmap.pixelsHigh / 2)?.alphaComponent ?? 0) > 0.1, "Native rendering is not blank")
        guard let data = bitmap.representation(using: .png, properties: [:]) else { throw Failure.assertion("Native PNG rendering") }
        try data.write(to: url)
        let decoded = try ImageDecoder.decode(url)
        try require(decoded.width > 100 && decoded.height > 100, "Rendered dimensions")
    }
    static func run(model: ViewerModel) async {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("YingViewSmoke-\(UUID().uuidString)", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: root) }
            let output = ProcessInfo.processInfo.environment["YINGVIEW_SMOKE_OUTPUT"].map { URL(fileURLWithPath: $0, isDirectory: true) } ?? root.appendingPathComponent("rendered", isDirectory: true)
            try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
            let one = root.appendingPathComponent("frame1.png"), two = root.appendingPathComponent("frame2.png"), ten = root.appendingPathComponent("frame10.png")
            try fixture(at: one, width: 120, height: 80); try fixture(at: two, width: 80, height: 200); try fixture(at: ten, width: 320, height: 100)
            model.open(one); try await awaitLoad(model)
            try require(model.image?.width == 120, "Open and decode PNG")
            let pngSupported = ImageDecoder.extensions.contains("png")
            try require(model.files.map(\.lastPathComponent) == ["frame1.png", "frame2.png", "frame10.png"], "Natural order; actual=\(model.files.map(\.absoluteString)); PNG supported=\(pngSupported)")
            model.open(one.resolvingSymlinksInPath()); try await awaitLoad(model)
            try require(model.files.count == 3 && model.index == 0, "Equivalent file URL does not duplicate the selected image")
            let canvas = CanvasView(frame: CGRect(x: 0, y: 0, width: 800, height: 500))
            let window = NSWindow(contentRect: canvas.bounds, styleMask: [.borderless], backing: .buffered, defer: false)
            window.contentView = canvas
            canvas.setImage(model.image); canvas.fit()
            try require(canvas.imageBounds.width <= 800.01 && canvas.imageBounds.height <= 500.01 && canvas.imageBounds.width > 120, "Contain enlarges and fits")
            try render(canvas, to: output.appendingPathComponent("contain.png"))
            canvas.quarterTurns = 1; canvas.fit()
            try require(canvas.imageBounds.width <= 800.01 && canvas.imageBounds.height <= 500.01, "Rotated contain")
            try render(canvas, to: output.appendingPathComponent("rotated.png"))
            canvas.actual()
            try require(abs(canvas.scale * Double(window.backingScaleFactor) - 1) < 0.0001, "Physical pixel 100 percent")
            canvas.zoom(by: 1.2); try require(canvas.scale > 1 / Double(window.backingScaleFactor), "Zoom")
            model.move(1); try await awaitLoad(model); try require(model.current.map { ViewerLogic.sameFile($0, two) } == true && model.image?.height == 200, "Next image")
            model.rotate(); model.move(1); try await awaitLoad(model); try require(model.rotation == 0 && model.current.map { ViewerLogic.sameFile($0, ten) } == true, "Navigation resets contain rotation")
            model.move(-1); try await awaitLoad(model)
            model.compare = true
            for _ in 0..<500 { if model.secondImage != nil || model.secondError != nil { break }; try await Task.sleep(nanoseconds: 20_000_000) }
            try require(model.secondImage != nil, "Comparison decode")
            let corrupt = root.appendingPathComponent("broken.png"); try Data("not an image".utf8).write(to: corrupt)
            model.open(corrupt); try await awaitLoad(model); try require(model.errorText != nil && model.image == nil, "Corrupt file error")
            model.open(one); try await awaitLoad(model); try require(model.image != nil && model.errorText == nil, "Error recovery")
            try FileManager.default.moveItem(at: one, to: root.appendingPathComponent("moved.png"))
            model.select(model.index); try await awaitLoad(model); try require(model.errorText != nil, "Moved file error")
            let previousIndex = model.index, previousCount = model.files.count
            try require(!model.trashCurrent() && model.index == previousIndex && model.files.count == previousCount, "Failed Trash preserves navigation")
            model.refresh(); try await awaitLoad(model); try require(!model.files.contains(where: { ViewerLogic.sameFile($0, one) }), "Refresh removes missing file")
            model.open(two); try await awaitLoad(model)
            try require(model.trashCurrent(), "Trash fixture succeeds")
            try await awaitLoad(model)
            try require(!FileManager.default.fileExists(atPath: two.path) && !model.files.contains(where: { ViewerLogic.sameFile($0, two) }), "Trash removes only fixture")
            model.open(ten); try await awaitLoad(model)
            model.open(one); model.open(ten); try await awaitLoad(model)
            try require(model.current.map { ViewerLogic.sameFile($0, ten) } == true && model.image?.width == 320 && model.errorText == nil, "Stale decode cannot overwrite current image")
            let empty = root.appendingPathComponent("empty", isDirectory: true)
            try FileManager.default.createDirectory(at: empty, withIntermediateDirectories: true)
            model.open(empty); try require(model.files.isEmpty && model.image == nil, "Empty folder is recoverable")
            model.open(ten); try await awaitLoad(model)
            let saved = (model.accent, model.textSize, model.preset)
            model.beginAppearanceEdit(); model.accent = "green"; model.textSize = 18; model.preset = "light"
            model.cancelAppearance(); try require(model.accent == saved.0 && model.textSize == saved.1 && model.preset == saved.2, "Cancel restores live appearance")
            model.compare = false; model.showFilmstrip = true
            let hosting = NSHostingView(rootView: ViewerWindow(model: model))
            hosting.frame = CGRect(x: 0, y: 0, width: 1120, height: 760)
            let previewWindow = NSWindow(contentRect: hosting.frame, styleMask: [.titled], backing: .buffered, defer: false)
            previewWindow.contentView = hosting
            hosting.layoutSubtreeIfNeeded()
            try await Task.sleep(nanoseconds: 250_000_000)
            try render(hosting, to: output.appendingPathComponent("viewer.png"))
            // Isolated favorite metadata and disposable selection fixtures.
            let picksFolder = root.appendingPathComponent("picks", isDirectory: true)
            try FileManager.default.createDirectory(at: picksFolder, withIntermediateDirectories: true)
            let picks = (1...3).map { picksFolder.appendingPathComponent("pick\($0).png") }
            for url in picks { try fixture(at: url, width: 160, height: 100) }
            let suite = "YingViewSelectionSmoke-\(UUID().uuidString)"
            let storage = UserDefaults(suiteName: suite)!
            defer { storage.removePersistentDomain(forName: suite) }
            let selector = ViewerModel(defaults: storage)
            selector.open(picks[0]); try await awaitLoad(selector)
            selector.setFavorites([picks[0], picks[2]], favorite: true)
            let restored = ViewerModel(defaults: storage)
            try require(restored.favoriteIDs.count == 2, "Photographer favorites survive reload")
            storage.set("corrupt sentinel", forKey: "photoSelections.v1")
            selector.setFavorites([picks[1]], favorite: true)
            try require(storage.string(forKey: "photoSelections.v1") == "corrupt sentinel" && selector.photoStatus != nil, "Wrong-root metadata is not overwritten")
            storage.removeObject(forKey: "photoSelections.v1")
            selector.openURLs([picksFolder, picks[0], picksFolder]); try await awaitLoad(selector)
            try require(selector.files.count == 3 && selector.files.allSatisfy { (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true }, "Mixed folder drops expand only regular images and deduplicate them")
            selector.photoSelection.replace([picks[0], picks[2]])
            selector.compareSelectedPhotos(); try await awaitLoad(selector)
            for _ in 0..<500 { if selector.secondImage != nil || selector.secondError != nil { break }; try await Task.sleep(nanoseconds: 20_000_000) }
            try require(selector.compare && selector.secondURL.map { ViewerLogic.sameFile($0, picks[2]) } == true && selector.secondImage != nil, "Compare chosen non-adjacent photos")
            selector.showSelection = true
            let grid = NSHostingView(rootView: ViewerWindow(model: selector))
            grid.frame = CGRect(x: 0, y: 0, width: 850, height: 650)
            let gridWindow = NSWindow(contentRect: grid.frame, styleMask: [.titled], backing: .buffered, defer: false)
            gridWindow.contentView = grid
            try await Task.sleep(nanoseconds: 500_000_000)
            try render(grid, to: output.appendingPathComponent("photo-selection.png"))
            try FileManager.default.removeItem(at: picks[2])
            selector.refresh(); try await awaitLoad(selector)
            try require(!selector.compare && selector.comparisonURL == nil, "Refresh clears a removed comparison reference")
            selector.photoSelection.replace([picks[0], picks[1]]); selector.compareSelectedPhotos()
            try await awaitLoad(selector)
            try FileManager.default.removeItem(at: picks[0])
            selector.refresh(); try await awaitLoad(selector)
            try require(!selector.compare && selector.comparisonURL == nil, "Refresh cannot compare a photo with itself after left removal")
            let selectionSet = root.appendingPathComponent("page-fixtures", isDirectory: true)
            try FileManager.default.createDirectory(at: selectionSet, withIntermediateDirectories: true)
            for i in 1...56 { try fixture(at: selectionSet.appendingPathComponent("frame\(i).png"), width: 16, height: 16) }
            selector.open(selectionSet); try await awaitLoad(selector)
            selector.setPhotoPageSize(24); selector.photoThumbnailSize = 260
            selector.changePhotoPage(2)
            try require(selector.photosOnPage.count == 8 && selector.visiblePhotoPage == 2, "User-controlled pagination for a 56-photo set")
            let viewSettings = ViewerModel(defaults: storage)
            try require(viewSettings.photoPageSize == 24 && viewSettings.photoThumbnailSize == 260, "Photo layout preferences survive reload")
            let pair = Array(selector.files.prefix(2))
            selector.photoSelection.replace([pair[0]]); selector.compareSelectedPhotos()
            try require(selector.compare && selector.secondURL == pair[1], "One selected photo starts comparison with its neighbor")
            selector.previewPhoto(pair[0]); selector.move(1); try await awaitLoad(selector); selector.togglePhotoSelection()
            try require(selector.showSelection && selector.selectedPhotos == [pair[1]], "Preview arrow navigation returns to the current photo")
            let moveFolder = root.appendingPathComponent("move-destination", isDirectory: true)
            try FileManager.default.createDirectory(at: moveFolder, withIntermediateDirectories: true)
            let firstTarget = moveFolder.appendingPathComponent(pair[0].lastPathComponent), conflict = moveFolder.appendingPathComponent(pair[1].lastPathComponent)
            let sentinel = Data([7, 19, 23]); try sentinel.write(to: conflict)
            selector.setFavorites([pair[0]], favorite: true); selector.photoSelection.replace(pair)
            await selector.moveSelectedPhotos(to: moveFolder)
            let conflictBytes = try Data(contentsOf: conflict)
            try require(!FileManager.default.fileExists(atPath: pair[0].path) && FileManager.default.fileExists(atPath: firstTarget.path) && conflictBytes == sentinel && selector.selectedPhotos == [pair[1]], "Move keeps same-name originals and skips overwriting")
            selector.removeSelectedPhotos(); selector.clearPhotoImport()
            try require(selector.files.isEmpty && FileManager.default.fileExists(atPath: pair[1].path) && selector.favoriteIDs.contains(PhotoSelectionState.identity(firstTarget)), "Clearing and removing affect only the selection; moved favorites follow the new path")
            let report = "PASS: fixture PNG decode; natural navigation; contain/enlargement; rotation; physical 100%; zoom; compare; corruption/moved-file recovery; refresh; fixture Trash; appearance rollback; persistent photo favorites; corrupt metadata preservation; non-adjacent comparison; deleted-pair reconciliation; page preferences; single-selection comparison; preview return; safe folder move; clear import; native PNG rendering.\n"
            try report.write(to: output.appendingPathComponent("smoke-test.txt"), atomically: true, encoding: .utf8)
            print(report)
            try FileManager.default.removeItem(at: root)
            fflush(stdout); exit(EXIT_SUCCESS)
        } catch {
            try? FileManager.default.removeItem(at: root)
            fputs("FAIL: YingView smoke test: \(error)\n", stderr); fflush(stderr); exit(EXIT_FAILURE)
        }
    }
}
