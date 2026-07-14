#!/usr/bin/env bash
# Downloads the fonts referenced by PakkaHisaab.Maui.csproj into Resources/Fonts.
# Run once after cloning:  bash tools/get_fonts.sh
set -euo pipefail

FONTS_DIR="$(cd "$(dirname "$0")/.." && pwd)/src/PakkaHisaab.Maui/Resources/Fonts"
mkdir -p "$FONTS_DIR"

echo "Downloading Poppins (SIL OFL)…"
curl -sL -o "$FONTS_DIR/Poppins-Regular.ttf"  "https://github.com/google/fonts/raw/main/ofl/poppins/Poppins-Regular.ttf"
curl -sL -o "$FONTS_DIR/Poppins-SemiBold.ttf" "https://github.com/google/fonts/raw/main/ofl/poppins/Poppins-SemiBold.ttf"
curl -sL -o "$FONTS_DIR/Poppins-Bold.ttf"     "https://github.com/google/fonts/raw/main/ofl/poppins/Poppins-Bold.ttf"

echo "Downloading Material Symbols Rounded (Apache 2.0)…"
curl -sL -o "$FONTS_DIR/MaterialSymbolsRounded.ttf" \
  "https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsRounded%5BFILL%2CGRAD%2Copsz%2Cwght%5D.ttf"

echo "Done. Fonts in $FONTS_DIR"
