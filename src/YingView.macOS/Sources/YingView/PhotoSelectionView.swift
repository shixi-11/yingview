import AppKit
import UniformTypeIdentifiers
import SwiftUI
import YingViewCore

struct PhotoSelectionView: View {
    @ObservedObject var model: ViewerModel
    @Environment(\.displayScale) private var displayScale
    private var columns: [GridItem] { [GridItem(.adaptive(minimum: model.photoThumbnailSize, maximum: model.photoThumbnailSize * 1.15), spacing: 12)] }
    private var thumbnailPixels: Int { Int(ceil(model.photoThumbnailSize * displayScale / 64) * 64) }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(Copy.t("选片", "Photo selection")).font(.title2.bold())
                Spacer()
                Button(Copy.t("打开文件夹…", "Open folder…")) { model.chooseFolder() }
                Button(Copy.t("清空导入", "Clear imported")) { model.clearPhotoImport() }.disabled(model.files.isEmpty)
                Toggle(Copy.t("只看收藏", "Favorites only"), isOn: $model.favoritesOnly)
            }
            if !model.selectionLocation.isEmpty { Text(model.selectionLocation).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle).help(model.selectionLocation) }
            HStack {
                Button(Copy.t("全选", "Select all")) { model.selectAllPhotos() }.keyboardShortcut("a", modifiers: .command)
                Button(Copy.t("取消选中", "Deselect all")) { model.photoSelection.replace([]) }
                Button(Copy.t("收藏所选", "Favorite")) { model.setFavorites(model.selectedPhotos, favorite: true) }.disabled(model.selectedPhotos.isEmpty)
                Button(Copy.t("取消收藏", "Unfavorite")) { model.setFavorites(model.selectedPhotos, favorite: false) }.disabled(model.selectedPhotos.isEmpty)
                Spacer()
            }.controlSize(.small)
            HStack {
                Button(Copy.t("查看原图", "View original")) {
                    if let photo = model.selectedPhotos.first { model.previewPhoto(photo) }
                }.disabled(model.selectedPhotos.count != 1)
                Button(Copy.t("对比两张", "Compare two")) { model.compareSelectedPhotos() }.disabled(model.files.isEmpty)
                Button(Copy.t("移到废纸篓", "Move to Trash")) { model.trashSelection() }.disabled(model.selectedPhotos.isEmpty)
                Spacer()
                Button(Copy.t("导出收藏…", "Export favorites…")) { model.exportPhotoFavorites() }.disabled(!model.files.contains { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) })
            }.controlSize(.small)
            ScrollView {
                LazyVGrid(columns: columns, spacing: 12) {
                    ForEach(model.photosOnPage, id: \.self) { url in
                        VStack(alignment: .leading, spacing: 7) {
                            VStack(spacing: 6) {
                                Thumbnail(url: url, thumbnailPixels: thumbnailPixels).frame(height: model.photoThumbnailSize * 0.7)
                                Text(url.lastPathComponent).font(.caption).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .contentShape(Rectangle())
                            .onTapGesture(count: 2) { model.previewPhoto(url) }
                            .onTapGesture { let modifiers = NSApp.currentEvent?.modifierFlags ?? []; model.selectPhoto(url, command: modifiers.contains(.command), shift: modifiers.contains(.shift)) }
                            Toggle(Copy.t("收藏", "Favorite"), isOn: Binding(get: { model.favoriteIDs.contains(PhotoSelectionState.identity(url)) }, set: { model.setFavorites([url], favorite: $0) })).toggleStyle(.checkbox)
                        }
                        .padding(8).background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 8))
                        .contextMenu {
                            Button(Copy.t("查看原图", "View original")) { model.previewPhoto(url) }
                            Button(Copy.t("将此图移到废纸篓…", "Move this photo to Trash…")) { model.trashPhoto(url) }
                            Divider()
                            Button(Copy.t("移出本次选片", "Remove from this selection")) { model.selectForContext(url); model.removeSelectedPhotos() }
                            Button(Copy.t("移动到文件夹…", "Move to folder…")) { model.selectForContext(url); model.choosePhotoMoveFolder() }
                        }
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(model.photoSelection.selected.contains(PhotoSelectionState.identity(url)) ? model.accentColor : .clear, lineWidth: 2))
                    }
                }.padding(3)
            }.id(model.visiblePhotoPage).onDeleteCommand { model.trashSelection() }
                .overlay { if model.selectionFiles.isEmpty { Text(model.files.isEmpty ? Copy.t("打开或拖入文件夹，开始选片", "Open or drop a folder to start selecting photos") : Copy.t("还没有收藏照片", "No favorites yet")).foregroundStyle(.secondary).allowsHitTesting(false) } }
            HStack {
                Button(Copy.t("← 上一页", "← Previous page")) { model.changePhotoPage(-1) }.disabled(model.visiblePhotoPage == 0)
                Text(model.selectionFiles.isEmpty ? "0 / 0" : "\(model.visiblePhotoPage + 1) / \(model.photoPageCount)").monospacedDigit()
                Button(Copy.t("下一页 →", "Next page →")) { model.changePhotoPage(1) }.disabled(model.visiblePhotoPage + 1 >= model.photoPageCount)
                Picker(Copy.t("每页", "Per page"), selection: Binding(get: { model.photoPageSize }, set: { model.setPhotoPageSize($0) })) {
                    ForEach(ViewerModel.photoPageSizes, id: \.self) { Text("\($0)").tag($0) }
                }.frame(width: 116)
                Slider(value: $model.photoThumbnailSize, in: 120...320) { Text(Copy.t("缩略图大小", "Thumbnail size")) }.frame(maxWidth: 250)
                Spacer()
            }.controlSize(.small)
            Text(Copy.t("已选 \(model.selectedPhotos.count) 张 · 收藏 \(model.files.filter { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) }.count) 张", "\(model.selectedPhotos.count) selected · \(model.files.filter { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) }.count) favorites")).font(.caption)
            Text(model.photoStatus ?? Copy.t("⌘ 多选 · Shift 连选 · 双击大图；收藏不改动原片。", "⌘ multi-select · Shift range · Double-click to preview. Favorites do not change originals.")).font(.caption).foregroundStyle(.secondary)
        }
        .padding(16).disabled(model.photoBusy)
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            let urls = providers.compactMap { provider -> NSItemProvider? in provider.hasItemConformingToTypeIdentifier("public.file-url") ? provider : nil }
            guard !urls.isEmpty else { return false }
            // Process all providers together to keep the selected scope deterministic.
            Task { @MainActor in
                var loaded: [URL] = []
                for provider in urls {
                    let data: Data? = await withCheckedContinuation { continuation in
                        provider.loadDataRepresentation(forTypeIdentifier: "public.file-url") { data, _ in continuation.resume(returning: data) }
                    }
                    if let data, let url = URL(dataRepresentation: data, relativeTo: nil) { loaded.append(url) }
                }
                if !loaded.isEmpty { model.openURLs(loaded) }
            }
            return true
        }
        .onChange(of: model.favoritesOnly) { _ in model.photoSelection.retain(model.selectionFiles); model.photoPage = 0 }
    }
}
