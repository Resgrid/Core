using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The per-address password sign-in throttle: failures across different usernames (spraying, stuffing) or too many attempts pause
	/// the address; one account's own repeated failures do not (the lockout handles those), and the cache never blocks a sign-in.
	/// </summary>
	[TestFixture]
	public class LoginThrottleServiceTests
	{
		private (int Window, int Distinct, int Attempts) _saved;
		private InMemoryCacheProvider _cache;
		private LoginThrottleService _throttle;

		[SetUp]
		public void SetUp()
		{
			_saved = (SessionSecurityConfig.LoginThrottleWindowMinutes, SessionSecurityConfig.LoginThrottleDistinctAccountLimit,
				SessionSecurityConfig.LoginThrottleAttemptLimit);
			(SessionSecurityConfig.LoginThrottleWindowMinutes, SessionSecurityConfig.LoginThrottleDistinctAccountLimit,
				SessionSecurityConfig.LoginThrottleAttemptLimit) = (15, 3, 20);
			_cache = new InMemoryCacheProvider();
			_throttle = new LoginThrottleService(_cache);
		}

		[TearDown]
		public void TearDown() =>
			(SessionSecurityConfig.LoginThrottleWindowMinutes, SessionSecurityConfig.LoginThrottleDistinctAccountLimit,
				SessionSecurityConfig.LoginThrottleAttemptLimit) = _saved;

		[Test]
		public async Task Failures_across_the_distinct_account_limit_pause_that_address_and_no_other()
		{
			await _throttle.RecordFailureAsync("203.0.113.7", "alice");
			await _throttle.RecordFailureAsync("203.0.113.7", "bob");
			(await _throttle.IsThrottledAsync("203.0.113.7")).Should().BeFalse("two accounts is under the limit of three");

			await _throttle.RecordFailureAsync("203.0.113.7", "carol");

			(await _throttle.IsThrottledAsync("203.0.113.7")).Should().BeTrue();
			(await _throttle.IsThrottledAsync("198.51.100.4")).Should().BeFalse("another network is untouched");
		}

		[Test]
		public async Task One_account_failing_over_and_over_counts_once_and_only_the_attempt_ceiling_pauses_it()
		{
			// Case and spacing don't make a different account.
			foreach (var name in new[] { "alice", "ALICE", " Alice " })
				await _throttle.RecordFailureAsync("203.0.113.7", name);
			for (var i = 3; i < 19; i++)
				await _throttle.RecordFailureAsync("203.0.113.7", "alice");
			(await _throttle.IsThrottledAsync("203.0.113.7")).Should().BeFalse("19 failures of one account stay with its lockout");

			await _throttle.RecordFailureAsync("203.0.113.7", "alice");

			(await _throttle.IsThrottledAsync("203.0.113.7")).Should().BeTrue("the attempt ceiling bounds even a single account");
		}

		[Test]
		public async Task Limits_of_zero_turn_the_throttle_off()
		{
			(SessionSecurityConfig.LoginThrottleDistinctAccountLimit, SessionSecurityConfig.LoginThrottleAttemptLimit) = (0, 0);

			foreach (var name in new[] { "a", "b", "c", "d", "e" })
				await _throttle.RecordFailureAsync("203.0.113.7", name);

			(await _throttle.IsThrottledAsync("203.0.113.7")).Should().BeFalse();
		}

		[Test]
		public async Task A_cache_that_fails_never_blocks_a_sign_in()
		{
			var broken = new Mock<ICacheProvider>();
			broken.Setup(c => c.GetStringAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException());
			broken.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ThrowsAsync(new TimeoutException());
			var throttle = new LoginThrottleService(broken.Object);

			await throttle.Invoking(t => t.RecordFailureAsync("203.0.113.7", "alice")).Should().NotThrowAsync();
			(await throttle.IsThrottledAsync("203.0.113.7")).Should().BeFalse();
		}

		[Test]
		public async Task Addresses_and_usernames_are_stored_only_hashed()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			await new LoginThrottleService(cache.Object).RecordFailureAsync("203.0.113.7", "alice");

			cache.Verify(c => c.IncrementAsync(It.Is<string>(k => k.Contains("203.0.113.7") || k.Contains("alice", StringComparison.OrdinalIgnoreCase)),
				It.IsAny<TimeSpan>()), Times.Never);
		}
	}
}
