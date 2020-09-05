﻿using System.IO;
using System.Text;
using BMAPI.v1;
using OsuMissAnalyzer.Core;

namespace OsuMissAnalyzer.UI
{
    public class OsuDatabase : BinaryReader
    {
        private string databaseFile;
        public OsuDatabase(string osudir, string file)
            : base(new FileStream(Path.Combine(osudir, file), FileMode.Open))
        {
            databaseFile = file;
        }
        public Beatmap GetBeatmap(string songsFolder, string mapHash)
        {
            BaseStream.Seek(0, SeekOrigin.Begin);
            uint version = ReadUInt32();
            Skip(13);
            SkipULEBString();
            uint num = ReadUInt32();
            for(uint i = 0; i < num; i++)
            {
                if(version < 20191106) Skip(4);
                for(int j = 0; j < 7; j++)
                {
                    SkipULEBString();
                }
                string hash = ReadULEBString();
                string file = ReadULEBString();
                Skip(39);
                for(int j = 0; j < 4; j++)
                {
                    Skip(14 * ReadInt32());
                }
                Skip(12);
                Skip(17 * ReadInt32());
                Skip(22);
                int mode = ReadByte();
                SkipULEBString();
                SkipULEBString();
                Skip(2);
                SkipULEBString();
                Skip(10);
                string folder = ReadULEBString();
                if (mode == 0 && hash == mapHash)
                {
                    return new Beatmap(Path.Combine(songsFolder, folder, file));
                }
                Skip(18);
            }
            return null;
        }
        private string ReadULEBString()
        {
            if (ReadByte() == 0) return "";
            int l = Read7BitEncodedInt();
            return Encoding.UTF8.GetString(ReadBytes(l));
        }
        private void SkipULEBString ()
        {
            if (ReadByte() == 0) return;
            int l = Read7BitEncodedInt();
            Skip(l);
        }
        private void Skip(int i)
        {
            ReadBytes(i);
        }
    }
}
