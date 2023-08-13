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
  -d=VALUE                   The output directory.
                               Default: ./extracted/
  -p=VALUE                   Partial extraction, e.g. "-p=/map".
  -r, --raw                  Directly dumps the contained files with their
                               hashed filenames rather than traversing the
                               archive's directory tree.This allows for the
                               extraction of base_cfg.scs, core.scs and
                               locale.scs, which do not include a top level
                               directory listing.
  -s, --skip-existing        Don't overwrite existing files.
  -?, -h, --help             Prints this message.
```

## Dependencies
* [Mono.Options](https://www.nuget.org/packages/Mono.Options/)
* [TruckLib.HashFs](https://github.com/sk-zk/TruckLib/)
