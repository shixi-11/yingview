import AppKit
import SwiftUI
import YingViewCore

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    var openURLs: (([URL]) -> Void)?
    private var pending: [URL] = []
    func application(_ application: NSApplication, open urls: [URL]) {
        if let openURLs { openURLs(urls) } else { pending.append(contentsOf: urls) }
    }
    func connect(_ model: ViewerModel) {
        openURLs = { urls in Task { @MainActor in model.openURLs(urls) } }
        if !pending.isEmpty { let urls = pending; pending = []; openURLs?(urls) }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
@MainActor
struct YingViewApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model = ViewerModel()
    @State private var started = false
    var body: some Scene {
        WindowGroup(Copy.brand) {
            ViewerWindow(model: model)
                .frame(minWidth: 850, minHeight: 480)
                .onAppear {
                    delegate.connect(model)
                    guard !started else { return }; started = true
                    if ProcessInfo.processInfo.arguments.contains("--smoke-test") { Task { await SmokeTest.run(model: model) } }
                    else if let path = ProcessInfo.processInfo.arguments.dropFirst().first(where: { !$0.hasPrefix("-") }) { model.open(URL(fileURLWithPath: path)) }
                    DispatchQueue.main.async { localizeMenus() }
                }
                .onOpenURL { model.open($0) }
        }
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button(Copy.t("关于映窗", "About YingView")) { NSApp.orderFrontStandardAboutPanel(options: [.applicationName: Copy.brand]) }
            }
            CommandGroup(replacing: .appSettings) {
                Button(Copy.t("外观…", "Appearance…")) { model.beginAppearanceEdit() }.keyboardShortcut(",")
            }
            CommandGroup(replacing: .newItem) {
                Button(Copy.t("打开图片…", "Open Images…")) { model.chooseImages() }.keyboardShortcut("o")
                Button(Copy.t("打开文件夹…", "Open Folder…")) { model.chooseFolder() }.keyboardShortcut("o", modifiers: [.command, .shift])
                Button(Copy.t("刷新", "Refresh")) { model.refresh() }.keyboardShortcut("r")
                Divider()
                Button(Copy.t("设为默认图片查看器…", "Set as Default Image Viewer…")) { model.setDefaultViewer() }
            }
            CommandMenu(Copy.t("图片", "Image")) {
                Button(Copy.t("上一张", "Previous")) { model.move(-1) }.disabled(model.index == 0)
                Button(Copy.t("下一张", "Next")) { model.move(1) }.disabled(model.index + 1 >= model.files.count)
                Divider()
                Button(Copy.t("完整显示", "Fit to Window")) { model.fit() }.keyboardShortcut("0")
                Button(Copy.t("实际大小", "Actual Size")) { model.perform(.actual) }.keyboardShortcut("1")
                Button(Copy.t("放大", "Zoom In")) { model.perform(.zoomIn) }.keyboardShortcut("+")
                Button(Copy.t("缩小", "Zoom Out")) { model.perform(.zoomOut) }.keyboardShortcut("-")
                Button(Copy.t("旋转", "Rotate")) { model.rotate() }.keyboardShortcut("r", modifiers: [.command, .shift])
                Button(Copy.t("显示/隐藏缩略图", "Toggle Thumbnails")) { model.showFilmstrip.toggle() }.keyboardShortcut("t")
                Button(Copy.t("双图对照", "Compare Two Images")) { model.compare.toggle() }.keyboardShortcut("d")
                Divider()
                Button(Copy.t("移到废纸篓", "Move to Trash")) { model.trashFromKeyboard() }.keyboardShortcut(.delete, modifiers: [.command])
            }
        }
    }
}

@MainActor private func localizeMenus() {
    let pairs: [(String, String)] = [("File", "文件"), ("Edit", "编辑"), ("View", "显示"), ("Window", "窗口"), ("Help", "帮助"), ("Services", "服务"), ("Undo", "撤销"), ("Redo", "重做"), ("Cut", "剪切"), ("Copy", "复制"), ("Paste", "粘贴"), ("Select All", "全选"), ("Close Window", "关闭窗口"), ("Minimize", "最小化"), ("Zoom", "缩放"), ("Bring All to Front", "前置全部窗口"), ("Enter Full Screen", "进入全屏"), ("Exit Full Screen", "退出全屏"), ("Hide Others", "隐藏其他"), ("Show All", "显示全部")]
    func translate(_ menu: NSMenu) {
        for item in menu.items {
            if let pair = pairs.first(where: { $0.0 == item.title || $0.1 == item.title }) { item.title = Copy.chinese ? pair.1 : pair.0 }
            if item.action == #selector(NSApplication.terminate(_:)) { item.title = Copy.t("退出映窗", "Quit YingView") }
            if item.action == #selector(NSApplication.hide(_:)) { item.title = Copy.t("隐藏映窗", "Hide YingView") }
            if let submenu = item.submenu { translate(submenu) }
        }
    }
    if let menu = NSApp.mainMenu { menu.items.first?.title = Copy.brand; translate(menu) }
}

