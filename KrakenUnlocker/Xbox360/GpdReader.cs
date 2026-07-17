using System.IO;
using System.Text;

namespace KrakenUnlocker.Xbox360
{
    public class GpdAchievement
    {
        public uint   Id           { get; set; }
        public string Name         { get; set; } = "";
        public string Description  { get; set; } = "";
        public int    Gamerscore   { get; set; }
        public bool   IsUnlocked   { get; set; }
        public bool   IsSecret     { get; set; }
        public string ImagePath    { get; set; } = "";
        public int    EntryOffset  { get; set; }
    }

    public static class GpdReader
    {
        // GPD is an XDBF file
        // Header: magic XDBF (0x58444246), version, entry_count, entry_alloc, free_block_count, free_block_alloc
        // Entries: namespace (2), id (8), offset (4), length (4)
        // Namespace 1 = Achievements, 2 = Images, 3 = Settings, 4 = Title, 5 = String

        private const uint XdbfMagic = 0x58444246;
        private const int  EntrySize = 18;
        private const int  HeaderSize = 24;

        public static List<GpdAchievement> ReadAchievements(string path)
        {
            var list = new List<GpdAchievement>();
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            // Validate magic
            uint magic = ReadUInt32BE(br);
            if (magic != XdbfMagic) throw new InvalidDataException("Not a valid GPD/XDBF file.");

            uint version    = ReadUInt32BE(br);
            uint entryCount = ReadUInt32BE(br);
            uint entryAlloc = ReadUInt32BE(br);
            uint freeCount  = ReadUInt32BE(br);
            uint freeAlloc  = ReadUInt32BE(br);

            long specTableOffset = HeaderSize;
            long freeSpaceOffset = specTableOffset + entryAlloc * EntrySize;
            long dataOffset      = freeSpaceOffset + freeAlloc * 8;

            // Read entry table
            for (int i = 0; i < entryCount; i++)
            {
                fs.Seek(specTableOffset + i * EntrySize, SeekOrigin.Begin);
                ushort ns     = ReadUInt16BE(br);
                ulong  id     = ReadUInt64BE(br);
                uint   offset = ReadUInt32BE(br);
                uint   length = ReadUInt32BE(br);

                // Namespace 1 = Achievement entries
                if (ns == 1 && id != 0x200000000 && length > 0)
                {
                    long entryAddr = dataOffset + offset;
                    if (entryAddr + length > fs.Length) continue;
                    fs.Seek(entryAddr, SeekOrigin.Begin);
                    var ach = ParseAchievementEntry(br, (int)id, (int)entryAddr);
                    if (ach != null) list.Add(ach);
                }
            }

            // Get strings for names/descriptions
            var strings = ReadStrings(fs, br, specTableOffset, dataOffset, entryCount, entryAlloc);
            foreach (var a in list)
            {
                if (strings.TryGetValue(((ulong)a.Id << 32) | 0x00010000, out var name))
                    a.Name = name;
                if (strings.TryGetValue(((ulong)a.Id << 32) | 0x00020000, out var desc))
                    a.Description = desc;
            }

            return list;
        }

        private static GpdAchievement? ParseAchievementEntry(BinaryReader br, int id, int offset)
        {
            try
            {
                uint structSize  = ReadUInt32BE(br);
                uint achId       = ReadUInt32BE(br);
                uint imageId     = ReadUInt32BE(br);
                uint gamerscore  = ReadUInt32BE(br);
                uint flags       = ReadUInt32BE(br);

                bool isUnlocked = (flags & 0x00000001) != 0 || (flags & 0x40000000) != 0;
                bool isSecret   = (flags & 0x00000004) != 0;

                return new GpdAchievement
                {
                    Id          = achId == 0 ? (uint)id : achId,
                    Gamerscore  = (int)gamerscore,
                    IsUnlocked  = isUnlocked,
                    IsSecret    = isSecret,
                    EntryOffset = offset
                };
            }
            catch { return null; }
        }

        private static Dictionary<ulong, string> ReadStrings(
            FileStream fs, BinaryReader br,
            long specTableOffset, long dataOffset,
            uint entryCount, uint entryAlloc)
        {
            var dict = new Dictionary<ulong, string>();
            for (int i = 0; i < entryCount; i++)
            {
                fs.Seek(specTableOffset + i * EntrySize, SeekOrigin.Begin);
                ushort ns     = ReadUInt16BE(br);
                ulong  id     = ReadUInt64BE(br);
                uint   offset = ReadUInt32BE(br);
                uint   length = ReadUInt32BE(br);

                if (ns == 5 && length > 0) // String namespace
                {
                    long addr = dataOffset + offset;
                    if (addr + length > fs.Length) continue;
                    fs.Seek(addr, SeekOrigin.Begin);
                    byte[] strBytes = br.ReadBytes((int)length);
                    // UTF-16BE or null-terminated ASCII
                    string s = length > 1 && strBytes[1] == 0
                        ? Encoding.Unicode.GetString(strBytes).TrimEnd('\0')
                        : Encoding.UTF8.GetString(strBytes).TrimEnd('\0');
                    dict[id] = s;
                }
            }
            return dict;
        }

        public static void SetUnlocked(string path, GpdAchievement ach, bool unlocked)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            using var br = new BinaryReader(fs);
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            uint magic = ReadUInt32BE(br);
            if (magic != XdbfMagic) throw new InvalidDataException("Not a valid GPD/XDBF file.");

            uint version    = ReadUInt32BE(br);
            uint entryCount = ReadUInt32BE(br);
            uint entryAlloc = ReadUInt32BE(br);
            uint freeCount  = ReadUInt32BE(br);
            uint freeAlloc  = ReadUInt32BE(br);

            long specTableOffset = HeaderSize;
            long freeSpaceOffset = specTableOffset + entryAlloc * EntrySize;
            long dataOffset      = freeSpaceOffset + freeAlloc * 8;

            for (int i = 0; i < entryCount; i++)
            {
                fs.Seek(specTableOffset + i * EntrySize, SeekOrigin.Begin);
                ushort ns     = ReadUInt16BE(br);
                ulong  id     = ReadUInt64BE(br);
                uint   offset = ReadUInt32BE(br);
                uint   length = ReadUInt32BE(br);

                if (ns == 1 && (uint)id == ach.Id)
                {
                    long entryAddr = dataOffset + offset + 16; // flags at offset 16
                    fs.Seek(entryAddr, SeekOrigin.Begin);
                    uint flags = ReadUInt32BE(br);
                    if (unlocked)
                        flags |= 0x40000000;
                    else
                        flags &= ~0x40000000u;
                    fs.Seek(entryAddr, SeekOrigin.Begin);
                    WriteUInt32BE(bw, flags);
                    fs.Flush();
                    return;
                }
            }
        }

        private static uint   ReadUInt32BE(BinaryReader br) { var b = br.ReadBytes(4); return (uint)((b[0]<<24)|(b[1]<<16)|(b[2]<<8)|b[3]); }
        private static ushort ReadUInt16BE(BinaryReader br) { var b = br.ReadBytes(2); return (ushort)((b[0]<<8)|b[1]); }
        private static ulong  ReadUInt64BE(BinaryReader br) { var b = br.ReadBytes(8); ulong v=0; for(int i=0;i<8;i++) v=(v<<8)|b[i]; return v; }
        private static void   WriteUInt32BE(BinaryWriter bw, uint v) { bw.Write(new[]{(byte)(v>>24),(byte)(v>>16),(byte)(v>>8),(byte)v}); }
    }
}
