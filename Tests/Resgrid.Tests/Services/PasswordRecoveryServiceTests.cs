using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// GetAsync resolves a recovery token and must always hand back a result: a missing, unreadable or expired
	/// token is a normal outcome of the reset flow, and every one of them has to be indistinguishable to the
	/// caller so nothing leaks which tokens ever existed.
	/// </summary>
	[TestFixture]
	public class PasswordRecoveryServiceTests
	{
		private const string Token = "a-recovery-token";

		private Mock<ICacheProvider> _cacheProvider;
		private PasswordRecoveryService _service;

		[SetUp]
		public void SetUp()
		{
			_cacheProvider = new Mock<ICacheProvider>();
			_service = new PasswordRecoveryService(_cacheProvider.Object);
		}

		private void StoredPayloadIs(string json) =>
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync(json);

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public async Task GetAsync_BlankToken_ReportsNotFoundWithoutTouchingTheCache(string token)
		{
			var result = await _service.GetAsync(token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
			_cacheProvider.Verify(x => x.GetStringAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetAsync_UnknownToken_ReportsNotFound()
		{
			StoredPayloadIs(null);

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
		}

		[Test]
		public async Task GetAsync_UnreadablePayload_ReportsNotFound()
		{
			StoredPayloadIs("{ this is not json");

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
		}

		[Test]
		public async Task GetAsync_ExpiredRequest_ReportsNotFound()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				Email = "someone@example.com",
				CreatedOn = DateTime.UtcNow.AddHours(-2),
				ExpiresOn = DateTime.UtcNow.AddMinutes(-1)
			}));

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
		}

		[Test]
		public async Task GetAsync_LiveRequest_ReturnsTheRequest()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				Email = "someone@example.com",
				AuthenticationGeneration = 7,
				CreatedOn = DateTime.UtcNow,
				ExpiresOn = DateTime.UtcNow.AddMinutes(10)
			}));

			var result = await _service.GetAsync(Token);

			result.Found.Should().BeTrue();
			result.Request.Should().NotBeNull();
			result.Request?.UserId.Should().Be("user-1");
			result.Request?.AuthenticationGeneration.Should().Be(7);
		}

		[Test]
		public async Task GetAsync_NullPayload_ReportsNotFound()
		{
			StoredPayloadIs("null");

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
		}

		[Test]
		public async Task TryConsumeAsync_ExpiredRequest_DoesNotConsume()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				ExpiresOn = DateTime.UtcNow.AddMinutes(-1)
			}));

			(await _service.TryConsumeAsync(Token)).Should().BeFalse();
			_cacheProvider.Verify(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
		}

		[Test]
		public async Task TryConsumeAsync_LiveRequest_ConsumesOnlyOnce()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				ExpiresOn = DateTime.UtcNow.AddMinutes(10)
			}));
			_cacheProvider.SetupSequence(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(1)
				.ReturnsAsync(2);

			(await _service.TryConsumeAsync(Token)).Should().BeTrue();
			(await _service.TryConsumeAsync(Token)).Should().BeFalse();
		}

		// ---- Link lifetimes --------------------------------------------------------------------------------------------

		[Test]
		public void Default_lifetimes_leave_time_for_a_slow_inbox_and_an_off_shift_member()
		{
			// Thirty minutes was too short: mail filtering can hold a message for most of that, and a responder who asks for a
			// link can be called out before they open it. Each link is still single-use and dies with any password change.
			SessionSecurityConfig.PublicResetLinkLifetimeMinutes.Should().Be(120);
			SessionSecurityConfig.AdministratorResetLinkLifetimeMinutes.Should().Be(1440);
		}

		[Test]
		public async Task IssueAsync_SelfServiceLink_IsStoredForTheSelfServiceLifetimeAndReportsIt()
		{
			_cacheProvider.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			_cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
			var lifetime = TimeSpan.FromMinutes(SessionSecurityConfig.PublicResetLinkLifetimeMinutes);
			var before = DateTime.UtcNow;

			var issue = await _service.IssueAsync("user-1", "someone@example.com", "203.0.113.9", 3, "stamp");

			issue.Issued.Should().BeTrue();
			issue.Token.Should().NotBeNullOrWhiteSpace();
			issue.Lifetime.Should().Be(lifetime);
			issue.ExpiresOn.Should().NotBeNull();
			issue.ExpiresOn!.Value.Should().BeCloseTo(before.Add(lifetime), TimeSpan.FromSeconds(5));
			_cacheProvider.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), lifetime), Times.Once);
		}

		[Test]
		public async Task IssueForAdministratorAsync_LastsTheAdministratorLifetimeAndNeverCountsTheNetwork()
		{
			_cacheProvider.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			_cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
			var lifetime = TimeSpan.FromMinutes(SessionSecurityConfig.AdministratorResetLinkLifetimeMinutes);

			var issue = await _service.IssueForAdministratorAsync("user-1", "someone@example.com", 3, "stamp");

			issue.Issued.Should().BeTrue();
			issue.Lifetime.Should().Be(lifetime);
			_cacheProvider.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), lifetime), Times.Once);
			_cacheProvider.Verify(x => x.IncrementAsync(It.Is<string>(key => key.Contains("rate:ip:")), It.IsAny<TimeSpan>()), Times.Never,
				"an administrator's sends are not signed-out requests from their network");
		}

		[Test]
		public async Task An_administrator_sending_many_links_neither_hits_nor_uses_up_their_networks_public_limit()
		{
			var service = new PasswordRecoveryService(new InMemoryCacheProvider());

			for (var i = 0; i < SessionSecurityConfig.PublicResetIpLimitPerHour + 5; i++)
				(await service.IssueForAdministratorAsync($"user-{i}", $"member{i}@example.test", 0, "stamp")).Issued
					.Should().BeTrue($"send {i + 1} is for a different member");

			(await service.IssueAsync("user-x", "someone@example.test", "203.0.113.7", 0, "stamp")).Issued
				.Should().BeTrue("the station's own forgot-password requests are unaffected by the administrator's sends");
		}

		[Test]
		public async Task Administrator_links_still_count_against_the_members_own_hourly_limit()
		{
			var service = new PasswordRecoveryService(new InMemoryCacheProvider());
			for (var i = 0; i < SessionSecurityConfig.PublicResetAccountLimitPerHour; i++)
				(await service.IssueForAdministratorAsync("user-1", "member@example.test", 0, "stamp")).Issued.Should().BeTrue();

			var over = await service.IssueForAdministratorAsync("user-1", "member@example.test", 0, "stamp");
			over.RateLimited.Should().BeTrue();
			over.Issued.Should().BeFalse();
			(await service.IssueAsync("user-1", " MEMBER@example.test", "198.51.100.3", 0, "stamp")).RateLimited
				.Should().BeTrue("there is one counter per member, whoever sends the link");
		}

		[Test]
		public async Task TryConsumeAsync_KeepsTheUsedMarkerForAsLongAsTheGrantLives()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				ExpiresOn = DateTime.UtcNow.AddHours(20)
			}));
			TimeSpan? markerLifetime = null;
			_cacheProvider.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.Callback<string, TimeSpan>((_, expiration) => markerLifetime = expiration)
				.ReturnsAsync(1);

			(await _service.TryConsumeAsync(Token)).Should().BeTrue();

			markerLifetime.Should().NotBeNull();
			markerLifetime!.Value.Should().BeCloseTo(TimeSpan.FromHours(20), TimeSpan.FromMinutes(1),
				"a day-long administrator link must not become usable again once a self-service lifetime has passed");
		}

		[TestCase(120, "2 hours")]
		[TestCase(60, "1 hour")]
		[TestCase(1440, "24 hours")]
		[TestCase(90, "90 minutes")]
		[TestCase(30, "30 minutes")]
		[TestCase(1, "1 minute")]
		public void Reset_emails_word_the_link_lifetime_plainly(int minutes, string expected) =>
			EmailService.DescribeLinkLifetime(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
	}
}
