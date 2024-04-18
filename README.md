## Extractor
A cross-platform .scs (HashFS) extractor written in C#. Supports raw dumps, partial extraction, and extracting all .scs files at once.

HashFS v2, introduced with game version 1.50, is supported, with one limitation: the packed .tobj format, which
.tobj/.dds pairs are converted to in v2, can be extracted, but not unpacked.

## Build
A self-contained binary for Windows is available on the Releases page. On other platforms, install the .NET 6 SDK and run the following:
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
                               -p=/map
                               -p=/def,/map
                               -p=/def/world/road.sii
      --paths=VALUE          Same as --partial, but expects a text file
                               containing paths to extract, separated by
                               newlines.
  -r, --raw                  Directly dumps the contained files with their
                               hashed filenames rather than traversing the
                               archive's directory tree. This allows for the
                               extraction of base_cfg.scs, core.scs and
                               locale.scs, which do not include a top level
                               directory listing.
      --salt=VALUE           Ignores the salt in the archive header and uses
                               this one instead.
  -s, --skip-existing        Don't overwrite existing files.
      --table-at-end         [HashFS v1 only] Ignores what the archive header
                               says and readsthe entry table from the end of
                               the file.
      --tree                 Prints the directory tree and exits. Can be
                               combined with --partial, --paths, and --all.
  -?, -h, --help             Prints this message and exits.
```

## Dependencies
* [Mono.Options](https://www.nuget.org/packages/Mono.Options/)
* [TruckLib.HashFs](https://github.com/sk-zk/TruckLib/)
