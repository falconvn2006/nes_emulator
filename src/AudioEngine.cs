using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using OpenTK.Audio.OpenAL;

namespace nes_emulator.src
{
	public static class SoundEngine
	{
		public class AudioSample
		{
			public float[] Samples;
			public int NumChannels;
			public int NumSamples;
			public bool IsValid;

			public AudioSample() { }

			public AudioSample(string wavFile)
			{
				LoadFromFile(wavFile);
			}

			public bool LoadFromFile(string wavFile)
			{
				try
				{
					using (var reader = new BinaryReader(File.OpenRead(wavFile)))
					{
						// Basic RIFF WAVE parser (16-bit PCM/PCM only, stereo/mono)
						// Read RIFF header
						if (new string(reader.ReadChars(4)) != "RIFF") throw new Exception("Not RIFF");
						reader.ReadInt32(); // File size
						if (new string(reader.ReadChars(4)) != "WAVE") throw new Exception("Not WAVE");

						// Read fmt chunk
						if (new string(reader.ReadChars(4)) != "fmt ") throw new Exception("Missing fmt");
						int fmtLength = reader.ReadInt32();
						int audioFormat = reader.ReadInt16();
						int numChannels = reader.ReadInt16();
						int sampleRate = reader.ReadInt32();
						int byteRate = reader.ReadInt32();
						int blockAlign = reader.ReadInt16();
						int bitsPerSample = reader.ReadInt16();
						if (fmtLength > 16) reader.ReadBytes(fmtLength - 16); // skip extra fmt bytes

						// Find data chunk
						string chunkId = new string(reader.ReadChars(4));
						int dataSize = reader.ReadInt32();
						while (chunkId != "data")
						{
							reader.ReadBytes(dataSize);
							chunkId = new string(reader.ReadChars(4));
							dataSize = reader.ReadInt32();
						}

						// Now read PCM data
						int numSamples = dataSize / (bitsPerSample / 8) / numChannels;
						float[] samples = new float[numSamples * numChannels];
						for (int i = 0; i < numSamples * numChannels; i++)
						{
							if (bitsPerSample == 16)
								samples[i] = reader.ReadInt16() / 32768.0f;
							else if (bitsPerSample == 8)
								samples[i] = (reader.ReadByte() - 128) / 128.0f;
							else
								throw new Exception("Unsupported bits per sample");
						}

						Samples = samples;
						NumChannels = numChannels;
						NumSamples = numSamples;
						IsValid = true;
						return true;
					}
				}
				catch
				{
					IsValid = false;
					return false;
				}
			}
		}

		private class PlayingSample
		{
			public int SampleId;
			public long Position;
			public bool Loop;
			public bool Finished;
			public bool FlagForStop;
		}

		private static List<AudioSample> audioSamples = new List<AudioSample>();
		private static List<PlayingSample> activeSamples = new List<PlayingSample>();
		private static Thread soundThread;
		private static bool soundThreadActive = false;
		private static uint sampleRate = 44100;
		private static uint channels = 1;
		private static uint blockCount = 8;
		private static uint blockSamples = 512;

		// OpenAL related
		private static ALDevice device;
		private static ALContext context;
		private static Queue<uint> availableBuffers = new Queue<uint>();
		private static int[] bufferIds;
		private static int sourceId;
		private static short[] blockMemory;

		public delegate float SynthFunction(int channel, float globalTime, float timeStep);
		public static SynthFunction UserSynthFunction;
		public static SynthFunction UserFilterFunction;

		public static bool InitialiseAudio(uint nSampleRate = 44100, uint nChannels = 1, uint nBlocks = 8, uint nBlockSamples = 512)
		{
			sampleRate = nSampleRate;
			channels = nChannels;
			blockCount = nBlocks;
			blockSamples = nBlockSamples;

			// Open OpenAL device/context
			device = ALC.OpenDevice(null);
			if (device == IntPtr.Zero)
				throw new Exception("Failed to open OpenAL device");
			context = ALC.CreateContext(device, (int[])null);
			if (context == IntPtr.Zero)
				throw new Exception("Failed to create OpenAL context");
			ALC.MakeContextCurrent(context);

			// Generate buffers and source
			bufferIds = new int[blockCount];
			AL.GenBuffers((int)blockCount, buffers: bufferIds);
			sourceId = AL.GenSource();
			foreach (int bid in bufferIds)
				availableBuffers.Enqueue((uint)bid);

			blockMemory = new short[blockSamples * channels];

			soundThreadActive = true;
			soundThread = new Thread(AudioThread) { IsBackground = true };
			soundThread.Start();
			return true;
		}

