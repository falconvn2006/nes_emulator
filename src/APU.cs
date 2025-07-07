using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src
{
	public class APU
	{

		#region Structs
		public struct Sequencer
		{
			public uint Sequence = 0x00000000;
			public uint NewSequence = 0x00000000;
			public ushort Timer = 0x0000;
			public ushort Reload = 0x0000;
			public byte Output = 0x00;

			public Sequencer()
			{

			}

			public Sequencer(uint _sequence, ushort _timer, ushort _reload)
			{
				Sequence = _sequence;
				Timer = _timer;
				Reload = _reload;
			}

			public byte Clock(bool _enable, Action<uint> _funcMapip)
			{
				if(_enable)
				{
					Timer--;
					if(Timer == 0xFFFF)
					{
						Timer = (ushort)(Reload + 1);

						_funcMapip(Sequence);

						Output = (byte)(Sequence & 0x00000001);
					}
				}

				return Output;
			}
		}

		public struct LengthCounter
		{
			public byte Counter = 0x00;
			public LengthCounter() { }

			public byte Clock(bool _enable, bool _halt)
			{
				if (!_enable)
					Counter = 0;
				else
					if(Counter > 0 && !_halt)
						Counter--;

				return Counter;
			}
		}

		public struct Envelope
		{
			public bool Start = false;
			public bool Disable = false;
			public ushort DividerCount = 0;
			public ushort Volume = 0;
			public ushort Output = 0;
			public ushort DecayCount = 0;

			public Envelope() { }

			public void Clock(bool _loop)
			{
				if(!Start)
				{
					if (DividerCount == 0)
					{
						DividerCount = Volume;

						if (DecayCount == 0)
						{
							if (_loop)
							{
								DecayCount = 15;
							}
						}
						else
							DecayCount--;
					}
					else
						DividerCount--;
				}
				else
				{
					Start = false;
					DecayCount = 15;
					DividerCount = Volume;
				}

				if (Disable)
					Output = Volume;
				else
					Output = DecayCount;
			}
		}

		public struct OsculatorPulse
		{
			public double Frequency = 0;
			public double DutyCycle = 0;
			public double Amplitude = 1;
			public double Pi = Math.PI;
			public double Harmonics = 20;

			public OsculatorPulse()	{}

			public double Sample(double _t)
			{
				double a = 0;
				double b = 0;
				double p = DutyCycle * 2.0 * Pi;

				var approxSin = (double __t) =>
				{
					double j = __t * 0.15915;
					j = j - (int)j;
					return 20.785 * j * (j - 0.5) * (j - 1.0);
				};

				for(double n = 1; n < Harmonics; n++)
				{
					double c = n * Frequency * 2.0 * Pi * _t;
					a += -approxSin(c) / n;
					b += -approxSin(c - p * n) / n;
				}

				return (2.0 * Amplitude / Pi) * (a - b);
			}
		}

		public struct Sweeper
		{
			public bool Enabled = false;
			public bool Down = false;
			public bool Reload = false;
			public byte Shift = 0x00;
			public byte Timer = 0x00;
			public byte Period = 0x00;
			public ushort Change = 0;
			public bool Mute = false;

			public Sweeper() { }

			public void Track(ref ushort _target)
			{
				if(Enabled)
				{
					Change = (byte)(_target >> Shift);
					Mute = (_target < 8) || (_target > 0x7FF);
				}
			}

			public bool Clock(ref ushort _target, bool _channel)
			{
				bool changed = false;
				if(Timer == 0 && Enabled && Shift > 0 && !Mute)
				{
					if(_target >= 8 && Change < 0x07FF)
					{
						if (Down)
							_target -= (ushort)(Change - (_channel ? 1 : 0));
						else
							_target += Change;

						changed = true;
					}
				}

				//if(Enabled)
				{
					if (Timer == 0 || Reload)
					{
						Timer = Period;
						Reload = false;
					}
					else
						Timer--;

					Mute = (_target < 8) || (_target > 0x7FF);
				}

				return changed;
			}
		}
		#endregion

		// Square Wave Pulse Channel 1
		private bool pulse1_enable = false;
		private bool pulse1_halt = false;
		private double pulse1_sample = 0.0;
		private double pulse1_output = 0.0;

		private Sequencer pulse1Sequencer = new Sequencer();
		private OsculatorPulse pulse1Osculator = new OsculatorPulse();
		private Envelope pulse1Envelope = new Envelope();
		private LengthCounter pulse1LC = new LengthCounter();
		private Sweeper pulse1Sweeper = new Sweeper();

		// Square Wave Pulse Channel 2
		private bool pulse2_enable = false;
		private bool pulse2_halt = false;
		private double pulse2_sample = 0.0;
		private double pulse2_output = 0.0;

		private Sequencer pulse2Sequencer = new Sequencer();
		private OsculatorPulse pulse2Osculator = new OsculatorPulse();
		private Envelope pulse2Envelope = new Envelope();
		private LengthCounter pulse2LC = new LengthCounter();
		private Sweeper pulse2Sweeper = new Sweeper();

		// Noise Channel
		private bool noise_enable = false;
		private bool noise_halt = false;
		private double noise_sample = 0;
		private double noise_output = 0;
		private Envelope noiseEnvelope = new Envelope();
		private LengthCounter noiseLC = new LengthCounter();
		private Sequencer noiseSequencer = new Sequencer();

		private static byte[] length_table = new byte[]
		{
			10, 254, 20,  2, 40,  4, 80,  6,
			160,   8, 60, 10, 14, 12, 26, 14,
			12,  16, 24, 18, 48, 20, 96, 22,
			192,  24, 72, 26, 16, 28, 32, 30
		};

		private uint clockCounter = 0;
		private uint frameClockCounter = 0;

		private double globalTime;

		public APU() 
		{
			noiseSequencer.Sequence = 0xDBDB;
		}

		public void CPUWrite(ushort addr, byte data)
		{
			switch (addr)
			{
				case 0x4000:
					switch((data & 0xC0) >> 6)
					{
						case 0x00: pulse1Sequencer.NewSequence = 0b01000000; pulse1Osculator.DutyCycle = 0.125; break;
						case 0x01: pulse1Sequencer.NewSequence = 0b01100000; pulse1Osculator.DutyCycle = 0.250; break;
						case 0x02: pulse1Sequencer.NewSequence = 0b01111000; pulse1Osculator.DutyCycle = 0.500; break;
						case 0x03: pulse1Sequencer.NewSequence = 0b10011111; pulse1Osculator.DutyCycle = 0.750; break;
					}

					pulse1Sequencer.Sequence = pulse1Sequencer.NewSequence;
					pulse1_halt = (data & 0x20) != 0;
					pulse1Envelope.Volume = (ushort)(data & 0x0F);
					pulse1Envelope.Disable = (data & 0x10) != 0;
					break;

				case 0x4001:
					pulse1Sweeper.Enabled = (data & 0x80) != 0;
					pulse1Sweeper.Period = (byte)((data & 0x70) >> 4);
					pulse1Sweeper.Down = (data & 0x08) != 0;
					pulse1Sweeper.Shift = (byte)(data & 0x07);
					pulse1Sweeper.Reload = true;
					break;
				case 0x4002:
					pulse1Sequencer.Reload = (ushort)((pulse1Sequencer.Reload & 0xFF00) | data);
					break;
				case 0x4003:
					pulse1Sequencer.Reload = (ushort)((data & 0x07) << 8 | (pulse1Sequencer.Reload & 0x00FF));
					pulse1Sequencer.Timer = pulse1Sequencer.Reload;
					pulse1Sequencer.Sequence = pulse1Sequencer.NewSequence;
					pulse1LC.Counter = length_table[(data & 0xF8) >> 3];
					pulse1Envelope.Start = true;
					break;
				case 0x4004:
					switch ((data & 0xC0) >> 6)
					{
						case 0x00: pulse2Sequencer.NewSequence = 0b01000000; pulse2Osculator.DutyCycle = 0.125; break;
						case 0x01: pulse2Sequencer.NewSequence = 0b01100000; pulse2Osculator.DutyCycle = 0.250; break;
						case 0x02: pulse2Sequencer.NewSequence = 0b01111000; pulse2Osculator.DutyCycle = 0.500; break;
						case 0x03: pulse2Sequencer.NewSequence = 0b10011111; pulse2Osculator.DutyCycle = 0.750; break;
					}

					pulse2Sequencer.Sequence = pulse2Sequencer.NewSequence;
					pulse2_halt = (data & 0x20) != 0;
					pulse2Envelope.Volume = (ushort)(data & 0x0F);
					pulse2Envelope.Disable = (data & 0x10) != 0;
					break;
				case 0x4005:
					pulse2Sweeper.Enabled = (data & 0x80) != 0;
					pulse2Sweeper.Period = (byte)((data & 0x70) >> 4);
					pulse2Sweeper.Down = (data & 0x08) != 0;
					pulse2Sweeper.Shift = (byte)(data & 0x07);
					pulse2Sweeper.Reload = true;
					break;
				case 0x4006:
					pulse2Sequencer.Reload = (ushort)((pulse2Sequencer.Reload & 0xFF00) | data);
					break;
				case 0x4007:
					pulse2Sequencer.Reload = (ushort)((data & 0x07) << 8 | (pulse2Sequencer.Reload & 0x00FF));
					pulse2Sequencer.Timer = pulse2Sequencer.Reload;
					pulse2Sequencer.Sequence = pulse2Sequencer.NewSequence;
					pulse2LC.Counter = length_table[(data & 0xF8) >> 3];
					pulse2Envelope.Start = true;
					break;
				case 0x4008:
					break;
				case 0x400C:
					noiseEnvelope.Volume = (ushort)(data & 0x0F);
					noiseEnvelope.Disable = (data & 0x10) != 0;
					noise_halt = (data & 0x20) != 0;
					break;
				case 0x400E:
					switch (data & 0x0F)
					{
						case 0x00: noiseSequencer.Reload = 0; break;
						case 0x01: noiseSequencer.Reload = 4; break;
						case 0x02: noiseSequencer.Reload = 8; break;
						case 0x03: noiseSequencer.Reload = 16; break;
						case 0x04: noiseSequencer.Reload = 32; break;
						case 0x05: noiseSequencer.Reload = 64; break;
						case 0x06: noiseSequencer.Reload = 96; break;
						case 0x07: noiseSequencer.Reload = 128; break;
						case 0x08: noiseSequencer.Reload = 160; break;
						case 0x09: noiseSequencer.Reload = 202; break;
						case 0x0A: noiseSequencer.Reload = 254; break;
						case 0x0B: noiseSequencer.Reload = 380; break;
						case 0x0C: noiseSequencer.Reload = 508; break;
						case 0x0D: noiseSequencer.Reload = 1016; break;
						case 0x0E: noiseSequencer.Reload = 2034; break;
						case 0x0F: noiseSequencer.Reload = 4068; break;
					}
					break;
				case 0x4015:
					pulse1_enable = (data & 0x01) != 0;
					pulse2_enable = (data & 0x02) != 0;
					noise_enable = (data & 0x04) != 0;
					break;
				case 0x400F:
					pulse1Envelope.Start = true;
					pulse2Envelope.Start = true;
					noiseEnvelope.Start = true;
					noiseLC.Counter = length_table[(data & 0xF8) >> 3];
					break;
			}
		}

		public byte CPURead(ushort addr) 
		{ 
			return 0; 
		}

		public void Clock()
		{
			bool quarterFrameClock = false;
			bool halfFrameClock = false;

			globalTime += (0.3333333333 / 1789773);

			if(clockCounter % 6 == 0)
			{
				frameClockCounter++;

				// 4-Step Sequence Mode
				if (frameClockCounter == 3729)
				{
					quarterFrameClock = true;
				}

				if (frameClockCounter == 7457)
				{
					quarterFrameClock = true;
					halfFrameClock = true;
				}

				if (frameClockCounter == 11186)
				{
					quarterFrameClock = true;
				}

				if (frameClockCounter == 14916)
				{
					quarterFrameClock = true;
					halfFrameClock = true;
					frameClockCounter = 0;
				}

				// Update functional units ======================

				// Quarter frame "beats" adjust the volume envelope
				if (quarterFrameClock)
				{
					pulse1Envelope.Clock(pulse1_halt);
					pulse2Envelope.Clock(pulse2_halt);
					noiseEnvelope.Clock(noise_halt);
				}

				// Half frame "beats" adjust the note length and
				// frequency sweepers
				if(halfFrameClock)
				{
					pulse1LC.Clock(pulse1_enable, pulse1_halt);
					pulse2LC.Clock(pulse2_enable, pulse2_halt);
					noiseLC.Clock(noise_enable, noise_halt);
					pulse1Sweeper.Clock(ref pulse1Sequencer.Reload, false);
					pulse2Sweeper.Clock(ref pulse2Sequencer.Reload, true);
				}

				// Update Pulse1 Channel ==========================
				pulse1Sequencer.Clock(pulse1_enable, (uint s) =>
				{
					// Shift right by 1 bit, wrapping around
					s = (byte)(((s & 0x0001) << 7) | ((s & 0x00FE) >> 1));
				});

				pulse1Osculator.Frequency = 1789773.0 / (16.0 * (double)(pulse1Sequencer.Reload + 1));
				pulse1Osculator.Amplitude = (double)(pulse1Envelope.Output - 1) / 16.0;
				pulse1_sample = pulse1Osculator.Sample(globalTime);

				if (pulse1LC.Counter > 0 && pulse1Sequencer.Timer >= 8 && !pulse1Sweeper.Mute && pulse1Envelope.Output > 2)
					pulse1_output += (pulse1_sample - pulse1_output) * 0.5;
				else
					pulse1_output = 0;

				// Update Pulse2 Channel ==========================
				pulse2Sequencer.Clock(pulse2_enable, (uint s) =>
				{
					// Shift right by 1 bit, wrapping around
					s = (byte)(((s & 0x0001) << 7) | ((s & 0x00FE) >> 1));
				});

				pulse2Osculator.Frequency = 1789773.0 / (16.0 * (double)(pulse2Sequencer.Reload + 1));
				pulse2Osculator.Amplitude = (double)(pulse2Envelope.Output - 1) / 16.0;
				pulse2_sample = pulse2Osculator.Sample(globalTime);

				if (pulse2LC.Counter > 0 && pulse2Sequencer.Timer >= 8 && !pulse2Sweeper.Mute && pulse2Envelope.Output > 2)
					pulse2_output += (pulse2_sample - pulse2_output) * 0.5;
				else
					pulse2_output = 0;

				noiseSequencer.Clock(noise_enable, (uint s) =>
				{
					s = (((s & 0x0001) ^ ((s & 0x0002) >> 1)) << 14) | ((s & 0x7FFF) >> 1);
				});

				if (noiseLC.Counter > 0 && noiseSequencer.Timer >= 8)
					noise_output = (double)noiseSequencer.Output * ((double)(noiseEnvelope.Output - 1) / 16.0);

				if (!pulse1_enable) pulse1_output = 0;
				if (!pulse2_enable) pulse2_output = 0;
				if (!noise_enable) noise_output = 0;
			}

			// Frequency sweepers change at high frequency
			pulse1Sweeper.Track(ref pulse1Sequencer.Reload);
			pulse2Sweeper.Track(ref pulse2Sequencer.Reload);

			clockCounter++;
		}

		public void Reset()
		{

		}

		public double GetOutputSample()
		{
			return ((1.0 * pulse1_output) - 0.8) * 0.1 +
					((1.0 * pulse2_output) - 0.8) * 0.1 +
					(2.0 * (noise_output - 0.5)) * 0.1;
		}
	}
}
