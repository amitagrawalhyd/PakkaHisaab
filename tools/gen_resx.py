#!/usr/bin/env python3
"""
Generates AppStrings.resx (neutral/English) plus culture-specific satellite .resx files
for all 22 additional languages from translations.json.

.NET resource fallback means a language file only needs the keys that differ from English;
anything missing falls back to the neutral resource automatically. Core UI strings are
translated for every language below; the long tail can be completed in translations.json
and regenerated with:  python3 tools/gen_resx.py
"""
import json
import os
import xml.sax.saxutils as sx

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "PakkaHisaab.Maui", "Resources", "Strings")

RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""


def write_resx(path, entries):
    with open(path, "w", encoding="utf-8") as f:
        f.write(RESX_HEADER)
        for key, value in sorted(entries.items()):
            f.write(f'  <data name="{key}" xml:space="preserve">\n')
            f.write(f"    <value>{sx.escape(value)}</value>\n")
            f.write("  </data>\n")
        f.write("</root>\n")


def main():
    with open(os.path.join(HERE, "translations.json"), encoding="utf-8") as f:
        translations = json.load(f)

    english = translations.pop("en")
    write_resx(os.path.join(OUT, "AppStrings.resx"), english)
    print(f"AppStrings.resx           {len(english):3d} keys (neutral)")

    for culture, entries in translations.items():
        unknown = set(entries) - set(english)
        if unknown:
            raise SystemExit(f"{culture}: keys not present in English: {unknown}")
        write_resx(os.path.join(OUT, f"AppStrings.{culture}.resx"), entries)
        print(f"AppStrings.{culture}.resx".ljust(26) + f"{len(entries):3d} keys")


if __name__ == "__main__":
    main()
