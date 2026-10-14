#!/bin/zsh
# Builds "Bluey.app" into build/ from the Swift package. Works with just the Command Line Tools.
set -euo pipefail
cd "$(dirname "$0")/.."

swift build -c release --product GooglyMac

# Assemble and sign outside the project: iCloud-synced folders (like Documents) attach Finder
# info to the bundle, which codesign rejects. The signed app is then copied into build/.
FINAL="build/Bluey.app"
STAGE=$(mktemp -d)
APP="$STAGE/Bluey.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$(swift build -c release --show-bin-path)/GooglyMac" "$APP/Contents/MacOS/GooglyMac"
cp Mac/Info.plist "$APP/Contents/Info.plist"

# The version comes from Config/Shared.xcconfig, the same file the iPhone project uses.
read_setting() { sed -n "s/^$1[[:space:]]*=[[:space:]]*//p" Config/Shared.xcconfig | head -1 | tr -d '[:space:]'; }
VERSION=$(read_setting MARKETING_VERSION)
BUILD=$(read_setting CURRENT_PROJECT_VERSION)
[[ -n "$VERSION" && -n "$BUILD" ]] || { echo "MARKETING_VERSION or CURRENT_PROJECT_VERSION missing in Config/Shared.xcconfig" >&2; exit 1; }
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" -c "Set :CFBundleVersion $BUILD" "$APP/Contents/Info.plist"
cp Shared/Fonts/*.ttf "$APP/Contents/Resources/"

# Sign with your Apple Development certificate when there is one, so macOS remembers the
# Screen Recording and Microphone permissions across rebuilds. Otherwise sign ad hoc.
# Use the certificate's hash, since the same name can appear twice in the keychain.
IDENTITY=$(security find-identity -v -p codesigning | grep '"Apple Development' | grep -v REVOKED | head -1 | awk '{print $2}' || true)
codesign --force --sign "${IDENTITY:--}" "$APP"
# macOS refuses to launch an app whose certificate Apple has revoked, even when the keychain
# still lists it as valid. Fall back to ad hoc signing then (permissions reset on each rebuild).
if [[ "$(spctl -a -vv "$APP" 2>&1 || true)" == *REVOKED* ]]; then
  echo "Signing certificate is revoked; signing ad hoc instead"
  codesign --force --sign - "$APP"
fi

rm -rf "$FINAL"
mkdir -p build
ditto --norsrc --noextattr "$APP" "$FINAL"
rm -rf "$STAGE"

echo "Built $FINAL"
echo "Run it with: open \"$FINAL\""
