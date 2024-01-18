An alternative .scs extractor written in C#. Supports partial extraction as well as extracting all .scs files at once.

## Build
A self-contained binary for Windows is available on the Releases page. On other platforms, install the .NET 6 SDK and run the following:
```sh
git clone https://github.com/sk-zk/Extractor.git --recursive
cd Extractor
dotnet publish -c Release
```

## Usage
```
extractor path [options]

Options:
  -a, --all                  Extracts every .scs archive in the directory.
  -d, --dest=VALUE           The output directory.
                               Default: ./extracted/
      --headers-at-end       Ignores what the archive header says and reads
                               entry headers from the end of the file.
      --list                 Lists entry headers and exits.
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
      --tree                 Prins the directory tree and exits. Can be
                               combined with --partial, --paths, and --all.
  -?, -h, --help             Prints this message and exits.
```

## Dependencies
* [Mono.Options](https://www.nuget.org/packages/Mono.Options/)
* [TruckLib.HashFs](https://github.com/sk-zk/TruckLib/)