struct ViewerWindow: View {
    @ObservedObject var model: ViewerModel
    var body: some View {
        VStack(spacing: 0) {
            toolbar
            HStack(spacing: 1) {
                canvas(image: model.image, url: model.current, message: model.errorText, loading: model.loading)
                if model.compare, let second = model.secondURL { canvas(image: model.secondImage, url: second, message: model.secondError, loading: model.secondImage == nil && model.secondError == nil) }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
            if model.showFilmstrip { filmstrip }
            HStack {
                Text(model.files.isEmpty ? Copy.t("打开图片或文件夹", "Open an image or folder") : "\(model.index + 1) / \(model.files.count)")
                Spacer()
                if let image = model.image { Text("\(image.width) × \(image.height)") }
                Text(Copy.t("滚轮缩放 · 拖动平移 · 双击完整显示", "Scroll to zoom · Drag to pan · Double-click to fit"))
            }.font(.system(size: max(model.textSize - 2, 11))).foregroundStyle(.secondary).padding(.horizontal, 16).padding(.vertical, 8)
        }
        .font(.system(size: model.textSize)).tint(model.accentColor).background(model.background).preferredColorScheme(model.scheme)
        .sheet(isPresented: $model.isEditingAppearance, onDismiss: { model.cancelAppearance() }) { AppearanceEditor(model: model) }
        .background(WindowTitle(title: model.current.map { "\($0.lastPathComponent) — \(Copy.brand)" } ?? Copy.brand))
    }
    private var toolbar: some View {
        HStack(spacing: 14) {
            Button { model.chooseImages() } label: { Label(Copy.t("打开", "Open"), systemImage: "folder") }
            Divider().frame(height: 20)
            control("chevron.left", Copy.t("上一张", "Previous")) { model.move(-1) }.disabled(model.index == 0)
            control("chevron.right", Copy.t("下一张", "Next")) { model.move(1) }.disabled(model.index + 1 >= model.files.count)
            Text(model.current?.lastPathComponent ?? Copy.brand).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
            control("arrow.up.left.and.arrow.down.right", Copy.t("完整显示", "Fit to Window")) { model.fit() }
            Button("100%") { model.perform(.actual) }.help(Copy.t("实际像素大小", "Actual pixel size"))
            control("minus.magnifyingglass", Copy.t("缩小", "Zoom Out")) { model.perform(.zoomOut) }
            control("plus.magnifyingglass", Copy.t("放大", "Zoom In")) { model.perform(.zoomIn) }
            control("rotate.right", Copy.t("旋转", "Rotate")) { model.rotate() }
            control("rectangle.bottomthird.inset.filled", Copy.t("缩略图", "Thumbnails")) { model.showFilmstrip.toggle() }
            control("rectangle.split.2x1", Copy.t("双图对照", "Compare")) { model.compare.toggle() }
            control("arrow.clockwise", Copy.t("刷新", "Refresh")) { model.refresh() }
            control("slider.horizontal.3", Copy.t("外观", "Appearance")) { model.beginAppearanceEdit() }
            control("trash", Copy.t("移到废纸篓", "Move to Trash")) { _ = model.trashCurrent() }.disabled(model.current == nil)
        }.buttonStyle(.borderless).padding(.horizontal, 16).padding(.vertical, 12).background(.bar)
    }
    private func control(_ symbol: String, _ label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Image(systemName: symbol) }.help(label).accessibilityLabel(label)
    }
    private func canvas(image: DecodedImage?, url: URL?, message: String?, loading: Bool) -> some View {
        ZStack {
            ImageCanvas(image: image, revision: model.revision, rotation: model.rotation, commandID: model.commandID, command: model.command, navigate: model.move, delete: model.trashFromKeyboard, drop: model.openURLs)
            if loading { ProgressView().allowsHitTesting(false) }
            if let message {
                VStack(spacing: 14) {
                    Image(systemName: "exclamationmark.triangle").font(.system(size: 32)).foregroundStyle(.secondary)
                    Text(message).multilineTextAlignment(.center).frame(maxWidth: 420)
                    Button(Copy.t("刷新", "Refresh")) { model.refresh() }
                }.padding(24)
            } else if url == nil {
                VStack(spacing: 18) {
                    Image(systemName: "photo.on.rectangle.angled").font(.system(size: 58, weight: .ultraLight)).foregroundStyle(model.accentColor)
                    Text(Copy.t("让每张图片，完整入眼。", "Every image, beautifully in view.")).font(.system(size: model.textSize + 10, weight: .light))
                    Text(Copy.t("拖入图片或文件夹，也可以从这里打开。", "Drop images or a folder, or open them below.")).foregroundStyle(.secondary)
                    HStack(spacing: 16) { Button(Copy.t("打开图片", "Open Images")) { model.chooseImages() }; Button(Copy.t("打开文件夹", "Open Folder")) { model.chooseFolder() } }
                }.padding(30)
            }
            if model.compare, let url { VStack { Spacer(); Text(url.lastPathComponent).lineLimit(1).padding(6).background(.ultraThinMaterial, in: Capsule()).padding(8) }.allowsHitTesting(false) }
        }.frame(maxWidth: .infinity, maxHeight: .infinity)
    }
    private var filmstrip: some View {
        ScrollViewReader { reader in
            ScrollView(.horizontal) {
                LazyHStack(spacing: 6) {
                    ForEach(Array(model.files.enumerated()), id: \.element) { i, url in
                        Button { model.select(i) } label: {
                            Thumbnail(url: url).frame(width: 64, height: 46).padding(3)
                                .background(i == model.index ? model.accentColor.opacity(0.22) : .clear, in: RoundedRectangle(cornerRadius: 5))
                                .overlay(RoundedRectangle(cornerRadius: 5).stroke(i == model.index ? model.accentColor : .clear, lineWidth: 2))
                        }.buttonStyle(.plain).help(url.lastPathComponent).accessibilityLabel(url.lastPathComponent).id(i)
                    }
                }.padding(.horizontal, 12).padding(.vertical, 4)
            }.frame(height: 62).background(.bar)
                .onChange(of: model.index) { index in reader.scrollTo(index, anchor: .center) }
                .onAppear { reader.scrollTo(model.index, anchor: .center) }
        }
    }
}

