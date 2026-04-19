#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$SCRIPT_DIR/src/MediaBitrateViewer.App/MediaBitrateViewer.App.csproj"
DIST_DIR="$SCRIPT_DIR/dist"
PACKAGING_DIR="$SCRIPT_DIR/packaging"
APP_NAME="MediaBitrateViewer"
ASSEMBLY_NAME="MediaBitrateViewer.App"
DEFAULT_RIDS=("win-x64" "win-arm64" "osx-arm64" "osx-x64" "linux-x64" "linux-arm64")

VERSION=""
RIDS=()
DEPLOY_MAC=false

usage() {
    echo "Usage: $0 [--version <version>] [--rid <rid>] [--deploy-mac]"
    echo ""
    echo "Options:"
    echo "  --version, -v   Set the version (default: read from csproj)"
    echo "  --rid, -r       Build for a specific RID (default: all)"
    echo "                  Available: win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64"
    echo "  --deploy-mac    Build osx-arm64 slim and deploy to /Applications/$APP_NAME.app"
    echo ""
    echo "Examples:"
    echo "  $0                          Build all platforms"
    echo "  $0 --rid osx-arm64          Build macOS ARM only"
    echo "  $0 --version 1.2.3          Build all with version 1.2.3"
    echo "  $0 --deploy-mac             Build and deploy to /Applications"
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version|-v)
            VERSION="$2"
            shift 2
            ;;
        --rid|-r)
            RIDS+=("$2")
            shift 2
            ;;
        --deploy-mac)
            DEPLOY_MAC=true
            shift
            ;;
        --help|-h)
            usage
            ;;
        *)
            echo "Unknown option: $1"
            usage
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROJECT" | head -1)
    if [[ -z "$VERSION" ]]; then
        echo "Error: Could not read version from $PROJECT"
        exit 1
    fi
fi

if [[ ${#RIDS[@]} -eq 0 ]]; then
    RIDS=("${DEFAULT_RIDS[@]}")
fi

echo "Building $APP_NAME v$VERSION"
echo "Targets: ${RIDS[*]}"
echo ""

mkdir -p "$DIST_DIR"
rm -rf "$DIST_DIR/*"

publish() {
    local rid="$1"
    local self_contained="$2"
    local variant="$3"
    local publish_dir="$DIST_DIR/publish/$rid/$variant"
    # PublishSingleFile implicitly sets PublishSelfContained=true,
    # so only use it for self-contained Windows builds
    local extra_args=()
    if [[ "$rid" == win-* && "$self_contained" == "true" ]]; then
        extra_args+=(-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
    fi

    echo "  Publishing $rid ($variant)..."
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained "$self_contained" \
        -p:Version="$VERSION" \
        -o "$publish_dir" \
        -v quiet \
        "${extra_args[@]}"
}

package_windows() {
    local rid="$1"
    local variant="$2"
    local publish_dir="$DIST_DIR/publish/$rid/$variant"
    local suffix=""
    [[ "$variant" == "slim" ]] && suffix="-slim"

    if [[ "$variant" == "full" ]]; then
        local exe_name="$APP_NAME-$VERSION-$rid.exe"
        echo "  Packaging $rid ($variant) -> $exe_name"
        cp "$publish_dir/$ASSEMBLY_NAME.exe" "$DIST_DIR/$exe_name"
    else
        local archive_name="$APP_NAME-$VERSION-$rid$suffix"
        echo "  Packaging $rid ($variant) -> $archive_name.zip"
        local staging="$DIST_DIR/staging/$archive_name"
        mkdir -p "$staging"
        cp -r "$publish_dir/"* "$staging/"
        (cd "$DIST_DIR/staging" && zip -rq "$DIST_DIR/$archive_name.zip" "$archive_name")
        rm -rf "$staging"
    fi
}

package_macos() {
    local rid="$1"
    local variant="$2"
    local publish_dir="$DIST_DIR/publish/$rid/$variant"
    local suffix=""
    [[ "$variant" == "slim" ]] && suffix="-slim"
    local bundle_name="$APP_NAME-$VERSION-$rid$suffix"

    echo "  Packaging $rid ($variant) -> $bundle_name.app"

    local app_bundle="$DIST_DIR/$bundle_name.app"
    mkdir -p "$app_bundle/Contents/MacOS"
    mkdir -p "$app_bundle/Contents/Resources"

    cp -r "$publish_dir/"* "$app_bundle/Contents/MacOS/"

    sed "s/VERSION_PLACEHOLDER/$VERSION/g" "$PACKAGING_DIR/macos/Info.plist" \
        > "$app_bundle/Contents/Info.plist"

    if [[ -f "$PACKAGING_DIR/macos/mediabitrateviewer.icns" ]]; then
        cp "$PACKAGING_DIR/macos/mediabitrateviewer.icns" "$app_bundle/Contents/Resources/"
    fi

    chmod +x "$app_bundle/Contents/MacOS/$ASSEMBLY_NAME"
}

package_linux() {
    local rid="$1"
    local variant="$2"
    local publish_dir="$DIST_DIR/publish/$rid/$variant"
    local suffix=""
    [[ "$variant" == "slim" ]] && suffix="-slim"
    local archive_name="$APP_NAME-$VERSION-$rid$suffix"

    echo "  Packaging $rid ($variant) -> $archive_name.tar.gz"

    local staging="$DIST_DIR/staging/$archive_name"
    mkdir -p "$staging"
    cp -r "$publish_dir/"* "$staging/"

    if [[ -f "$PACKAGING_DIR/linux/mediabitrateviewer.desktop" ]]; then
        cp "$PACKAGING_DIR/linux/mediabitrateviewer.desktop" "$staging/"
    fi

    if [[ -f "$PACKAGING_DIR/linux/mediabitrateviewer.png" ]]; then
        cp "$PACKAGING_DIR/linux/mediabitrateviewer.png" "$staging/mediabitrateviewer.png"
    fi

    chmod +x "$staging/$ASSEMBLY_NAME"

    tar -czf "$DIST_DIR/$archive_name.tar.gz" -C "$DIST_DIR/staging" "$archive_name"
    rm -rf "$staging"
}

if [[ "$DEPLOY_MAC" == true ]]; then
    echo "Building and deploying $APP_NAME v$VERSION to /Applications..."
    echo ""

    mkdir -p "$DIST_DIR"
    publish "osx-arm64" false "slim"
    package_macos "osx-arm64" "slim"

    local_app="/Applications/$APP_NAME.app"
    built_app="$DIST_DIR/$APP_NAME-$VERSION-osx-arm64-slim.app"

    rm -rf "$local_app"
    cp -r "$built_app" "$local_app"

    rm -rf "$DIST_DIR/publish" "$DIST_DIR/staging"
    rm -rf "$built_app"

    echo ""
    echo "Deployed to $local_app"
    exit 0
fi

for RID in "${RIDS[@]}"; do
    echo ""
    echo "=== $RID ==="

    publish "$RID" true "full"
    publish "$RID" false "slim"

    case "$RID" in
        win-*)
            package_windows "$RID" "full"
            package_windows "$RID" "slim"
            ;;
        osx-*)
            package_macos "$RID" "full"
            package_macos "$RID" "slim"
            ;;
        linux-*)
            package_linux "$RID" "full"
            package_linux "$RID" "slim"
            ;;
    esac
done

rm -rf "$DIST_DIR/publish" "$DIST_DIR/staging"

echo ""
echo "Build complete. Artifacts:"
du -sh "$DIST_DIR"/* | sort -k2
