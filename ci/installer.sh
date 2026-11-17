#!/usr/bin/env bash
# The installer people download (docs/install.md): an MSI of a published app folder, and WiX's standard setup .exe around
# it, by WiX v5.0.2 (pinned in .config/dotnet-tools.json) and its one extension. In stages, so the sign job can sign
# between them, each from the repository's root:
#   tools     WiX and its extension, fetched (the sign job does this before it signs in, so nothing is downloaded after)
#   msi       the MSI of artifacts/zip/tafs-$PLATFORM
#   bundle    the setup .exe around that MSI
#   detach    the setup's engine, out to artifacts/installer/engine-<arch>.exe to be signed (a setup's own signature
#             covers the engine only once the engine is signed inside it)
#   reattach  the signed engine back into the setup, which is then signed itself
# Reads PLATFORM (x64 or ARM64), VERSION (the MSI's) and SETUP_NAME (the setup's file name), as ci/version.sh sets them.
set -euo pipefail

arch=$(echo "$PLATFORM" | tr '[:upper:]' '[:lower:]')
mkdir -p artifacts/installer
# WiX reads -d paths from the .wxs file's folder, so they're absolute Windows paths.
msi=$(cygpath -w "$PWD/artifacts/installer/tafs-$VERSION-$arch.msi")
setup="artifacts/installer/tafs-setup-$SETUP_NAME-$arch.exe"
engine="artifacts/installer/engine-$arch.exe"
icon=$(cygpath -w "$PWD/src/Tafs.App/Assets/tafs.ico")

case "${1:-}" in
  tools)
    dotnet tool restore
    dotnet wix extension add WixToolset.Bal.wixext/5.0.2
    ;;
  msi)
    app=$(cygpath -w "$PWD/artifacts/zip/tafs-$PLATFORM")
    # -wx makes a warning (a folder it can't harvest, say) fail the build instead of shipping an installer with nothing in it.
    dotnet wix build installer/Package.wxs -wx -arch "$arch" -d AppDir="$app" -d Version="$VERSION" -d Icon="$icon" -o "$msi"
    ;;
  bundle)
    # The 5.0.2 package names its DLL after the bootstrapper applications, so it's named by its path.
    bal=.wix/extensions/WixToolset.Bal.wixext/5.0.2/wixext5/WixToolset.BootstrapperApplications.wixext.dll
    # tafs's mark and icon: the setup window's corner and Windows' installed-apps list.
    logo=$(cygpath -w "$PWD/installer/logo.png")
    dotnet wix build installer/Bundle.wxs -wx -arch "$arch" -ext "$bal" -d Msi="$msi" -d Version="$VERSION" -d Logo="$logo" \
      -d Icon="$icon" -o "$setup"
    ;;
  detach)
    dotnet wix burn detach "$setup" -engine "$engine"
    ;;
  reattach)
    dotnet wix burn reattach "$setup" -engine "$engine" -o "$setup.reattached"
    mv -f "$setup.reattached" "$setup"
    rm -f "$engine"
    ;;
  *)
    echo "usage: ci/installer.sh tools|msi|bundle|detach|reattach" >&2
    exit 2
    ;;
esac
ls -la artifacts/installer
