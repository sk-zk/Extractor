# Extractor
A cross-platform .scs extractor for both HashFS and ZIP.

## Features
* Supports HashFS v1 and v2 as well as ZIP (including "locked" ZIP files)
* Can extract multiple archives at once
* Partial extraction
* Raw dumps
* Built-in path-finding mode for HashFS archives without directory listings
* Automatic conversion of 3nK-encoded and encrypted SII files

## Build
A Windows executable is available on the Releases page. On other platforms, install the
.NET 8 SDK and run the following:

```sh
git clone https://github.com/sk-zk/Extractor.git
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
    <td><b>Description</b></td>
  </tr>
</thead>
<tr>
  <td><code>-a</code></td>
  <td><code>--all</code></td>
  <td>Extracts all .scs archives in the specified directory.</td>
</tr>
<tr>
  <td><code>-d</code></td>
  <td><code>--dest</code></td>
  <td>Sets the output directory. Defaults to <code>./extracted</code>.</td>
</tr>
<tr>
  <td></td>
  <td><code>--list</code></td>
  <td>Lists paths contained in the archive. Can be combined with <code>--deep</code>.</td>
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
  line breaks.</td>
</tr>
<tr>
  <td><code>-s</code></td>
  <td><code>--skip-existing</code></td>
  <td>Don't overwrite existing files.</td>
</tr>
<tr>
  <td></td>
  <td><code>--tree</code></td>
  <td>Prints the directory tree and exits. Can be combined with <code>--deep</code>, <code>--partial</code>, 
  <code>--paths</code>, and <code>--all</code>.</td>
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
    <td><b>Description</b></td>
  </tr>
</thead>
<tr>
  <td></td>
  <td><code>--additional</code></td>
  <td>When using <code>--deep</code>, specifies additional start paths to search.
  Expects a text file containing paths to extract, separated by line breaks.</td>
</tr>
<tr>
  <td></td>
  <td><code>--deep</code></td>
  <td>An extraction mode which scans the contained entries for referenced paths instead of traversing
  the directory tree from <code>/</code>. Use this option to extract archives without a top level directory listing.</td>
</tr>
<tr>
  <td></td>
  <td><code>--list-all</code></td>
  <td>When using <code>--deep</code>, lists all paths referenced by files in the archive,
  even if they are not contained in it.</td>
</tr>
<tr>
  <td></td>
  <td><code>--list-entries</code></td>
  <td>Lists entries contained in the archive.</td>
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
  <td>Ignores the salt specified in the archive header and uses the given one instead.</td>
</tr>
<tr>
  <td></td>
  <td><code>--table-at-end</code></td>
  <td>[v1 only] Ignores what the archive header says and reads the entry table from
  the end of the file.</td>
</tr>
</table>


### Examples
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

Extract with deep mode:
```
extractor "path\to\file.scs" --deep
```

Extract with deep mode when the mod has a separate defs archive:
```
extractor "defs.scs" --deep
extractor "defs.scs" --deep --list-all > paths.txt
extractor "other.scs" --deep --additional=paths.txt
```
