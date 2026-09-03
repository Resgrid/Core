using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Providers.Scanning;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The clamd INSTREAM client (RMS plan section 4.7 malware scanning) against a loopback fake that speaks the
	/// wire protocol, so framing, reply parsing and the fail-closed rule are exercised end to end without a
	/// ClamAV install.
	/// </summary>
	[TestFixture]
	public class ClamAvAttachmentScannerTests
	{
		private const string EicarPrefix = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR";

		private bool _enabled;
		private string _host;
		private int _port;
		private int _timeout;
		private int _chunkSize;
		private bool _failClosed;
		private readonly List<FakeClamd> _fakes = new List<FakeClamd>();

		[SetUp]
		public void SetUp()
		{
			_enabled = AttachmentScanningConfig.Enabled;
			_host = AttachmentScanningConfig.Host;
			_port = AttachmentScanningConfig.Port;
			_timeout = AttachmentScanningConfig.TimeoutSeconds;
			_chunkSize = AttachmentScanningConfig.ChunkSize;
			_failClosed = AttachmentScanningConfig.FailClosed;

			AttachmentScanningConfig.Enabled = true;
			AttachmentScanningConfig.Host = "127.0.0.1";
			AttachmentScanningConfig.TimeoutSeconds = 5;
			AttachmentScanningConfig.ChunkSize = 1024;
			AttachmentScanningConfig.FailClosed = true;
		}

		[TearDown]
		public void TearDown()
		{
			foreach (var fake in _fakes)
				fake.Dispose();
			_fakes.Clear();

			AttachmentScanningConfig.Enabled = _enabled;
			AttachmentScanningConfig.Host = _host;
			AttachmentScanningConfig.Port = _port;
			AttachmentScanningConfig.TimeoutSeconds = _timeout;
			AttachmentScanningConfig.ChunkSize = _chunkSize;
			AttachmentScanningConfig.FailClosed = _failClosed;
		}

		private FakeClamd Clamd(Func<byte[], string> reply, bool neverReply = false)
		{
			var fake = new FakeClamd(reply, neverReply);
			_fakes.Add(fake);
			AttachmentScanningConfig.Port = fake.Port;
			return fake;
		}

		private static string ClamdVerdict(byte[] payload)
		{
			return Encoding.ASCII.GetString(payload).Contains(EicarPrefix) ? "stream: Win.Test.EICAR_HDB-1 FOUND\0" : "stream: OK\0";
		}

		private static byte[] Bytes(int count)
		{
			var data = new byte[count];
			for (var i = 0; i < count; i++)
				data[i] = (byte)(i % 251);
			return data;
		}

		[Test]
		public async Task Disabled_scanning_reports_skipped_without_opening_a_socket()
		{
			AttachmentScanningConfig.Enabled = false;
			AttachmentScanningConfig.Port = 1; // nothing listens here; a connection attempt would fail the test

			var result = await new ClamAvAttachmentScanner().ScanAsync("report.pdf", "application/pdf", Bytes(10));

			result.State.Should().Be(RmsAttachmentScanState.Skipped);
			result.Detail.Should().Be(ClamAvAttachmentScanner.DisabledDetail);
		}

		[Test]
		public async Task A_clean_file_is_streamed_in_chunks_and_reported_clean()
		{
			var fake = Clamd(ClamdVerdict);
			var data = Bytes(2500);

			var result = await new ClamAvAttachmentScanner().ScanAsync("report.pdf", "application/pdf", data);

			result.State.Should().Be(RmsAttachmentScanState.Clean);
			fake.Command.Should().Be("zINSTREAM");
			fake.Chunks.Should().Be(3, "1024-byte chunks for 2500 bytes");
			fake.Received.Should().Equal(data, "clamd must see exactly the bytes that will be stored");
		}

		[Test]
		public async Task A_signature_hit_rejects_the_attachment_and_names_the_signature()
		{
			Clamd(ClamdVerdict);
			var data = Encoding.ASCII.GetBytes(EicarPrefix + "-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

			var result = await new ClamAvAttachmentScanner().ScanAsync("eicar.com", "application/octet-stream", data);

			result.State.Should().Be(RmsAttachmentScanState.Rejected);
			result.Detail.Should().Contain("Win.Test.EICAR_HDB-1");
		}

		[Test]
		public async Task An_engine_error_follows_the_fail_closed_setting()
		{
			Clamd(_ => "stream: INSTREAM size limit exceeded. ERROR\0");
			var scanner = new ClamAvAttachmentScanner();

			AttachmentScanningConfig.FailClosed = true;
			var closed = await scanner.ScanAsync("big.pdf", "application/pdf", Bytes(100));
			closed.State.Should().Be(RmsAttachmentScanState.Rejected);
			closed.Detail.Should().Contain("size limit exceeded");

			Clamd(_ => "stream: INSTREAM size limit exceeded. ERROR\0");
			AttachmentScanningConfig.FailClosed = false;
			var open = await scanner.ScanAsync("big.pdf", "application/pdf", Bytes(100));
			open.State.Should().Be(RmsAttachmentScanState.Pending);
			open.Detail.Should().Contain("size limit exceeded");
		}

		[Test]
		public async Task An_unreachable_engine_follows_the_fail_closed_setting()
		{
			// Bind then release a port so nothing is listening on it.
			var probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			AttachmentScanningConfig.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();

			var scanner = new ClamAvAttachmentScanner();

			AttachmentScanningConfig.FailClosed = true;
			(await scanner.ScanAsync("a.pdf", "application/pdf", Bytes(10))).State.Should().Be(RmsAttachmentScanState.Rejected);

			AttachmentScanningConfig.FailClosed = false;
			var open = await scanner.ScanAsync("a.pdf", "application/pdf", Bytes(10));
			open.State.Should().Be(RmsAttachmentScanState.Pending);
			open.Detail.Should().Contain("unreachable");
		}

		[Test]
		public async Task A_silent_engine_times_out_rather_than_hanging_the_upload()
		{
			Clamd(ClamdVerdict, neverReply: true);
			AttachmentScanningConfig.TimeoutSeconds = 1;

			var result = await new ClamAvAttachmentScanner().ScanAsync("a.pdf", "application/pdf", Bytes(10));

			result.State.Should().Be(RmsAttachmentScanState.Rejected);
			result.Detail.Should().Contain("did not answer");
		}

		[Test]
		public void The_callers_own_cancellation_still_propagates()
		{
			Clamd(ClamdVerdict, neverReply: true);
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			Func<Task> act = () => new ClamAvAttachmentScanner().ScanAsync("a.pdf", "application/pdf", Bytes(10), cts.Token);

			act.Should().ThrowAsync<OperationCanceledException>();
		}

		[TestCase("stream: OK\0", RmsAttachmentScanState.Clean, "OK")]
		[TestCase("stream: Win.Test.EICAR_HDB-1 FOUND\0", RmsAttachmentScanState.Rejected, "Win.Test.EICAR_HDB-1")]
		[TestCase("stream: Eicar-Signature FOUND", RmsAttachmentScanState.Rejected, "Eicar-Signature")]
		[TestCase("stream: INSTREAM size limit exceeded. ERROR\0", RmsAttachmentScanState.Rejected, "size limit")]
		[TestCase("", RmsAttachmentScanState.Rejected, "empty reply")]
		[TestCase(null, RmsAttachmentScanState.Rejected, "empty reply")]
		public void Replies_map_to_scan_states(string reply, RmsAttachmentScanState expected, string detailFragment)
		{
			AttachmentScanningConfig.FailClosed = true;

			var result = ClamAvAttachmentScanner.Interpret(reply);

			result.State.Should().Be(expected);
			result.Detail.Should().Contain(detailFragment);
		}

		/// <summary>Loopback clamd: one connection, INSTREAM framing, a scripted reply.</summary>
		private sealed class FakeClamd : IDisposable
		{
			private readonly TcpListener _listener;
			private readonly Func<byte[], string> _reply;
			private readonly bool _neverReply;
			private readonly List<byte> _received = new List<byte>();

			public FakeClamd(Func<byte[], string> reply, bool neverReply)
			{
				_reply = reply;
				_neverReply = neverReply;
				_listener = new TcpListener(IPAddress.Loopback, 0);
				_listener.Start();
				_ = Task.Run(ServeAsync);
			}

			public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
			public string Command { get; private set; }
			public int Chunks { get; private set; }
			public byte[] Received { get { lock (_received) return _received.ToArray(); } }

			private async Task ServeAsync()
			{
				try
				{
					using var client = await _listener.AcceptTcpClientAsync();
					using var stream = client.GetStream();

					var command = new List<byte>();
					var one = new byte[1];
					while (await stream.ReadAsync(one, 0, 1) == 1 && one[0] != 0)
						command.Add(one[0]);
					Command = Encoding.ASCII.GetString(command.ToArray());

					var header = new byte[4];
					while (true)
					{
						await stream.ReadExactlyAsync(header, 0, 4);
						var length = BinaryPrimitives.ReadInt32BigEndian(header);
						if (length == 0)
							break;

						var chunk = new byte[length];
						await stream.ReadExactlyAsync(chunk, 0, length);
						Chunks++;
						lock (_received) _received.AddRange(chunk);
					}

					if (_neverReply)
					{
						await Task.Delay(TimeSpan.FromSeconds(10));
						return;
					}

					var reply = Encoding.ASCII.GetBytes(_reply(Received));
					await stream.WriteAsync(reply, 0, reply.Length);
					await stream.FlushAsync();
				}
				catch
				{
					// The scanner closing early (timeout, cancellation) ends the fake; nothing to assert here.
				}
			}

			public void Dispose()
			{
				try { _listener.Stop(); } catch { }
			}
		}
	}
}
