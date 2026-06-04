#!/usr/bin/env bash
#
# build-deb.sh — publish pdn (the packet.net node host) self-contained for one
# RID and package it as a Debian .deb. Used locally and by publish-node.yml.
#
#   scripts/build-deb.sh <rid> <version>
#   e.g. scripts/build-deb.sh linux-arm64 0.1.0
#
# Cross-publishes from x64 (ReadyToRun via crossgen2), so all three arches build
# on the one self-hosted runner — no arch-native machine or cross C-toolchain.
# Produces artifacts/packetnet_<version>_<arch>.deb.
set -euo pipefail

rid="${1:?usage: build-deb.sh <rid> <version>}"
version="${2:?usage: build-deb.sh <rid> <version>}"

case "$rid" in
  linux-x64)   arch=amd64 ;;
  linux-arm64) arch=arm64 ;;
  linux-arm)   arch=armhf ;;
  *) echo "unknown rid: $rid (want linux-x64 | linux-arm64 | linux-arm)" >&2; exit 2 ;;
esac

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
proj="$root/src/Packet.Node/Packet.Node.csproj"
pub="$root/artifacts/node/$rid"
stage="$root/artifacts/deb/$rid"
out="$root/artifacts/packetnet_${version}_${arch}.deb"

echo "==> publish $rid (self-contained, single-file, R2R, invariant globalization)"
dotnet publish "$proj" -c Release -r "$rid" --self-contained true \
  -p:Version="$version" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=true \
  -p:InvariantGlobalization=true \
  -p:DebugType=none -p:DebugSymbols=false \
  -v minimal -o "$pub"

echo "==> stage .deb tree for $arch"
rm -rf "$stage"
install -d "$stage/opt/packetnet/app" "$stage/lib/systemd/system" \
           "$stage/etc/packetnet" "$stage/DEBIAN"
cp -a "$pub/." "$stage/opt/packetnet/app/"
cp "$root/packaging/packetnet.service" "$stage/lib/systemd/system/packetnet.service"
cp "$root/packaging/packetnet.yaml"    "$stage/etc/packetnet/packetnet.yaml"
sed -e "s/@ARCH@/$arch/" -e "s/@VERSION@/$version/" \
    "$root/packaging/control.in" > "$stage/DEBIAN/control"
cp "$root/packaging/conffiles" "$root/packaging/postinst" \
   "$root/packaging/prerm" "$root/packaging/postrm" "$stage/DEBIAN/"
chmod 0755 "$stage/DEBIAN/postinst" "$stage/DEBIAN/prerm" "$stage/DEBIAN/postrm"

echo "==> build .deb"
mkdir -p "$root/artifacts"
# --root-owner-group (dpkg >= 1.19): root:root files without fakeroot.
dpkg-deb --build --root-owner-group "$stage" "$out"

echo "==> built $out"
dpkg-deb --info "$out"
echo "--- contents (top) ---"
dpkg-deb --contents "$out" | awk '{print $1, $6}' | head -30
if command -v lintian >/dev/null 2>&1; then
  lintian "$out" || true
else
  echo "(lintian not installed — skipping deb-lint)"
fi
