[![Build status](https://ci.appveyor.com/api/projects/status/c8arh5v18u285jmj/branch/master?svg=true)](https://ci.appveyor.com/project/amrali-eg/encodingchecker/branch/master)

# EncodingChecker v2.0
File Encoding Checker is a GUI tool that allows you to validate the text encoding of one or more files. The tool can display the encoding for all selected files, or only the files that do not have the encodings you specify.

File Encoding Checker requires Microsoft .NET Framework 4 to run.

![form image](./form.png "File Encoding Checker Form Preview")

## Fixed issues
Sorting the results by clicking a column header is working now.

When viewing a directory, some files matching the file masks were not listed.

Improved performance of the list view control for faster processing of results.

Added feature to export selected results to a text file.

Switched to UtfUnknown library for better encoding detection (Multiple bugs from Ude fixed).

Validating the detected file encoding to avoid errors during conversion of files.

UTF-16 text files without byte-order-mark (BOM) can be detected by heuristics.

## Credits
The original project [EncodingChecker](https://archive.codeplex.com/?p=encodingchecker) on CodePlex was written by [Jeevan James](https://github.com/JeevanJames).

For encoding detection, File Encoding Checker uses the [UtfUnknown](https://github.com/CharsetDetector/UTF-unknown) library, which is a C# port of [uchardet](https://gitlab.freedesktop.org/uchardet/uchardet) library - A C++ port of the original [Mozilla Universal Charset Detector](https://dxr.mozilla.org/mozilla/source/extensions/universalchardet/).

## Supported Charsets
File Encoding Checker currently supports over forty charsets.

* ASCII
* UTF-7 (with a BOM)
* UTF-8 (with or without a BOM)
* UTF-16 BE or LE (with or without a BOM)
* UTF-32 BE or LE (with a BOM)
* Arabic: iso-8859-6, windows-1256.
* Baltic: iso-8859-4, windows-1257.
* Central European: ibm852, iso-8859-2, windows-1250, x-mac-ce.
* Chinese (Traditional and Simplified): big5, GB18030, hz-gb-2312, x-cp50227.
* Cyrillic (primarily Russian): IBM855, cp866, iso-8859-5, koi8-r, windows-1251, x-mac-cyrillic.
* Estonian: iso-8859-13.
* Greek: iso-8859-7, windows-1253.
* Hebrew: iso-8859-8, windows-1255.
* Japanese: euc-jp, iso-2022-jp, shift_jis.
* Korean: euc-kr, iso-2022-kr, ks_c_5601-1987 (cp949).
* Thai: windows-874 (aliases TIS-620 and iso-8859-11 in .NET)
* Turkish: iso-8859-3, iso-8859-9.
* Western European: iso-8859-1, iso-8859-15, windows-1252.
* Vietnamese: windows-1258.
