using System;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class PasswordRecoveryServiceTests
	{
		[Test]
		public async Task issue_creates_an_opaque_short_lived_grant_without_putting_the_token_in_the_cache_key()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			string storedKey = null;
			string storedValue = null;
			TimeSpan storedLifetime = default;
			cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.Callback<string, string, TimeSpan>((key, value, lifetime) =>
				{
					storedKey = key;
					storedValue = value;
					storedLifetime = lifetime;
				})
				.ReturnsAsync(true);
			var service = new PasswordRecoveryService(cache.Object);

			var result = await service.IssueAsync("user-1", "person@example.test", "192.0.2.1", 4, "stamp");

			Assert.That(result.Issued, Is.True);
			Assert.That(result.Token, Has.Length.EqualTo(43));
			Assert.That(result.Token, Does.Match("^[A-Za-z0-9_-]+$"));
			Assert.That(storedKey, Does.Not.Contain(result.Token));
			Assert.That(storedLifetime, Is.EqualTo(TimeSpan.FromMinutes(
				Math.Max(5, SessionSecurityConfig.PublicResetLinkLifetimeMinutes))));
			var request = JsonConvert.DeserializeObject<PasswordRecoveryRequest>(storedValue);
			Assert.That(request.UserId, Is.EqualTo("user-1"));
			Assert.That(request.Email, Is.EqualTo("person@example.test"));
			Assert.That(request.AuthenticationGeneration, Is.EqualTo(4));
			Assert.That(request.SecurityStampHash, Has.Length.EqualTo(64));
		}

		[Test]
		public async Task unknown_accounts_are_rate_limited_but_never_receive_a_persisted_grant()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var service = new PasswordRecoveryService(cache.Object);

			var result = await service.IssueAsync(null, "unknown@example.test", "192.0.2.2", 0, null);

			Assert.That(result.Issued, Is.False);
			Assert.That(result.Token, Is.Null);
			cache.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()),
				Times.Never);
			cache.Verify(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Exactly(2));
		}

		[Test]
		public async Task consume_is_single_use_even_while_the_request_record_still_exists()
		{
			var cache = new Mock<ICacheProvider>();
			var request = new PasswordRecoveryRequest
			{
				UserId = "user-1",
				Email = "person@example.test",
				CreatedOn = DateTime.UtcNow,
				ExpiresOn = DateTime.UtcNow.AddMinutes(10)
			};
			cache.Setup(x => x.GetStringAsync(It.IsAny<string>()))
				.ReturnsAsync(JsonConvert.SerializeObject(request));
			cache.SetupSequence(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(1)
				.ReturnsAsync(2);
			var service = new PasswordRecoveryService(cache.Object);

			Assert.That(await service.TryConsumeAsync("opaque-token"), Is.True);
			Assert.That(await service.TryConsumeAsync("opaque-token"), Is.False);
		}
	}
}
