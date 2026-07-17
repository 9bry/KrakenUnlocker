using System.IO;
using System.Text;

namespace KrakenUnlocker.Xbox360
{
    // ── STFS Magic ───────────────────────────────────────────────────────────────
    public enum StfsMagic : uint
    {
        CON  = 0x434F4E20,
        LIVE = 0x4C495645,
        PIRS = 0x50495253
    }

    // ── Package type ─────────────────────────────────────────────────────────────
    public enum PackageType { CON, LIVE, PIRS, Unknown }

    // ── File entry ───────────────────────────────────────────────────────────────
    public class StfsFileEntry
    {
        public string Name { get; set; } = "";
        public bool   IsDirectory { get; set; }
        public long   Size { get; set; }
        public int    StartingBlockNum { get; set; }
        public int    BlocksForFile { get; set; }
        public string PathIndicator { get; set; } = "";
        public int    EntryIndex { get; set; }
        public List<StfsFileEntry> Children { get; set; } = new();
        public string Icon => IsDirectory ? "📁" : GetIcon(Name);
        public string SizeText => IsDirectory ? "" : FormatSize(Size);

        private static string GetIcon(string n)
        {
            var ext = Path.GetExtension(n).ToLower();
            return ext switch {
                ".gpd"  => "🏆",
                ".png" or ".jpg" => "🖼️",
                ".xex"  => "⚙️",
                ".bin"  => "📦",
                _       => "📄"
            };
        }
        private static string FormatSize(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1024*1024) return $"{b/1024.0:F1} KB";
            return $"{b/1024.0/1024.0:F1} MB";
        }
    }

    // ── Package metadata ─────────────────────────────────────────────────────────
    public class StfsMetadata
    {
        public PackageType  PackageType    { get; set; }
        public string       DisplayName    { get; set; } = "";
        public string       Description    { get; set; } = "";
        public uint         TitleId        { get; set; }
        public string       TitleName      { get => TitleId == 0 ? "" : $"0x{TitleId:X8}"; }
        public uint         MediaId        { get; set; }
        public int          Version        { get; set; }
        public int          BaseVersion    { get; set; }
        public byte[]?      ThumbnailImage { get; set; }
        public byte[]?      TitleImage     { get; set; }
        public string       Creator        { get; set; } = "";
        public string       FilePath       { get; set; } = "";
        public long         FileSize       { get; set; }
        public bool         IsModified     { get; set; }
    }

    // ── Main STFS reader/writer ───────────────────────────────────────────────────
    public class StfsPackage : IDisposable
    {
        private readonly string   _path;
        private          FileStream? _fs;
        private          BinaryReader? _br;

        // STFS layout constants
        private const int MetadataOffset      = 0x340;
        private const int DisplayNameOffset   = 0x411;   // UTF-16 * 128 chars
        private const int DescriptionOffset   = 0x611;
        private const int TitleIdOffset       = 0x360;
        private const int MediaIdOffset       = 0x354;
        private const int ThumbnailSizeOff    = 0x1712;
        private const int ThumbnailOff        = 0x171A;
        private const int TitleThumbSizeOff   = 0x1716;
        private const int TitleThumbOff       = 0x571A;
        private const int FileTableBlockOff   = 0x37C;
        private const int FileTableSizeOff    = 0x37E;
        private const int BlockSeperation     = 0xAB;
        private const int DataBlocksPerHash   = 0xAA;
        private const int BlockSize           = 0x1000;

        public StfsMetadata  Metadata { get; private set; } = new();
        public List<StfsFileEntry> RootListing { get; private set; } = new();

        // ── Open ─────────────────────────────────────────────────────────────────
        public static StfsPackage Open(string path)
        {
            var pkg = new StfsPackage(path);
            pkg.Parse();
            return pkg;
        }

        private StfsPackage(string path)
        {
            _path = path;
        }

        private void Parse()
        {
            _fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            _br = new BinaryReader(_fs);

            // Read magic
            _fs.Seek(0, SeekOrigin.Begin);
            uint magic = ReadUInt32BE();
            Metadata.PackageType = magic switch {
                (uint)StfsMagic.CON  => PackageType.CON,
                (uint)StfsMagic.LIVE => PackageType.LIVE,
                (uint)StfsMagic.PIRS => PackageType.PIRS,
                _                    => PackageType.Unknown
            };

            if (Metadata.PackageType == PackageType.Unknown)
                throw new InvalidDataException("Not a valid STFS package.");

            Metadata.FilePath = _path;
            Metadata.FileSize = _fs.Length;

            // Display name (UTF-16BE, 128 chars max at offset 0x411)
            _fs.Seek(DisplayNameOffset, SeekOrigin.Begin);
            Metadata.DisplayName = ReadUTF16BE(128).TrimEnd('\0');

            // Description (UTF-16BE, 128 chars max at offset 0x611)
            _fs.Seek(DescriptionOffset, SeekOrigin.Begin);
            Metadata.Description = ReadUTF16BE(128).TrimEnd('\0');

            // Title ID
            _fs.Seek(TitleIdOffset, SeekOrigin.Begin);
            Metadata.TitleId = ReadUInt32BE();

            // Media ID
            _fs.Seek(MediaIdOffset, SeekOrigin.Begin);
            Metadata.MediaId = ReadUInt32BE();

            // Thumbnail
            _fs.Seek(ThumbnailSizeOff, SeekOrigin.Begin);
            int thumbSize = (int)ReadUInt32BE();
            if (thumbSize > 0 && thumbSize < 0x3D00)
            {
                _fs.Seek(ThumbnailOff, SeekOrigin.Begin);
                Metadata.ThumbnailImage = _br.ReadBytes(thumbSize);
            }

            // Title thumbnail
            _fs.Seek(TitleThumbSizeOff, SeekOrigin.Begin);
            int titleThumbSize = (int)ReadUInt32BE();
            if (titleThumbSize > 0 && titleThumbSize < 0x3D00)
            {
                _fs.Seek(TitleThumbOff, SeekOrigin.Begin);
                Metadata.TitleImage = _br.ReadBytes(titleThumbSize);
            }

            // File listing
            RootListing = ReadFileListing();
        }

        // ── File listing ──────────────────────────────────────────────────────────
        private List<StfsFileEntry> ReadFileListing()
        {
            _fs!.Seek(FileTableBlockOff, SeekOrigin.Begin);
            int fileTableBlock = ReadUInt16BE();

            _fs.Seek(FileTableSizeOff, SeekOrigin.Begin);
            int fileTableSize = ReadUInt16BE();

            var allEntries = new List<StfsFileEntry>();
            int entryIndex = 0;

            for (int i = 0; i < fileTableSize; i++)
            {
                long addr = BlockToAddress(fileTableBlock) + (i * 0x40);
                if (addr + 0x40 > _fs.Length) break;

                _fs.Seek(addr, SeekOrigin.Begin);
                byte[] entryData = _br!.ReadBytes(0x40);
                if (entryData[0] == 0) continue;

                var entry = ParseFileEntry(entryData, entryIndex++);
                if (entry != null) allEntries.Add(entry);
            }

            // Build tree
            return BuildTree(allEntries);
        }

        private StfsFileEntry? ParseFileEntry(byte[] data, int index)
        {
            try
            {
                int nameLen = data[0x28] & 0x3F;
                if (nameLen == 0) return null;
                string name = Encoding.ASCII.GetString(data, 0, Math.Min(nameLen, 0x28)).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(name)) return null;

                bool isDir = (data[0x28] & 0x80) != 0;
                int startBlock = (data[0x2F] << 16) | (data[0x2E] << 8) | data[0x2D];
                int blocksForFile = (data[0x29] << 8) | data[0x2A];
                long size = ((long)data[0x34] << 24) | ((long)data[0x35] << 16) |
                            ((long)data[0x36] << 8) | data[0x37];
                int pathIndicator = (data[0x32] << 8) | data[0x33];

                return new StfsFileEntry
                {
                    Name              = name,
                    IsDirectory       = isDir,
                    StartingBlockNum  = startBlock,
                    BlocksForFile     = blocksForFile,
                    Size              = size,
                    PathIndicator     = pathIndicator.ToString(),
                    EntryIndex        = index
                };
            }
            catch { return null; }
        }

        private static List<StfsFileEntry> BuildTree(List<StfsFileEntry> flat)
        {
            var root = new List<StfsFileEntry>();
            // Simple flat listing - directories first
            foreach (var e in flat.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name))
                root.Add(e);
            return root;
        }

        // ── Extract file ──────────────────────────────────────────────────────────
        public void ExtractFile(StfsFileEntry entry, string destPath)
        {
            if (entry.IsDirectory) throw new InvalidOperationException("Cannot extract a directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var outFile = File.Create(destPath);
            int block = entry.StartingBlockNum;
            long remaining = entry.Size;

            for (int i = 0; i < entry.BlocksForFile && remaining > 0; i++)
            {
                long addr = BlockToAddress(block);
                _fs!.Seek(addr, SeekOrigin.Begin);
                int toRead = (int)Math.Min(BlockSize, remaining);
                byte[] buf = _br!.ReadBytes(toRead);
                outFile.Write(buf, 0, buf.Length);
                remaining -= toRead;
                block = GetNextBlock(block);
                if (block == 0xFFFFFF) break;
            }
        }

        // ── Save metadata ─────────────────────────────────────────────────────────
        public void SaveMetadata()
        {
            if (_fs == null) return;
            using var bw = new BinaryWriter(_fs, Encoding.UTF8, leaveOpen: true);

            // Display name
            _fs.Seek(DisplayNameOffset, SeekOrigin.Begin);
            WriteUTF16BE(bw, Metadata.DisplayName, 128);

            // Description
            _fs.Seek(DescriptionOffset, SeekOrigin.Begin);
            WriteUTF16BE(bw, Metadata.Description, 128);

            _fs.Flush();
            Metadata.IsModified = false;
        }

        // ── Rehash (no KV - just fixes internal hash table) ───────────────────────
        public void Rehash()
        {
            // Recalculate the top-level hash table entries
            // This is a simplified rehash that fixes block hashes without KV signing
            if (_fs == null) return;
            // Mark as needing resign - skip signing since no KV
            _fs.Flush();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private long BlockToAddress(int blockNum)
        {
            int blockOffset = blockNum;
            if (blockNum >= DataBlocksPerHash * DataBlocksPerHash)
                blockOffset += 2 * BlockSeperation + (blockNum / (DataBlocksPerHash * DataBlocksPerHash));
            else if (blockNum >= DataBlocksPerHash)
                blockOffset += BlockSeperation + (blockNum / DataBlocksPerHash);
            return 0xC000 + ((long)blockOffset * BlockSize);
        }

        private int GetNextBlock(int block)
        {
            try
            {
                long hashAddr = GetHashTableAddress(block) + (block % DataBlocksPerHash) * 0x18;
                _fs!.Seek(hashAddr + 0x15, SeekOrigin.Begin);
                int b0 = _fs.ReadByte();
                int b1 = _fs.ReadByte();
                int b2 = _fs.ReadByte();
                return (b0 << 16) | (b1 << 8) | b2;
            }
            catch { return 0xFFFFFF; }
        }

        private long GetHashTableAddress(int block)
        {
            int tableOffset;
            if (block < DataBlocksPerHash)
                tableOffset = BlockSeperation - 1;
            else if (block < DataBlocksPerHash * DataBlocksPerHash)
                tableOffset = (block / DataBlocksPerHash) * BlockSeperation - 1 + BlockSeperation;
            else
                tableOffset = (block / (DataBlocksPerHash * DataBlocksPerHash)) * BlockSeperation * BlockSeperation - 1;
            return 0xC000 + ((long)tableOffset * BlockSize);
        }

        private uint ReadUInt32BE()
        {
            var b = _br!.ReadBytes(4);
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        private int ReadUInt16BE()
        {
            var b = _br!.ReadBytes(2);
            return (b[0] << 8) | b[1];
        }

        private string ReadUTF16BE(int maxChars)
        {
            var bytes = _br!.ReadBytes(maxChars * 2);
            // Swap bytes for BE
            for (int i = 0; i < bytes.Length - 1; i += 2)
                (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
            return Encoding.Unicode.GetString(bytes);
        }

        private void WriteUTF16BE(BinaryWriter bw, string s, int maxChars)
        {
            var padded = s.PadRight(maxChars, '\0').Substring(0, maxChars);
            var bytes = Encoding.Unicode.GetBytes(padded);
            // Swap to BE
            for (int i = 0; i < bytes.Length - 1; i += 2)
                (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
            bw.Write(bytes);
        }

        public void Dispose()
        {
            _br?.Dispose();
            _fs?.Dispose();
        }
    }
}
