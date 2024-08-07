## Extractor
A cross-platform .scs (HashFS) extractor written in C#. Supports raw dumps, partial extraction,
and extracting all .scs files at once.

HashFS v2, introduced with game version 1.50, is supported.

## Build
A self-contained binary for Windows is available on the Releases page. On other platforms, install the
.NET 6 (or higher) SDK and run the following:
```sh
git clone https://github.com/sk-zk/Extractor.git --recursive
cd Extractor
dotnet publish -c Release
```

## Usage
```
extractor path... [options]

Options:
  -a, --all                  Extracts all .scs archives in the specified
                               directory.
  -d, --dest=VALUE           The output directory.
                               Default: ./extracted/
      --list                 Lists entries and exits.
  -p, --partial=VALUE        Partial extraction, e.g.:
                               -p=/locale
                               -p=/def,/map
                               -p=/def/world/road.sii
  -P, --paths=VALUE          Same as --partial, but expects a text file
                               containing paths to extract, separated by
                               newlines.
  -r, --raw                  Directly dumps the contained files with their
                               hashed filenames rather than traversing the
                               archive's directory tree.
      --salt=VALUE           Ignores the salt in the archive header and uses
                               this one instead.
  -s, --skip-existing        Don't overwrite existing files.
      --table-at-end         [HashFS v1 only] Ignores what the archive header
                               says and reads the entry table from the end of
                               the file.
      --tree                 Prints the directory tree and exits. Can be
                               combined with --partial, --paths, and --all.
  -?, -h, --help             Prints this message and exits.
```

### Usage samples
Normal extraction:
```
extractor "path\to\file.scs"
```

Extract two .scs files at once:
```
extractor "path\to\file1.scs" "path\to\file2.scs"
```

Extract all .scs files in a directory:
```
extractor "path\to\directory" -a
```

Extract `/def` and `/manifest.sii` only:
```
extractor "path\to\file.scs" -p=/def,/manifest.sii
```

Extract `/map` only for all .scs files in a directory:
```
extractor "path\to\directory" -a -p=/map
```

Extract `locale.scs`:
```
extractor "path\to\locale.scs" -p=/locale
```
