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
            let report = "PASS: fixture PNG decode; natural navigation; contain/enlargement; rotation; physical 100%; zoom; compare; corruption/moved-file recovery; refresh; fixture Trash; appearance rollback; native PNG rendering.\n"
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