struct Thumbnail: View {
    let url: URL
    @State private var image: CGImage?
    var body: some View {
        Group {
            if let image { Image(decorative: image, scale: 1).resizable().scaledToFit() }
            else { Image(systemName: "photo").foregroundStyle(.secondary) }
        }.task(id: url) {
            let result = try? await ImageDecoder.load(url, thumbnail: true)
            if !Task.isCancelled { image = result?.frames.first }
        }
    }
}

struct AppearanceEditor: View {
    @ObservedObject var model: ViewerModel
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text(Copy.t("外观", "Appearance")).font(.title2.bold())
            Picker(Copy.t("风格", "Style"), selection: $model.preset) {
                Text(Copy.t("深色", "Dark")).tag("dark"); Text(Copy.t("浅色", "Light")).tag("light")
                Text(Copy.t("系统", "System")).tag("system"); Text(Copy.t("石墨", "Graphite")).tag("graphite")
            }.pickerStyle(.segmented)
            Picker(Copy.t("强调色", "Accent"), selection: $model.accent) {
                Text(Copy.t("蓝色", "Blue")).tag("blue"); Text(Copy.t("紫色", "Purple")).tag("purple"); Text(Copy.t("绿色", "Green")).tag("green")
            }.pickerStyle(.segmented)
            HStack { Text(Copy.t("文字大小", "Text size")); Slider(value: $model.textSize, in: 11...20, step: 1); Text("\(Int(model.textSize)) pt") }
            Text(Copy.t("每张图片，都有自己的光。", "Every image has its own light.")).font(.system(size: model.textSize)).foregroundStyle(model.accentColor).padding(20).frame(maxWidth: .infinity).background(model.background, in: RoundedRectangle(cornerRadius: 10))
            HStack { Spacer(); Button(Copy.t("取消", "Cancel")) { model.cancelAppearance() }.keyboardShortcut(.cancelAction); Button(Copy.t("应用", "Apply")) { model.saveAppearance() }.keyboardShortcut(.defaultAction) }
        }.padding(24).frame(width: 460).tint(model.accentColor).preferredColorScheme(model.scheme)
    }
}

private struct WindowTitle: NSViewRepresentable {
    let title: String
    func makeNSView(context: Context) -> NSView { NSView() }
    func updateNSView(_ view: NSView, context: Context) { DispatchQueue.main.async { view.window?.title = title } }
}
