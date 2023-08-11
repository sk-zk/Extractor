An alternative .scs extractor written in C#. Supports partial extraction as well as extracting all .scs files at once.

## Usage
```
extractor path [options]

Options:
  -a, --all                  Extracts every .scs file in the directory.
  -d=VALUE                   The output directory.
                               Default: ./extracted/
  -p=VALUE                   Partial extraction, e.g. "-p=/map".
  -?, -h, --help             Prints this message.
```

## Dependencies
* [Mono.Options](https://www.nuget.org/packages/Mono.Options/)
* [TruckLib.HashFs](https://github.com/sk-zk/TruckLib/)
