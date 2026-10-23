#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PACKAGE="$ROOT/src/YingView.macOS"
VERSION="$(python3 -c 'import xml.etree.ElementTree as E,sys;print(E.parse(sys.argv[1]).findtext("PropertyGroup/Version"))' "$ROOT/src/YingView/YingView.csproj")"
SOURCE="$(git -C "$ROOT" rev-parse HEAD)"
swift test --package-path "$PACKAGE"
swift build --package-path "$PACKAGE" -c release --arch arm64 --arch x86_64
BIN="$(swift build --package-path "$PACKAGE" -c release --arch arm64 --arch x86_64 --show-bin-path)"
lipo -verify_arch arm64 x86_64 "$BIN/YingView"
for LANGUAGE in zh-CN en-US; do
  NAME="YingView"; [ "$LANGUAGE" != zh-CN ] || NAME="映窗"
  DEST="$ROOT/dist/macOS-$LANGUAGE"
  APP="$DEST/$NAME.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp "$BIN/YingView" "$APP/Contents/MacOS/YingView"
  cp "$PACKAGE/Info.plist" "$APP/Contents/Info.plist"
  python3 - "$APP/Contents/Info.plist" "$NAME" "$LANGUAGE" "$VERSION" "$SOURCE" "$ROOT" <<'PY'
import plistlib,sys,struct,pathlib
path,name,language,version,source,root=sys.argv[1:]
with open(path,'rb') as f: info=plistlib.load(f)
info.update(CFBundleDisplayName=name,CFBundleName=name,CFBundleExecutable='YingView',CFBundleShortVersionString=version,
    CFBundleVersion='1',ClientLanguage=language,SourceRevision=source,CFBundleIconFile='AppIcon',
    CFBundleDevelopmentRegion='zh-Hans' if language=='zh-CN' else 'en',CFBundleLocalizations=['zh-Hans' if language=='zh-CN' else 'en'],NSHighResolutionCapable=True,
    NSHumanReadableCopyright='版权所有 © 2026 光之十一' if language=='zh-CN' else 'Copyright © 2026 Shixi Lin')
with open(path,'wb') as f: plistlib.dump(info,f)
ico=(pathlib.Path(root)/'src'/'YingView'/'Assets'/f'yingview-{language}.ico').read_bytes()
entries=[struct.unpack_from('<BBBBHHII',ico,6+i*16) for i in range(struct.unpack_from('<H',ico,4)[0])]
pngs=[(e[6],ico[e[7]:e[7]+e[6]]) for e in entries if ico[e[7]:e[7]+8]==b'\x89PNG\r\n\x1a\n']
if not pngs: raise SystemExit('No PNG representation in existing localized icon')
(pathlib.Path(path).parent/'Resources'/'icon-source.png').write_bytes(max(pngs)[1])
PY
  ICONSET="$DEST/AppIcon.iconset"; mkdir -p "$ICONSET"
  for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$APP/Contents/Resources/icon-source.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE*2))
    sips -z "$DOUBLE" "$DOUBLE" "$APP/Contents/Resources/icon-source.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
  cp "$ROOT/LICENSE" "$ROOT/NOTICE" "$APP/Contents/Resources/"
  codesign --force --sign - "$APP"
  codesign --verify --deep --strict "$APP"
  # UDRO is an uncompressed, read-only installer disk image, not a ZIP archive.
  hdiutil create -volname "$NAME" -srcfolder "$APP" -format UDRO -ov "$DEST/$NAME.dmg"
  hdiutil verify "$DEST/$NAME.dmg"
done
