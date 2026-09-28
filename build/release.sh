#!/bin/bash
###############################################################################
#  Road Editor for Mac — build / sign / notarize a universal .app + .dmg
#
#  Usage:  build/release.sh [version]        (default version: 1.0.0)
#
#  Prereqs (one-time):
#    - .NET 8 SDK at ~/.dotnet (Microsoft build; needed for osx-x64/arm64 RID
#      publish). Homebrew dotnet@8 also works.
#    - Signing identity "Developer ID Application: Catherine Gramze (AJHGU52KS3)".
#    - notarytool keychain profile "clean-installer" (Apple ID + app-specific
#      password stored via `xcrun notarytool store-credentials clean-installer`).
#    - build/AppIcon.icns  and  build/entitlements.plist  (tracked in repo).
#
#  Icon: build/AppIcon.icns is drawn by build/icon.swift (a road crossing a
#  river on a bridge).
###############################################################################
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"

APP_NAME="Road Editor for Mac"
PROJ="RoadEditor.App/RoadEditor.App.csproj"
BUNDLE_ID="com.gramzesweatshop.roadeditor"
VERSION="${1:-1.0.0}"
SIGN_ID="Developer ID Application: Catherine Gramze (AJHGU52KS3)"
NOTARY_PROFILE="clean-installer"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

BUILD="$ROOT/build"
STAGE="$BUILD/stage"
APP="$BUILD/$APP_NAME.app"
DMG="$BUILD/$APP_NAME-$VERSION.dmg"

echo ">> Publishing both architectures (self-contained, single-file)…"
rm -rf "$STAGE"; mkdir -p "$STAGE"
for RID in osx-x64 osx-arm64; do
    "$DOTNET" publish "$PROJ" -c Release -r "$RID" --self-contained true \
        -p:PublishSingleFile=true -o "$STAGE/$RID"
done

echo ">> Assembling universal .app…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# Apphost is per-arch -> lipo into a universal binary.
lipo -create "$STAGE/osx-x64/$APP_NAME" "$STAGE/osx-arm64/$APP_NAME" \
     -output "$APP/Contents/MacOS/$APP_NAME"
chmod +x "$APP/Contents/MacOS/$APP_NAME"
# The SkiaSharp/HarfBuzz/AvaloniaNative dylibs ship already-universal; copy as-is.
# (Deliberately NOT copying *.pdb — a non-code file in MacOS/ breaks codesign.)
cp "$STAGE/osx-arm64/"*.dylib "$APP/Contents/MacOS/"
cp "$BUILD/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>$APP_NAME</string>
	<key>CFBundleDisplayName</key><string>$APP_NAME</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleExecutable</key><string>$APP_NAME</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>CFBundleSignature</key><string>????</string>
	<key>LSMinimumSystemVersion</key><string>10.15</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
	<key>NSHumanReadableCopyright</key><string>Road Editor © 2026 GramzeSweatshop; neighborhood file handlers © 2008–2010 Mootilda. GPL v2 or later.</string>
</dict>
</plist>
PLIST

echo ">> Signing (inner dylibs first, then the bundle with hardened runtime)…"
for f in "$APP/Contents/MacOS/"*.dylib; do
    codesign --force --options runtime --timestamp --sign "$SIGN_ID" "$f"
done
codesign --force --options runtime --timestamp \
    --entitlements "$BUILD/entitlements.plist" --sign "$SIGN_ID" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

echo ">> Building + signing the .dmg…"
DMGSTAGE="$BUILD/dmgstage"; rm -rf "$DMGSTAGE"; mkdir -p "$DMGSTAGE"
cp -R "$APP" "$DMGSTAGE/"
ln -s /Applications "$DMGSTAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "$APP_NAME" -srcfolder "$DMGSTAGE" -ov -format UDZO "$DMG"
codesign --force --timestamp --sign "$SIGN_ID" "$DMG"

echo ">> Notarizing (uploads to Apple, waits for the verdict)…"
xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait

echo ">> Stapling…"
xcrun stapler staple "$DMG"
xcrun stapler staple "$APP"

echo ">> Gatekeeper check:"
spctl -a -vv -t open --context context:primary-signature "$DMG" || true

echo ">> DONE: $DMG"
