import AppKit
import SwiftUI
import YingViewCore

struct ImageCanvas: NSViewRepresentable {
    let image: DecodedImage?
    let revision: Int
    let rotation: Int
    let commandID: Int
    let command: CanvasAction
    var navigate: (Int) -> Void = { _ in }
    var delete: () -> Void = {}
    var drop: ([URL]) -> Void = { _ in }
    func makeNSView(context: Context) -> CanvasView { CanvasView() }
    func updateNSView(_ view: CanvasView, context: Context) {
        view.navigate = navigate; view.deleteImage = delete; view.dropURLs = drop
        if view.revision != revision || view.firstImage !== image?.frames.first {
            view.revision = revision; view.setImage(image)
        }
        if view.quarterTurns != rotation { view.quarterTurns = rotation; view.fit() }
        if view.commandID != commandID {
            view.commandID = commandID
            switch command { case .fit: view.fit(); case .actual: view.actual(); case .zoomIn: view.zoom(by: 1.2); case .zoomOut: view.zoom(by: 1 / 1.2) }
        }
    }
}

final class CanvasView: NSView {
    var revision = -1
    var commandID = -1
    var quarterTurns = 0
    var firstImage: CGImage? { decoded?.frames.first }
    private(set) var decoded: DecodedImage?
    private(set) var scale = 1.0
    private var fitting = true
    private var actualSize = false
    private var pan = CGPoint.zero
    private var previousMouse = CGPoint.zero
    private var frameIndex = 0
    private var timer: Timer?
    var navigate: (Int) -> Void = { _ in }
    var deleteImage: () -> Void = {}
    var dropURLs: ([URL]) -> Void = { _ in }
    override var acceptsFirstResponder: Bool { true }
    override var isOpaque: Bool { false }
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes([.fileURL])
        setAccessibilityElement(true)
        setAccessibilityRole(.image)
        setAccessibilityLabel(Copy.t("图片，可拖动和缩放", "Image, drag to pan and scroll to zoom"))
    }
    convenience init() { self.init(frame: .zero) }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    deinit { timer?.invalidate() }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil {
            DispatchQueue.main.async { [weak self] in
                guard let self, let window = self.window, !(window.firstResponder is NSTextView), window.attachedSheet == nil else { return }
                window.makeFirstResponder(self)
            }
            scheduleFrame()
        } else { timer?.invalidate(); timer = nil }
    }
    override func setFrameSize(_ newSize: NSSize) { super.setFrameSize(newSize); if fitting { updateFit() }; needsDisplay = true }
    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        if actualSize { scale = ViewerLogic.actualScale(backingScale: Double(window?.backingScaleFactor ?? 1)) }
        else if fitting { updateFit() }
        needsDisplay = true
    }
    func setImage(_ value: DecodedImage?) {
        timer?.invalidate(); decoded = value; frameIndex = 0; pan = .zero; fitting = true; actualSize = false
        updateFit(); needsDisplay = true; scheduleFrame()
    }
    func fit() { fitting = true; actualSize = false; pan = .zero; updateFit(); needsDisplay = true }
    private func updateFit() {
        guard let image = firstImage else { return }
        scale = ViewerLogic.containScale(imageWidth: Double(image.width), imageHeight: Double(image.height), viewWidth: Double(bounds.width), viewHeight: Double(bounds.height), quarterTurns: quarterTurns)
    }
    func actual() {
        fitting = false; actualSize = true; pan = .zero
        scale = ViewerLogic.actualScale(backingScale: Double(window?.backingScaleFactor ?? 1)); needsDisplay = true
    }
    func zoom(by factor: Double) {
        guard firstImage != nil else { return }
        fitting = false; actualSize = false
        let old = scale; scale = min(max(scale * factor, 0.0001), 64)
        let ratio = scale / max(old, 0.0001); pan.x *= ratio; pan.y *= ratio
        needsDisplay = true
    }
    var imageBounds: CGRect {
        guard let image = firstImage else { return .zero }
        let swapped = abs(quarterTurns % 2) == 1
        let size = CGSize(width: Double(swapped ? image.height : image.width) * scale, height: Double(swapped ? image.width : image.height) * scale)
        return CGRect(x: bounds.midX + pan.x - size.width / 2, y: bounds.midY + pan.y - size.height / 2, width: size.width, height: size.height)
    }
    override func draw(_ dirtyRect: NSRect) {
        guard let decoded, decoded.frames.indices.contains(frameIndex), let context = NSGraphicsContext.current?.cgContext else { return }
        let image = decoded.frames[frameIndex]
        context.saveGState(); context.clip(to: bounds)
        context.translateBy(x: bounds.midX + pan.x, y: bounds.midY + pan.y)
        context.rotate(by: -CGFloat(quarterTurns) * .pi / 2)
        context.scaleBy(x: scale, y: scale)
        context.interpolationQuality = .high
        context.draw(image, in: CGRect(x: -Double(image.width) / 2, y: -Double(image.height) / 2, width: Double(image.width), height: Double(image.height)))
        context.restoreGState()
    }
    private func scheduleFrame() {
        timer?.invalidate(); timer = nil
        guard window != nil, let decoded, decoded.frames.count > 1 else { return }
        let next = Timer(timeInterval: decoded.delays[frameIndex], repeats: false) { [weak self] _ in
            guard let self, let decoded = self.decoded else { return }
            self.frameIndex = (self.frameIndex + 1) % decoded.frames.count
            self.needsDisplay = true; self.scheduleFrame()
        }
        timer = next; RunLoop.main.add(next, forMode: .common)
    }
    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        if event.clickCount == 2 { fit(); return }
        previousMouse = convert(event.locationInWindow, from: nil)
    }
    override func mouseDragged(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        pan.x += point.x - previousMouse.x; pan.y += point.y - previousMouse.y
        previousMouse = point; fitting = false; needsDisplay = true
    }
    override func scrollWheel(with event: NSEvent) { zoom(by: exp(Double(event.scrollingDeltaY) * (event.hasPreciseScrollingDeltas ? 0.008 : 0.09))) }
    override func magnify(with event: NSEvent) { zoom(by: Double(1 + event.magnification)) }
    override func keyDown(with event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if flags.contains(.control) || flags.contains(.option) { super.keyDown(with: event); return }
        switch event.keyCode {
        case 123 where !flags.contains(.command): navigate(-1)
        case 124 where !flags.contains(.command): navigate(1)
        case 51, 117: deleteImage()
        case 53: fit()
        default: super.keyDown(with: event)
        }
    }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { .copy }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        guard !urls.isEmpty else { return false }; dropURLs(urls); return true
    }
}
