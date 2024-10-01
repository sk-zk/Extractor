## Extractor
A cross-platform .scs extractor for both HashFS and ZIP archives. Supports raw dumps,
partial extraction, and extracting all .scs files at once.

HashFS v2, introduced with game version 1.50, is supported.

## Build
A self-contained binary for Windows is available on the Releases page. On other platforms, install the
.NET 8 SDK and run the following:
```sh
git clone https://github.com/sk-zk/Extractor.git --recursive
cd Extractor
dotnet publish -c Release
```

## Usage
```
extractor path... [options]
```

### General options
<table>
<thead>
  <tr>
    <td><b>Short</b></td>
    <td><b>Long</b></td>
    <td><b>Descripton</b></td>
  </tr>
</thead>
<tr>
  <td><code>-a</code></td>
  <td><code>--all</code></td>
  <td>Extract all .scs archives in the specified directory.</td>
</tr>
<tr>
  <td><code>-d</code></td>
  <td><code>--dest</code></td>
  <td>Sets the output directory. Defaults to <code>./extracted</code>.</td>
</tr>
<tr>
  <td><code>-p</code></td>
  <td><code>--partial</code></td>
  <td>Limits extraction to the comma-separated list of files and/or directories specified. Examples:<br>
  <code>-p=/locale</code><br>
  <code>-p=/def,/map</code><br>
  <code>-p=/def/world/road.sii</code><br>
  </td>
</tr>
<tr>
  <td><code>-P</code></td>
  <td><code>--paths</code></td>
  <td>Same as <code>--partial</code>, but expects a text file containing paths to extract, separated by
  newlines.</td>
</tr>
<tr>
  <td><code>-s</code></td>
  <td><code>--skip-existing</code></td>
  <td>Don't overwrite existing files.</td>
</tr>
<tr>
  <td><code>-?</code>, <code>-h</code></td>
  <td><code>--help</code></td>
  <td>Prints the extractor's version and usage information.</td>
</tr>
</table>

### HashFS options
<table>
<thead>
  <tr>
    <td><b>Short</b></td>
    <td><b>Long</b></td>
    <td><b>Descripton</b></td>
  </tr>
</thead>
<tr>
  <td></td>
  <td><code>--list</code></td>
  <td>Lists entries and exits.</td>
</tr>
<tr>
  <td><code>-r</code></td>
  <td><code>--raw</code></td>
  <td>Directly dumps the contained files with their hashed filenames rather than traversing
  the archive's directory tree.</td>
</tr>
<tr>
  <td></td>
  <td><code>--salt</code></td>
  <td>Ignores the salt specified the archive header and uses the given one instead.</td>
</tr>
<tr>
  <td></td>
  <td><code>--table-at-end</code></td>
  <td>[v1 only] Ignores what the archive header says and reads the entry table from
  the end of the file.</td>
</tr>
<tr>
  <td></td>
  <td><code>--tree</code></td>
  <td>Prints the directory tree and exits. Can be combined with <code>--partial</code>, <code>--paths</code>, and <code>--all</code>.</td>
</tr>
</table>

### Samples
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

Extract `def` and `manifest.sii` only:
```
extractor "path\to\file.scs" -p=/def,/manifest.sii
```

Extract `map` only for all .scs files in a directory:
```
extractor "path\to\directory" -a -p=/map
```

Extract `locale.scs`:
```
extractor "path\to\locale.scs" -p=/locale
```
