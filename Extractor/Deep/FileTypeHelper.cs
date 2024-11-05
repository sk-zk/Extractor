using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor.Deep
{
    public static class FileTypeHelper
    {
        private static readonly Dictionary<FileType, Func<byte[], bool>> FileTypeCheckers = new()
        {
            { FileType.Dds, IsDdsFile },
            { FileType.Font, IsFontFile },
            { FileType.Jpeg, IsJpegFile },
            { FileType.Material, IsMatFile },
            { FileType.Pdn, IsPdnFile },
            { FileType.Pmg, IsPmgFile },
            { FileType.Pmc, IsPmcFile },
            { FileType.Pmd, IsPmdFile },
            { FileType.Pma, IsPmaFile },
            { FileType.Png, IsPngFile },
            { FileType.Psd, IsPsdFile },
            { FileType.Sii, IsSiiFile },
            { FileType.SoundBank, IsSoundBankFile },
            { FileType.SoundBankGuids, IsSoundBankGuidsFile },
            { FileType.TgaMask, IsTgaFile },
            { FileType.Tobj, IsTobjFile },
            { FileType.SoundRef, IsSoundRefFile },
        };

        public static FileType Infer(byte[] fileBuffer)
        {
            if (fileBuffer.Length == 0)
                return FileType.Unknown;

            foreach (var (type, checker) in FileTypeCheckers)
            {
                if (checker(fileBuffer))
                    return type;
            }
            return FileType.Unknown;
        }

        private static bool IsSiiFile(byte[] fileBuffer)
        {
            if (fileBuffer.Length < 4)
                return false;

            var magic = Encoding.UTF8.GetString(fileBuffer[0..4]);
            return magic == "SiiN"        // regular SII
                || magic == "\uFEFFS"      // regular SII with BOM, ugh
                || magic == "ScsC"         // encrypted SII
                || magic.StartsWith("3nK") // 3nK-encoded SII
            ;
        }

        private static bool IsPdnFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 3 &&
                Encoding.ASCII.GetString(fileBuffer[0..3]) == "PDN";
        }

        private static bool IsPmgFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 4 &&
                Encoding.ASCII.GetString(fileBuffer[1..4]) == "gmP";
        }

        private static bool IsMatFile(byte[] fileBuffer)
        {
            if (fileBuffer.Length < 10)
                return false;

            var start = Encoding.UTF8.GetString(fileBuffer[0..10]).Trim('\uFEFF');
            return start.StartsWith("material") || start.StartsWith("effect");
        }

        private static bool IsSoundBankFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 4 &&
                Encoding.ASCII.GetString(fileBuffer[0..4]) == "RIFF";
        }

        private static bool IsDdsFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 3 &&
                Encoding.ASCII.GetString(fileBuffer[0..3]) == "DDS";
        }

        private static bool IsSoundRefFile(byte[] fileBuffer)
        {
            var lines = Encoding.UTF8.GetString(fileBuffer)
                .Trim(['\uFEFF', '\0'])
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("source="))
                    return true;
            }
            return false;
        }

        private const uint MaxPlausiblePmdPieceCount = 5000;

        private static bool IsPmdFile(byte[] fileBuffer)
        {
            // PMD/PMA/PMC don't have any magic bytes to distinguish them,
            // so we need to make an educated guess.
            // PMC is on version 6, I haven't seen any lower versions in
            // the wild so far, and PMD and PMA are still below 6, so
            // for PMC, checking the version is sufficient.
            // PMA and PMD, however, have overlapping versions. PMD is
            // on version 4, and PMA is on version 5, but version 4 still
            // exists in the wild. To distinguish between the two, I read
            // an integer from offset 12, which in PMD is the piece count
            // and in PMA is the MSB of a hash, so if this integer is too
            // large to plausibly be a piece count, the file must be a
            // PMA file.

            if (fileBuffer.Length < 20)
                return false;

            if (BitConverter.ToInt32(fileBuffer.AsSpan()[0..4]) != 4)
                return false;

            var pieceCountMaybe = BitConverter.ToUInt32(fileBuffer.AsSpan()[12..16]);
            return pieceCountMaybe is > 0 and <= MaxPlausiblePmdPieceCount;
        }

        private static bool IsPmaFile(byte[] fileBuffer)
        {
            // See the comment in IsPmdFile for an explanation.

            if (fileBuffer.Length < 20)
                return false;

            var version = BitConverter.ToInt32(fileBuffer.AsSpan()[0..4]);
            if (version is not (4 or 5))
                return false;

            var pieceCountMaybe = BitConverter.ToUInt32(fileBuffer.AsSpan()[12..16]);
            return pieceCountMaybe is 0 or > MaxPlausiblePmdPieceCount;
        }

        private static bool IsPmcFile(byte[] fileBuffer)
        {
            // See the comment in IsPmdFile for an explanation.

            return fileBuffer.Length > 4
                && BitConverter.ToInt32(fileBuffer.AsSpan()[0..4]) == 6;
        }

        private static bool IsTobjFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 4
                && BitConverter.ToInt32(fileBuffer.AsSpan()[0..4]) == 0x70b10a01;
        }

        private static bool IsSoundBankGuidsFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 38 &&
                Guid.TryParse(Encoding.ASCII.GetString(fileBuffer[0..38]), out var _);
        }

        private static bool IsFontFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 10 &&
                Encoding.UTF8.GetString(fileBuffer[0..10]) == "# SCS Font";
        }

        private static bool IsJpegFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 2
                && fileBuffer[0] == 0xFF && fileBuffer[1] == 0xD8;
        }

        private static bool IsTgaFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 18
                && Encoding.ASCII.GetString(fileBuffer[^18..^2]) == "TRUEVISION-XFILE";
        }

        private static bool IsPngFile(byte[] fileBuffer)
        {
            if (fileBuffer.Length < 8)
                return false;

            byte[] magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return magic.SequenceEqual(fileBuffer[0..8]);
        }

        private static bool IsPsdFile(byte[] fileBuffer)
        {
            return fileBuffer.Length > 4 &&
                Encoding.ASCII.GetString(fileBuffer[0..4]) == "8BPS";
        }

        public static FileType PathToFileType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".dat" && filePath.Contains("glass"))
            {
                return FileType.Sui;
            }
            return ExtensionToFileType(extension);
        }

        public static FileType ExtensionToFileType(string extension)
        {
            return extension switch
            {
                ".bank" => FileType.SoundBank,
                ".dds" => FileType.Dds,
                ".font" => FileType.Font,
                ".guids" => FileType.SoundBankGuids,
                ".bank.guids" => FileType.SoundBankGuids,
                ".ini" => FileType.Ini,
                ".jpg" => FileType.Jpeg,
                ".mask" => FileType.TgaMask,
                ".mat" => FileType.Material,
                ".pdn" => FileType.Pdn,
                ".pma" => FileType.Pma,
                ".pmc" => FileType.Pmc,
                ".pmd" => FileType.Pmd,
                ".pmg" => FileType.Pmg,
                ".png" => FileType.Png,
                ".ppd" => FileType.Ppd,
                ".psd" => FileType.Psd,
                ".sii" => FileType.Sii,
                ".soundref" => FileType.SoundRef,
                ".sui" => FileType.Sui,
                ".tobj" => FileType.Tobj,
                _ => FileType.Unknown,
            };
        }

        public static string FileTypeToExtension(FileType fileType)
        {
            return fileType switch
            {
                FileType.SoundBank => ".bank",
                FileType.Dds => ".dds",
                FileType.Font => ".font",
                FileType.Ini => ".ini",
                FileType.SoundBankGuids => ".bank.guids",
                FileType.Jpeg => ".jpg",
                FileType.TgaMask => ".mask",
                FileType.Material => ".mat",
                FileType.Pdn => ".pdn",
                FileType.Pma => ".pma",
                FileType.Pmc => ".pmc",
                FileType.Pmd => ".pmd",
                FileType.Pmg => ".pmg",
                FileType.Png => ".png",
                FileType.Ppd => ".ppd",
                FileType.Psd => ".psd",
                FileType.Sii => ".sii",
                FileType.SoundRef => ".soundref",
                FileType.Sui => ".sui",
                FileType.Tobj => ".tobj",
                _ => string.Empty,
            };
        }
    }
}
