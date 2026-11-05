import AppKit
import SwiftUI
import YingViewCore

struct PhotoSelectionView: View {
    @ObservedObject var model: ViewerModel
    private let columns = [GridItem(.adaptive(minimum: 150, maximum: 210), spacing: 12)]
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(Copy.t("选片", "Photo selection")).font(.title2.bold())
                Spacer()
                Picker(Copy.t("模式", "Mode"), selection: $model.clientMode) {
                    Text(Copy.t("摄影师", "Photographer")).tag(false)
                    Text(Copy.t("客人选片", "Client selection")).tag(true)
                }.frame(width: 220)
            }
            HStack {
                TextField(Copy.t("客户名（选填）", "Client name (optional)"), text: $model.clientName).disabled(!model.clientMode).frame(maxWidth: 240)
                Toggle(Copy.t("只看收藏", "Favorites only"), isOn: $model.favoritesOnly)
                Spacer()
            }
            HStack {
                Button(Copy.t("全选", "Select all")) { model.selectAllPhotos() }.keyboardShortcut("a", modifiers: .command)
                Button(Copy.t("清空选择", "Clear")) { model.photoSelection.replace([]) }
                Button(Copy.t("收藏所选", "Favorite")) { model.setFavorites(model.selectedPhotos, favorite: true) }.disabled(model.selectedPhotos.isEmpty)
                Button(Copy.t("取消收藏", "Unfavorite")) { model.setFavorites(model.selectedPhotos, favorite: false) }.disabled(model.selectedPhotos.isEmpty)
                Spacer()
            }.controlSize(.small)
            HStack {
                Button(Copy.t("对比两张", "Compare two")) { model.compareSelectedPhotos() }.disabled(model.selectedPhotos.count != 2)
                Button(Copy.t("移到废纸篓", "Move to Trash")) { model.trashSelection() }.disabled(model.clientMode || model.selectedPhotos.isEmpty)
                Spacer()
                Button(Copy.t("导出收藏…", "Export favorites…")) { model.exportPhotoFavorites() }.disabled(!model.files.contains { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) })
            }.controlSize(.small)
            ScrollView {
                LazyVGrid(columns: columns, spacing: 12) {
                    ForEach(model.selectionFiles, id: \.self) { url in
                        VStack(alignment: .leading, spacing: 7) {
                            VStack(spacing: 6) {
                                Thumbnail(url: url).frame(height: 112)
                                Text(url.lastPathComponent).font(.caption).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .contentShape(Rectangle())
                            .onTapGesture(count: 2) { model.previewPhoto(url) }
                            .onTapGesture { let modifiers = NSApp.currentEvent?.modifierFlags ?? []; model.selectPhoto(url, command: modifiers.contains(.command), shift: modifiers.contains(.shift)) }
                            Toggle(Copy.t("收藏", "Favorite"), isOn: Binding(get: { model.favoriteIDs.contains(PhotoSelectionState.identity(url)) }, set: { model.setFavorites([url], favorite: $0) })).toggleStyle(.checkbox)
                        }
                        .padding(8).background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 8))
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(model.photoSelection.selected.contains(PhotoSelectionState.identity(url)) ? model.accentColor : .clear, lineWidth: 2))
                    }
                }.padding(3)
            }.onDeleteCommand { model.trashSelection() }
            Text(Copy.t("已选 \(model.selectedPhotos.count) 张 · 收藏 \(model.files.filter { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) }.count) 张", "\(model.selectedPhotos.count) selected · \(model.files.filter { model.favoriteIDs.contains(PhotoSelectionState.identity($0)) }.count) favorites")).font(.caption)
            Text(model.photoStatus ?? (model.clientMode ? Copy.t("客人清单单独保存在本机；此模式不能删除原片。", "Client lists are saved separately on this Mac. Deletion of originals is disabled.") : Copy.t("⌘ 多选 · Shift 连选 · 双击大图；收藏不改动原片。", "⌘ multi-select · Shift range · Double-click to preview. Favorites do not change originals."))).font(.caption).foregroundStyle(.secondary)
        }
        .padding(16).disabled(model.photoBusy)
        .onChange(of: model.clientMode) { _ in model.photoSelection.replace([]); model.photoStatus = nil }
        .onChange(of: model.clientName) { _ in model.photoSelection.replace([]) }
        .onChange(of: model.favoritesOnly) { _ in model.photoSelection.retain(model.selectionFiles) }
    }
}
