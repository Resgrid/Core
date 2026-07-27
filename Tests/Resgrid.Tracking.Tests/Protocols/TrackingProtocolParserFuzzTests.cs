using System;
using System.Buffers;
using System.Buffers.Binary;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using Resgrid.Providers.Tracking.Protocols.Queclink;
using Resgrid.Providers.Tracking.Protocols.Teltonika;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class TrackingProtocolParserFuzzTests
	{
		private const string IterationsEnvironmentVariable =
			"RESGRID_TRACKING_FUZZ_ITERATIONS";
		private const int DefaultIterations = 500;
		private const int MaximumIterations = 50000;
		private const int MaximumInputBytes = 4096;
		private const int MaximumFrameBytes = 65536;

		[Test]
		public void Parse_TeltonikaTcpBoundedRandomPackets_DoesNotEscapeContract()
		{
			Run(
				iteration => CreateSession(
					new TeltonikaCodec8ProtocolModule(),
					TrackingSocketTransport.Tcp,
					iteration),
				seed: 72001,
				ShapeTeltonikaTcp);
		}

		[Test]
		public void Parse_TeltonikaUdpBoundedRandomPackets_DoesNotEscapeContract()
		{
			Run(
				iteration => CreateSession(
					new TeltonikaCodec8ProtocolModule(),
					TrackingSocketTransport.Udp,
					iteration),
				seed: 72002,
				ShapeTeltonikaUdp);
		}

		[Test]
		public void Parse_QueclinkBoundedRandomPackets_DoesNotEscapeContract()
		{
			Run(
				iteration => CreateSession(
					new QueclinkProtocolModule(),
					TrackingSocketTransport.Tcp,
					iteration),
				seed: 72003,
				ShapeQueclink);
		}

		[Test]
		public void Parse_Gt06BoundedRandomPackets_DoesNotEscapeContract()
		{
			Run(
				iteration => CreateSession(
					new Gt06ProtocolModule(),
					TrackingSocketTransport.Tcp,
					iteration),
				seed: 72004,
				ShapeGt06);
		}

		private static void Run(
			Func<int, ITrackingProtocolSession> sessionFactory,
			int seed,
			Action<byte[], int> shape)
		{
			var random = new Random(seed);
			var iterations = GetIterationCount();
			for (var iteration = 0;
				 iteration < iterations;
				 iteration++)
			{
				var payload = new byte[
					random.Next(1, MaximumInputBytes + 1)];
				random.NextBytes(payload);
				if ((iteration & 1) == 1)
					shape(payload, iteration);

				var input =
					new ReadOnlySequence<byte>(payload);
				ProtocolParseResult result;
				try
				{
					result = sessionFactory(iteration)
						.Parse(ref input);
				}
				catch (Exception ex)
				{
					Assert.Fail(
						$"Parser escaped its contract at iteration {iteration}: {ex}");
					return;
				}

				result.Should().NotBeNull(
					$"iteration {iteration} must return a parse result");
				Enum.IsDefined(result.Status).Should().BeTrue(
					$"iteration {iteration} must return a known status");
				AssertSequenceBounds(
					input,
					result,
					iteration);
			}
		}

		private static ITrackingProtocolSession CreateSession(
			ITrackingProtocolModule module,
			TrackingSocketTransport transport,
			int iteration)
		{
			return module.CreateSession(
				new TrackingSessionContext
				{
					SessionId = $"fuzz-{module.ProtocolKey}-{iteration}",
					Transport = transport,
					ConnectedOnUtc = DateTime.UtcNow,
					MaxFrameBytes = MaximumFrameBytes
				});
		}

		private static void AssertSequenceBounds(
			ReadOnlySequence<byte> input,
			ProtocolParseResult result,
			int iteration)
		{
			long consumed;
			long examined;
			try
			{
				consumed = input.Slice(
						0,
						result.Consumed)
					.Length;
				examined = input.Slice(
						0,
						result.Examined)
					.Length;
			}
			catch (ArgumentOutOfRangeException ex)
			{
				Assert.Fail(
					$"Parser returned an out-of-range sequence position at iteration {iteration}: {ex}");
				return;
			}

			consumed.Should().BeGreaterThanOrEqualTo(0);
			examined.Should().BeGreaterThanOrEqualTo(consumed);
			examined.Should().BeLessThanOrEqualTo(input.Length);
		}

		private static int GetIterationCount()
		{
			var configured = Environment.GetEnvironmentVariable(
				IterationsEnvironmentVariable);
			return int.TryParse(configured, out var iterations) &&
				   iterations > 0
				? Math.Min(iterations, MaximumIterations)
				: DefaultIterations;
		}

		private static void ShapeTeltonikaTcp(
			byte[] payload,
			int iteration)
		{
			if (payload.Length < 12)
				return;

			payload.AsSpan(0, 4).Clear();
			BinaryPrimitives.WriteUInt32BigEndian(
				payload.AsSpan(4, 4),
				(uint)(payload.Length - 12));
			payload[8] = (iteration & 2) == 0
				? (byte)0x08
				: (byte)0x8E;
		}

		private static void ShapeTeltonikaUdp(
			byte[] payload,
			int iteration)
		{
			if (payload.Length < 24)
				return;

			BinaryPrimitives.WriteUInt16BigEndian(
				payload,
				checked((ushort)(payload.Length - 2)));
			payload[4] = 0x01;
			payload[5] = (byte)(iteration % 256);
			BinaryPrimitives.WriteUInt16BigEndian(
				payload.AsSpan(6, 2),
				15);
			for (var index = 0; index < 15; index++)
				payload[8 + index] = (byte)('0' + ((iteration + index) % 10));
		}

		private static void ShapeQueclink(
			byte[] payload,
			int iteration)
		{
			if (payload.Length < 8)
				return;

			for (var index = 0;
				 index < payload.Length;
				 index++)
			{
				payload[index] =
					(byte)(' ' + (payload[index] % 95));
			}

			"+RESP:"u8.CopyTo(payload);
			payload[^1] = (byte)'$';
			if ((iteration & 2) == 0)
				payload[6] = (byte)'G';
		}

		private static void ShapeGt06(
			byte[] payload,
			int iteration)
		{
			if (payload.Length < 10)
				return;

			payload[0] = 0x78;
			payload[1] = 0x78;
			payload[2] = checked((byte)Math.Min(
				byte.MaxValue,
				payload.Length - 5));
			payload[3] = (iteration & 2) == 0
				? (byte)0x22
				: (byte)0x16;
			payload[^2] = 0x0D;
			payload[^1] = 0x0A;
		}
	}
}
