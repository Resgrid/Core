using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// The verification wording is what an administrator acts on when reading a communication test
	/// report ("who can we actually reach?"), and the same labels are served to the apps through the
	/// v4 API. Pin them so a rename cannot quietly change what the report claims.
	/// </summary>
	[TestFixture]
	public class CommunicationTestResultExtensionsTests
	{
		private static CommunicationTestResult Result(CommunicationTestChannel channel, ContactVerificationStatus status)
		{
			return new CommunicationTestResult
			{
				Channel = (int)channel,
				VerificationStatus = (int)status
			};
		}

		[Test]
		public void verified_channel_should_read_as_verified()
		{
			Result(CommunicationTestChannel.Sms, ContactVerificationStatus.Verified)
				.GetVerificationDisplayText().Should().Be("Verified");
		}

		[Test]
		public void pending_channel_should_read_as_unverified_not_pending()
		{
			// "Pending" describes the code lifecycle; the administrator needs to know the channel is
			// unverified and will not be sent to.
			Result(CommunicationTestChannel.Email, ContactVerificationStatus.Pending)
				.GetVerificationDisplayText().Should().Be("Unverified");
		}

		[Test]
		public void grandfathered_channel_should_read_as_grandfathered()
		{
			Result(CommunicationTestChannel.Voice, ContactVerificationStatus.Grandfathered)
				.GetVerificationDisplayText().Should().Be("Grandfathered");
		}

		[Test]
		public void push_should_report_not_applicable_regardless_of_stored_status()
		{
			// Push rows are stored as Verified because there is nothing to verify; surfacing that
			// word would tell the reader a device is confirmed when no such check exists.
			Result(CommunicationTestChannel.Push, ContactVerificationStatus.Verified)
				.GetVerificationDisplayText().Should().Be("N/A");

			Result(CommunicationTestChannel.Push, ContactVerificationStatus.Verified)
				.HasVerifiableContactMethod().Should().BeFalse();
		}

		[Test]
		public void contact_channels_should_count_as_verifiable()
		{
			Result(CommunicationTestChannel.Sms, ContactVerificationStatus.Verified).HasVerifiableContactMethod().Should().BeTrue();
			Result(CommunicationTestChannel.Email, ContactVerificationStatus.Verified).HasVerifiableContactMethod().Should().BeTrue();
			Result(CommunicationTestChannel.Voice, ContactVerificationStatus.Verified).HasVerifiableContactMethod().Should().BeTrue();
		}

		[Test]
		public void missing_result_should_render_a_dash()
		{
			CommunicationTestResult missing = null;
			missing.GetVerificationDisplayText().Should().Be("-");
		}

		[Test]
		public void verification_status_labels_should_match_the_report_legend()
		{
			ContactVerificationStatus.Verified.ToDisplayText().Should().Be("Verified");
			ContactVerificationStatus.Pending.ToDisplayText().Should().Be("Unverified");
			ContactVerificationStatus.Grandfathered.ToDisplayText().Should().Be("Grandfathered");
		}
	}
}
