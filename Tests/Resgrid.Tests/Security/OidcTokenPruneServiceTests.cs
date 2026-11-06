using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using OpenIddict.Abstractions;
using Resgrid.Config;
using Resgrid.Web.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// One pass of the OIDC store prune: tokens first, then authorizations, both from the same threshold, which never comes
	/// closer than a day whatever the configured minimum age.
	/// </summary>
	[TestFixture]
	public class OidcTokenPruneServiceTests
	{
		private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

		private int _savedMinimumAgeDays;
		private List<string> _calls;
		private Mock<IOpenIddictTokenManager> _tokens;
		private Mock<IOpenIddictAuthorizationManager> _authorizations;

		[SetUp]
		public void SetUp()
		{
			_savedMinimumAgeDays = OidcConfig.TokenPruneMinimumAgeDays;
			_calls = new List<string>();
			_tokens = new Mock<IOpenIddictTokenManager>();
			_tokens.Setup(t => t.PruneAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
				.Callback<DateTimeOffset, CancellationToken>((threshold, _) => _calls.Add($"tokens {threshold:O}"))
				.ReturnsAsync(0L);
			_authorizations = new Mock<IOpenIddictAuthorizationManager>();
			_authorizations.Setup(a => a.PruneAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
				.Callback<DateTimeOffset, CancellationToken>((threshold, _) => _calls.Add($"authorizations {threshold:O}"))
				.ReturnsAsync(0L);
		}

		[TearDown]
		public void TearDown() => OidcConfig.TokenPruneMinimumAgeDays = _savedMinimumAgeDays;

		[Test]
		public async Task Prunes_tokens_then_authorizations_older_than_the_minimum_age()
		{
			OidcConfig.TokenPruneMinimumAgeDays = 14;

			await OidcTokenPruneService.PruneAsync(_tokens.Object, _authorizations.Object, Now, CancellationToken.None);

			var threshold = Now.AddDays(-14);
			_calls.Should().Equal($"tokens {threshold:O}", $"authorizations {threshold:O}");
		}

		[TestCase(0)]
		[TestCase(-3)]
		public async Task Never_prunes_anything_younger_than_a_day(int configuredDays)
		{
			OidcConfig.TokenPruneMinimumAgeDays = configuredDays;

			await OidcTokenPruneService.PruneAsync(_tokens.Object, _authorizations.Object, Now, CancellationToken.None);

			_tokens.Verify(t => t.PruneAsync(Now.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
			_authorizations.Verify(a => a.PruneAsync(Now.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
