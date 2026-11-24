using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Providers.NumberProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>The Twilio recording provider (M0332) only ever builds carrier URLs from well-formed sids, and does nothing unconfigured.</summary>
	[TestFixture, NonParallelizable]
	public class VoiceRecordingProviderTests
	{
		[TestCase("RE0123456789abcdef0123456789ABCDEF", "RE", true)]
		[TestCase(" RE0123456789abcdef0123456789abcdef ", "RE", true)]
		[TestCase("CA0123456789abcdef0123456789abcdef", "CA", true)]
		[TestCase("CA0123456789abcdef0123456789abcdef", "RE", false)]
		[TestCase("RE0123456789abcdef0123456789abcde", "RE", false)]
		[TestCase("RE0123456789abcdef0123456789abcdeg", "RE", false)]
		[TestCase("RE/../../Calls/0123456789abcdef0123", "RE", false)]
		[TestCase(null, "RE", false)]
		[TestCase("", "RE", false)]
		public void a_sid_is_the_prefix_and_32_hex_digits(string value, string prefix, bool expected)
		{
			VoiceRecordingProvider.IsSid(value, prefix).Should().Be(expected);
		}

		[Test]
		public async Task a_body_that_stalls_after_the_headers_is_unavailable_once_the_body_timeout_passes()
		{
			var stalled = new StallingStream(new byte[] { 1, 2, 3 });

			var body = await VoiceRecordingProvider.ReadBodyAsync(stalled, 1024, TimeSpan.FromMilliseconds(200), CancellationToken.None);

			body.Status.Should().Be(VoiceRecordingDownloadStatuses.Unavailable, "worker 80 retries it; the rest of its pass goes on");
		}

		[Test]
		public async Task the_callers_own_cancellation_still_throws_during_the_body_read()
		{
			using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

			var read = () => VoiceRecordingProvider.ReadBodyAsync(new StallingStream(new byte[0]), 1024, TimeSpan.FromMinutes(5), caller.Token);

			await read.Should().ThrowAsync<OperationCanceledException>();
		}

		[Test]
		public async Task a_whole_body_is_read_and_one_over_the_limit_is_too_large()
		{
			(await VoiceRecordingProvider.ReadBodyAsync(new MemoryStream(new byte[] { 9, 8, 7 }), 1024, TimeSpan.FromSeconds(5), CancellationToken.None)).Data.Should().Equal(9, 8, 7);
			(await VoiceRecordingProvider.ReadBodyAsync(new MemoryStream(new byte[5]), 4, TimeSpan.FromSeconds(5), CancellationToken.None)).Status
				.Should().Be(VoiceRecordingDownloadStatuses.TooLarge);
			(await VoiceRecordingProvider.ReadBodyAsync(new MemoryStream(new byte[0]), 4, TimeSpan.FromSeconds(5), CancellationToken.None)).Status
				.Should().Be(VoiceRecordingDownloadStatuses.Unavailable);
		}

		/// <summary>Hands over its bytes, then never sends another (a media server that stopped mid-body).</summary>
		private sealed class StallingStream : Stream
		{
			private byte[] _first;

			public StallingStream(byte[] first) { _first = first; }

			public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			{
				if (_first != null && _first.Length > 0)
				{
					var count = Math.Min(buffer.Length, _first.Length);
					_first.AsMemory(0, count).CopyTo(buffer);
					_first = null;
					return count;
				}

				await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
				return 0;
			}

			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
			public override void Flush() { }
			public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}

		[Test]
		public async Task without_twilio_credentials_nothing_is_called()
		{
			var savedSid = Resgrid.Config.NumberProviderConfig.TwilioAccountSid;
			var savedToken = Resgrid.Config.NumberProviderConfig.TwilioAuthToken;
			try
			{
				Resgrid.Config.NumberProviderConfig.TwilioAccountSid = "";
				Resgrid.Config.NumberProviderConfig.TwilioAuthToken = "";
				var provider = new VoiceRecordingProvider();

				(await provider.StartCallRecordingAsync("CA0123456789abcdef0123456789abcdef", "https://api.example/api/Twilio/VoiceIntakeRecording")).Should().BeNull();
				(await provider.DownloadRecordingAsync("RE0123456789abcdef0123456789abcdef", 1024)).Status.Should().Be(VoiceRecordingDownloadStatuses.Unavailable);
				(await provider.DeleteRecordingAsync("RE0123456789abcdef0123456789abcdef")).Should().BeFalse("unconfigured: the copy is not known to be gone");
				(await provider.DownloadRecordingAsync("not-a-sid", 1024)).Status.Should().Be(VoiceRecordingDownloadStatuses.NotFound);
				(await provider.DeleteRecordingAsync("not-a-sid")).Should().BeTrue("nothing a malformed sid could name exists");
			}
			finally
			{
				Resgrid.Config.NumberProviderConfig.TwilioAccountSid = savedSid;
				Resgrid.Config.NumberProviderConfig.TwilioAuthToken = savedToken;
			}
		}
	}
}
