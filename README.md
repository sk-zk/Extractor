An alternative .scs extractor written in C#. Supports partial extraction as well as extracting all .scs files at once.

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
  -?, -h, --help             Prints this message.
```

## Dependencies
* [Mono.Options](https://www.nuget.org/packages/Mono.Options/)
* [TruckLib.HashFs](https://github.com/sk-zk/TruckLib/)
