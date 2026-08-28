using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ProtectedDataEnvelopeTests
	{
		[Test]
		public void Format_then_TryParse_round_trips()
		{
			var envelope = ProtectedDataEnvelope.Format(3, "cGF5bG9hZA==");

			envelope.Should().Be("rgdp:1:3:cGF5bG9hZA==");
			ProtectedDataEnvelope.TryParse(envelope, out var formatVersion, out var keyVersion, out var payload).Should().BeTrue();
			formatVersion.Should().Be(ProtectedDataEnvelope.CurrentVersion);
			keyVersion.Should().Be(3);
			payload.Should().Be("cGF5bG9hZA==");
			ProtectedDataEnvelope.IsEnveloped(envelope).Should().BeTrue();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("plaintext narrative about a patient")]
		[TestCase("rgdp")]
		[TestCase("rgdp:")]
		[TestCase("rgdp:1:3")]
		[TestCase("rgdp:1:3:")]
		[TestCase("rgdp:x:3:payload")]
		[TestCase("rgdp:1:x:payload")]
		[TestCase("rgdp:0:3:payload")]
		[TestCase("rgdp:1:0:payload")]
		public void TryParse_rejects_plaintext_and_malformed_values(string value)
		{
			ProtectedDataEnvelope.TryParse(value, out _, out _, out _).Should().BeFalse();
			ProtectedDataEnvelope.IsEnveloped(value).Should().BeFalse();
		}

		[Test]
		public void Unknown_future_format_version_is_not_parseable_and_reads_as_corrupt()
		{
			// A future-version envelope is NOT parseable (documented TryParse contract) and must not
			// read as plaintext either (HasEnvelopePrefix still true) — a prefixed value the current
			// code cannot parse is corrupt, handled fail closed upstream.
			var future = "rgdp:99:1:payload";
			ProtectedDataEnvelope.TryParse(future, out _, out _, out _).Should().BeFalse();
			ProtectedDataEnvelope.IsEnveloped(future).Should().BeFalse();
			ProtectedDataEnvelope.HasEnvelopePrefix(future).Should().BeTrue();
		}

		[Test]
		public void Payload_containing_colons_is_preserved()
		{
			// base64 never contains ':' but the parser must still be robust to them (split limit 4).
			ProtectedDataEnvelope.TryParse("rgdp:1:2:abc:def", out _, out _, out var payload).Should().BeTrue();
			payload.Should().Be("abc:def");
		}

		[Test]
		public void Binary_prefix_is_detected_but_not_text_parseable()
		{
			ProtectedDataEnvelope.HasEnvelopePrefix("rgdpb:1:2:xyz").Should().BeTrue();
			ProtectedDataEnvelope.TryParse("rgdpb:1:2:xyz", out _, out _, out _).Should().BeFalse();
		}

		[Test]
		public void Redaction_value_is_the_exact_contract_string()
		{
			ProtectedDataEnvelope.RedactionValue.Should().Be("REDACTED");
		}
	}
}
