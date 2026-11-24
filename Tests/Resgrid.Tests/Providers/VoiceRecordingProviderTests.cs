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