		public static bool DestroyAudio()
		{
			soundThreadActive = false;
			soundThread?.Join();

			AL.DeleteSource(sourceId);
			AL.DeleteBuffers(bufferIds.Length, bufferIds);
			ALC.DestroyContext(context);
			ALC.CloseDevice(device);

			return true;
		}

		public static void SetUserSynthFunction(SynthFunction func) => UserSynthFunction = func;
		public static void SetUserFilterFunction(SynthFunction func) => UserFilterFunction = func;

		public static int LoadAudioSample(string wavFile)
		{
			var sample = new AudioSample(wavFile);
			if (sample.IsValid)
			{
				audioSamples.Add(sample);
				return audioSamples.Count; // 1-based index
			}
			return -1;
		}

		public static void PlaySample(int id, bool loop = false)
		{
			if (id <= 0 || id > audioSamples.Count) return;
			activeSamples.Add(new PlayingSample
			{
				SampleId = id,
				Position = 0,
				Loop = loop,
				Finished = false,
				FlagForStop = false
			});
		}

		public static void StopSample(int id)
		{
			foreach (var s in activeSamples)
				if (s.SampleId == id)
					s.FlagForStop = true;
		}

		public static void StopAll()
		{
			foreach (var s in activeSamples)
				s.FlagForStop = true;
		}

		public static float GetMixerOutput(int channel, float globalTime, float timeStep)
		{
			float mixerSample = 0.0f;
			for (int i = 0; i < activeSamples.Count; i++)
			{
				var s = activeSamples[i];
				if (s.FlagForStop)
				{
					s.Loop = false;
					s.Finished = true;
				}
				else if (!s.Finished)
				{
					var sample = audioSamples[s.SampleId - 1];
					if (s.Position < sample.NumSamples)
					{
						mixerSample += sample.Samples[s.Position * sample.NumChannels + channel];
						s.Position++;
					}
					else if (s.Loop)
					{
						s.Position = 0;
					}
					else
					{
						s.Finished = true;
					}
				}
			}
			activeSamples.RemoveAll(s => s.Finished);

			if (UserSynthFunction != null)
				mixerSample += UserSynthFunction(channel, globalTime, timeStep);

			if (UserFilterFunction != null)
				return UserFilterFunction(channel, globalTime, mixerSample);

			return mixerSample;
		}

		private static void AudioThread()
		{
			float globalTime = 0.0f;
			float timeStep = 1.0f / sampleRate;

			var processedBufs = new int[blockCount];

			while (soundThreadActive)
			{
				// Unqueue processed buffers
				AL.GetSource(sourceId, ALGetSourcei.BuffersProcessed, out int processed);
				if (processed > 0)
				{
					AL.SourceUnqueueBuffers(sourceId, processed, processedBufs);
					for (int i = 0; i < processed; i++)
						availableBuffers.Enqueue((uint)processedBufs[i]);
				}

				// Wait for a free buffer
				if (availableBuffers.Count == 0)
				{
					Thread.Sleep(2);
					continue;
				}

				// Fill block
				for (int n = 0; n < blockSamples; n++)
					for (int c = 0; c < channels; c++)
					{
						float sample = GetMixerOutput(c, globalTime, timeStep);
						sample = Math.Max(-1.0f, Math.Min(1.0f, sample));
						blockMemory[n * channels + c] = (short)(sample * short.MaxValue);
					}
				globalTime += timeStep * blockSamples;

				// Queue buffer
				int buf = (int)availableBuffers.Dequeue();
				int size = (int)(blockSamples * channels * sizeof(short));
				ALFormat format = (channels == 1) ? ALFormat.Mono16 : ALFormat.Stereo16;
				unsafe
				{
					fixed(short* ptr = blockMemory)
					{
						AL.BufferData(buf, format, ptr, size, (int)sampleRate);
					}
				}
				AL.SourceQueueBuffer(sourceId, buf);

				// If not playing, start
				AL.GetSource(sourceId, ALGetSourcei.SourceState, out int state);
				if ((ALSourceState)state != ALSourceState.Playing)
					AL.SourcePlay(sourceId);
			}
		}
	}
}