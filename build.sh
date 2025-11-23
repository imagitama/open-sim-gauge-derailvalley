#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(realpath "$SCRIPT_DIR/..")"

version=$(<"$SCRIPT_DIR/VERSION.txt")
default_platform="win-x64" # "osx-arm64" "linux-x64"
platform=("$@")

if [ -z "$platform" ]; then
    platform="$default_platform"
fi

echo "Build $platform $version"

echo "Cleaning..."
rm -rf "$SCRIPT_DIR/dist"

echo "Building data source..."

DATA_SRC_ROOT="$SCRIPT_DIR/server/data-sources"
DATA_OUT_ROOT="$SCRIPT_DIR/dist/$platform/server/data-sources"

mkdir -p "$DATA_OUT_ROOT"

for projdir in "$DATA_SRC_ROOT"/*/ ; do
    [ -d "$projdir" ] || continue

    name="$(basename "$projdir")"
    csproj=$(find "$projdir" -maxdepth 1 -name '*.csproj' | head -n 1)

    if [ -z "$csproj" ]; then
        continue
    fi

    echo "  Building data source: $name"

    OUT_DIR="$projdir/bin/publish"
    rm -rf "$OUT_DIR"

    dotnet publish "$csproj" \
        -c Release -r "$platform" \
        --self-contained false \
        /p:PublishSingleFile=false \
        /p:PublishDir="$OUT_DIR"

    DEST="$DATA_OUT_ROOT"
    mkdir -p "$DEST"

    cp "$OUT_DIR"/*.dll "$DEST"/
done

echo "Copying..."
cp -R "$SCRIPT_DIR"/client "$SCRIPT_DIR/dist/$platform/client"
cp "$SCRIPT_DIR"/server/server.json "$SCRIPT_DIR/dist/$platform/server"
cp "$SCRIPT_DIR"/README.md "$SCRIPT_DIR/dist/$platform"
cp "$SCRIPT_DIR"/LICENSE.md "$SCRIPT_DIR/dist/$platform"
cp "$SCRIPT_DIR"/CHANGELOG.md "$SCRIPT_DIR/dist/$platform"
cp "$SCRIPT_DIR"/VERSION.txt "$SCRIPT_DIR/dist/$platform"

echo "Zipping..."
zip_name="opensimgauge-derailvalley-${version}-${platform}.zip"
(cd "$SCRIPT_DIR/dist/$platform" && zip -r "$SCRIPT_DIR/dist/$zip_name" .)

echo "Created $SCRIPT_DIR/dist/$zip_name"
echo "Done"
