﻿using Henke37.IOUtils;
using Henke37.Nitro.Composer;
using Henke37.Nitro.Composer.Player.Decoders;
using System;
using System.Collections.Generic;
using System.IO;

namespace Henke37.Nitro.Composer.Player {
	/* Stream player included in the same assembly for convenience.
	 * You will most likely want support for both. */
	public class StreamPlayer : BasePlayer {

		private STRM strm;

		private BaseSampleDecoder[] decoders;

		private int loadedBlock;
		private uint currentSamplePos;
		private uint samplesLeftInBlock;

		public StreamPlayer(SDat sdat, string streamName) {
			var strm = sdat.OpenStream(streamName);
			Load(strm);
		}

		public StreamPlayer(SDat sdat, int streamIndex) {
			Load(sdat, streamIndex);
		}

		public StreamPlayer(STRM strm) {
			Load(strm);
		}

		private void Load(SDat sdat, int streamIndex) {
			var strm=sdat.OpenStream(streamIndex);
			Load(strm);
		}

		private void Load(STRM strm) {
			this.strm = strm;

			Reset();

			decoders = new BaseSampleDecoder[strm.channels];
			for(int channel=0;channel<strm.channels;++channel) {
				decoders[channel] = BaseSampleDecoder.CreateDecoder(strm.encoding);
			}
		}

		private void Reset() {
			loadedBlock = -1;
			currentSamplePos = 0;
		}

		public override int SampleRate { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		public override void GenerateSamples(SamplePair[] samples) {
			if(strm.channels==2) {
				GenerateStereoSamples(samples);
			} else {
				GenerateMonoSamples(samples);
			}
		}

		private void GenerateMonoSamples(SamplePair[] samples) {
			throw new NotImplementedException();
		}

		private void GenerateStereoSamples(SamplePair[] samples) {
			int sampleIndex = 0;

			while(sampleIndex < samples.Length) {
				//ensure that the correct block is loaded
				uint targetSamplePos = currentSamplePos;
				int targetBlock = (int)(targetSamplePos / strm.blockSamples);
				if(loadedBlock != targetBlock) {
					LoadBlock(targetBlock);
					currentSamplePos = targetSamplePos;
				}
				//fastforward the block if needed
				while(currentSamplePos < targetSamplePos) {
					foreach(var decoder in decoders) {
						decoder.IncrementSample();
					}
					currentSamplePos++;
				}
				//decode the samples
				for(; sampleIndex < samples.Length; ++sampleIndex) {
					if(samplesLeftInBlock <= 0) break;
					
					samples[sampleIndex] = new SamplePair(
						decoders[0].GetSample(), 
						decoders[1].GetSample()
					);
				}
			}
		}

		private void LoadBlock(int blockId) {
			for(int channel=0;channel<strm.channels;++channel) {
				var decoder = decoders[channel];
				decoder.Init(new BinaryReader(GetBlockStream(blockId, channel)));
			}
			loadedBlock = blockId;

			bool lastBlock = strm.nBlock < blockId;
			samplesLeftInBlock = lastBlock?strm.lastBlockSamples:strm.blockSamples;
		}

		private Stream GetBlockStream(int blockId, int channel) {
			bool lastBlock = strm.nBlock < blockId;
			long blockLen = lastBlock ? strm.lastBlockLength : strm.blockLength;
			long offset = strm.blockLength * strm.channels * blockId + blockLen * channel;

			return new SubStream(strm.dataStream, offset, blockLen);
		}
	}
}
