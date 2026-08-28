using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Signed anonymous file-link validation (CallFiles/GetFile): expiring three-part links are
	/// enforced, legacy two-part links survive only while the transition flag allows them, and
	/// malformed payloads always read as invalid.
	/// </summary>
	[TestFixture]
	public class CallFilesSignedLinkTests
	{
		private bool _originalAllowLegacy;

		[SetUp]
		public void SetUp() => _originalAllowLegacy = SecurityConfig.AllowLegacySignedFileLinks;

		[TearDown]
		public void TearDown() => SecurityConfig.AllowLegacySignedFileLinks = _originalAllowLegacy;

		[Test]
		public void Unexpired_three_part_link_is_valid()
		{
			var payload = $"42|17|{DateTime.UtcNow.AddHours(1).Ticks}";

			CallFilesController.TryValidateSignedFileQuery(payload, out var dept, out var attachment).Should().BeTrue();
			dept.Should().Be(42);
			attachment.Should().Be(17);
		}

		[Test]
		public void Expired_link_is_refused()
		{
			var payload = $"42|17|{DateTime.UtcNow.AddMinutes(-1).Ticks}";

			CallFilesController.TryValidateSignedFileQuery(payload, out _, out _).Should().BeFalse();
		}

		[Test]
		public void Legacy_two_part_link_honors_the_transition_flag()
		{
			SecurityConfig.AllowLegacySignedFileLinks = true;
			CallFilesController.TryValidateSignedFileQuery("42|17", out _, out _).Should().BeTrue();

			SecurityConfig.AllowLegacySignedFileLinks = false;
			CallFilesController.TryValidateSignedFileQuery("42|17", out _, out _).Should().BeFalse();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("42")]
		[TestCase("0|17")]
		[TestCase("abc|17")]
		[TestCase("42|abc")]
		[TestCase("42|17|not-ticks")]
		public void Malformed_payloads_are_refused(string payload)
		{
			CallFilesController.TryValidateSignedFileQuery(payload, out _, out _).Should().BeFalse();
		}
	}
}
