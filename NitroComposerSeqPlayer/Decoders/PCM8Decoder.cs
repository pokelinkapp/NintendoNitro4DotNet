﻿using HenkesUtils;
using System.IO;

namespace NitroComposerPlayer.Decoders {
	internal class PCM8Decoder : BaseSampleDecoder {
		public override void Init(BinaryReader reader) {
			this.reader = reader;
		}

		internal override int GetSample(uint samplePosition) {
			reader.Seek((int)samplePosition);
			var b = reader.ReadByte();
			return b | (b<<8);
		}
	}
}