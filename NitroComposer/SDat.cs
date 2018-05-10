﻿using HenkesUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NitroComposer {
    public class SDat {
        private static readonly byte[] SIGNATURE_SDAT = { (byte)'S', (byte)'D', (byte)'A', (byte)'T' };
        private static readonly byte[] SIGNATURE_FAT = { (byte)'F', (byte)'A', (byte)'T', (byte)' ' };
        private static readonly byte[] SIGNATURE_INFO = { (byte)'I', (byte)'N', (byte)'F', (byte)'O' };
        private static readonly byte[] SIGNATURE_SYMB = { (byte)'S', (byte)'Y', (byte)'M', (byte)'B' };

        public static SDat Open(string filename) {
            return Open(File.OpenRead(filename));
        }

        public static SDat Open(Stream stream) {
            var sdat = new SDat();
            sdat.Parse(stream);
            return sdat;
        }

        private List<FATRecord> files;
        private Stream mainStream;

        public List<SequenceInfoRecord> sequenceInfo;
        public List<StreamInfoRecord> streamInfo;

        public List<string> seqSymbols;
        public List<string> bankSymbols;
        public List<string> waveArchiveSymbols;
        public List<string> playerSymbols;
        public List<string> groupSymbols;
        public List<string> streamPlayerSymbols;
        public List<string> streamSymbols;

        public SDat() {

        }

        private void Parse(Stream stream) {
            if(!stream.CanRead) throw new ArgumentException("Stream must be readable!", nameof(stream));
            if(!stream.CanSeek) throw new ArgumentException("Stream must be seekable!", nameof(stream));
            mainStream = stream;

            using(var r = new BinaryReader(stream, Encoding.UTF8, true)) {
                var sig = r.ReadBytes(4);

                if(!sig.SequenceEqual(SIGNATURE_SDAT)) throw new InvalidDataException("SDAT signature is wrong");

                stream.Position = 14;
                var blockCount = r.ReadUInt16();
                var symbPos = r.ReadUInt32();
                var symbSize = r.ReadUInt32();
                var infoPos = r.ReadUInt32();
                var infoSize = r.ReadUInt32();
                var fatPos = r.ReadUInt32();
                var fatSize = r.ReadUInt32();

                files = parseFat(new SubStream(stream, fatPos, fatSize));
                parseInfo(new SubStream(stream, infoPos, infoSize));

                if(symbPos != 0) {
                    parseSymb(new SubStream(stream, symbPos, symbSize));
                }
            }
        }

        private void parseInfo(Stream stream) {
            using(var r = new BinaryReader(stream)) {
                var sig = r.ReadBytes(4);
                if(!sig.SequenceEqual(SIGNATURE_INFO)) throw new InvalidDataException("INFO signature is wrong");
                var internalSize = r.ReadUInt32();
                if(internalSize != stream.Length) throw new InvalidDataException("INFO block size is wrong!");

                const int subsectionCount = 8;
                var subSectionPositions = r.ReadUInt32Array(subsectionCount);

                List<UInt32> ReadInfoRecordPtrTable(int subsectionIndex) {
                    stream.Position = subSectionPositions[subsectionIndex];
                    var recordCount = r.ReadUInt32();
                    return r.ReadUInt32Array((int)recordCount);
                }

                using(var subReader = new BinaryReader(new SubStream(stream, 0))) {
                    List<uint> recordPositions = ReadInfoRecordPtrTable(0);
                    sequenceInfo = new List<SequenceInfoRecord>(recordPositions.Count);
                    foreach(var position in recordPositions) {
                        if(position == 0) {
                            sequenceInfo.Add(null);
                            continue;
                        }
                        subReader.BaseStream.Position = position;
                        var record = SequenceInfoRecord.Read(subReader);
                        sequenceInfo.Add(record);
                    }
                }


                using(var subStream = new SubStream(stream, 0)) {
                    List<uint> recordPositions = ReadInfoRecordPtrTable(7);
                    streamInfo = new List<StreamInfoRecord>(recordPositions.Count);
                    foreach(var position in recordPositions) {
                        if(position == 0) {
                            sequenceInfo.Add(null);
                            continue;
                        }
                        subStream.Position = position;
                        StreamInfoRecord record = StreamInfoRecord.Read(r);
                        streamInfo.Add(record);
                    }
                }
            }
        }

        private void parseSymb(SubStream symbStream) {
            using(var r = new BinaryReader(symbStream)) {
                var sig = r.ReadBytes(4);
                if(!sig.SequenceEqual(SIGNATURE_SYMB)) throw new InvalidDataException("SYMB signature is wrong");
                var internalSize = r.ReadUInt32();
                //if(internalSize != stream.Length) throw new InvalidDataException("SYMB block size is wrong!");

                var stringReader = new BinaryReader(new SubStream(symbStream, 0));

                List<string> parseSymbSubRec(SubStream subStream) {
                    using(var r2 = new BinaryReader(subStream)) {
                        var nameCount = r2.ReadUInt32();
                        var names = new List<string>((int)nameCount);
                        for(UInt32 nameIndex = 0; nameIndex < nameCount; ++nameIndex) {
                            subStream.Position = 4 + 4 * nameIndex;
                            var stringPos = r2.ReadUInt32();

                            if(stringPos == 0) {
                                names.Add(null);
                                continue;
                            }

                            stringReader.Seek((int)stringPos);
                            names.Add(stringReader.ReadNullTerminatedUTF8String());
                        }
                        return names;
                    }

                }

                seqSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                r.Skip(4);
                bankSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                waveArchiveSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                playerSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                groupSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                streamPlayerSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
                streamSymbols = parseSymbSubRec(new SubStream(symbStream, r.ReadUInt32()));
            }
        }

        private static List<FATRecord> parseFat(Stream stream) {
            using(var r = new BinaryReader(stream)) {
                var sig = r.ReadBytes(4);
                if(!sig.SequenceEqual(SIGNATURE_FAT)) throw new InvalidDataException("FAT signature is wrong");
                r.Skip(4);
                var numRecords = r.ReadInt32();

                List<FATRecord> files = new List<FATRecord>(numRecords);

                for(int recordIndex = 0; recordIndex < numRecords; ++recordIndex) {
                    var position = r.ReadUInt32();
                    var size = r.ReadUInt32();
                    files.Add(new FATRecord(position, size));
                    r.Skip(8);
                }

                return files;
            }
        }

        internal Stream OpenSubFile(int fatId) {
            var record = files[fatId];
            return new SubStream(mainStream, record.position, record.size);
        }

        public STRM OpenStream(string name) {
            int streamIndex = streamSymbols.IndexOf(name);
            if(streamIndex == -1) throw new KeyNotFoundException();
            return OpenStream(streamIndex);
        }

        public STRM OpenStream(int streamIndex) {
            var infoRecord = streamInfo[streamIndex];
            return new STRM(OpenSubFile(infoRecord.fatId));
        }

        private class FATRecord {
            internal UInt32 size;
            internal UInt32 position;

            internal FATRecord(UInt32 size, UInt32 position) {
                this.size = size;
                this.position = position;
            }
        }

        public class SequenceInfoRecord {
            public ushort fatId;
            public ushort bankId;
            public byte vol;
            public byte channelPriority;
            public byte playerPriority;
            public byte player;

            public static SequenceInfoRecord Read(BinaryReader r) {
                var record = new SequenceInfoRecord();
                record.fatId = r.ReadUInt16();
                r.Skip(2);
                record.bankId = r.ReadUInt16();
                record.vol = r.ReadByte();
                record.channelPriority = r.ReadByte();
                record.playerPriority = r.ReadByte();
                record.player = r.ReadByte();

                return record;
            }
        }

        public class StreamInfoRecord {
            public ushort fatId;
            public byte vol;
            public byte priority;
            public byte player;
            public bool forceStereo;

            public static StreamInfoRecord Read(BinaryReader r) {
                var record = new StreamInfoRecord();
                record.fatId = r.ReadUInt16();
                r.Skip(2);
                record.vol = r.ReadByte();
                record.priority = r.ReadByte();
                record.player = r.ReadByte();
                record.forceStereo = r.ReadBoolean();

                return record;
            }
        }
    }
}
