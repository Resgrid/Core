using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.BadgeCounts;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The badge counts registry (v4 Counts/GetBadgeCounts): a provider runs only when asked for and allowed, and one that
	/// fails or does not apply is left out without failing the others.
	/// </summary>
	[TestFixture]
	public class BadgeCountsServiceTests
	{
		private const int DepartmentId = 4;
		private const string UserId = "member-a";

		private sealed class FakeProvider : IBadgeCountProvider
		{
			private readonly Func<BadgeCountContext, Task<BadgeCount>> _count;

			public FakeProvider(string key, string policy, Func<BadgeCountContext, Task<BadgeCount>> count)
			{
				Key = key;
				RequiredPolicy = policy;
				_count = count;
			}

			public int Calls { get; private set; }

			public string Key { get; }

			public string RequiredPolicy { get; }

			public Task<BadgeCount> GetCountAsync(BadgeCountContext context)
			{
				Calls++;
				return _count(context);
			}
		}

		private static FakeProvider Counting(string key, int count, string policy = null) => new FakeProvider(key, policy, _ => Task.FromResult(new BadgeCount(key, count)));

		private static BadgeCountContext Member() => new BadgeCountContext { DepartmentId = DepartmentId, UserId = UserId };

		private static Task<bool> AllowAll(string policy) => Task.FromResult(true);

		[Test]
		public async Task only_asked_for_counts_run_and_unknown_keys_are_ignored()
		{
			var calls = Counting("calls", 3);
			var messages = Counting("messages", 2);
			var service = new BadgeCountsService(new IBadgeCountProvider[] { calls, messages });

			var counts = await service.GetBadgeCountsAsync(Member(), new[] { "CALLS", "future-key" }, AllowAll);

			counts.Select(c => (c.Key, c.Count)).Should().Equal(("calls", 3));
			messages.Calls.Should().Be(0);
		}

		[Test]
		public async Task no_keys_asks_for_every_count()
		{
			var service = new BadgeCountsService(new IBadgeCountProvider[] { Counting("messages", 1), Counting("calls", 2) });

			var counts = await service.GetBadgeCountsAsync(Member(), Array.Empty<string>(), AllowAll);

			counts.Select(c => c.Key).Should().Equal("calls", "messages");
			service.Keys.Should().Equal("calls", "messages");
		}

		[Test]
		public async Task a_count_whose_policy_the_member_lacks_is_never_computed()
		{
			var calls = Counting("calls", 3, "Call_View");
			var weather = Counting("weather", 1, "WeatherAlert_View");
			var service = new BadgeCountsService(new IBadgeCountProvider[] { calls, weather });

			var counts = await service.GetBadgeCountsAsync(Member(), null, policy => Task.FromResult(policy == "WeatherAlert_View"));

			counts.Select(c => c.Key).Should().Equal("weather");
			calls.Calls.Should().Be(0);
		}

		[Test]
		public async Task a_failing_or_inapplicable_count_is_left_out_without_failing_the_others()
		{
			var failing = new FakeProvider("calls", null, _ => throw new InvalidOperationException("db down"));
			var off = new FakeProvider("chats", null, _ => Task.FromResult<BadgeCount>(null));
			var service = new BadgeCountsService(new IBadgeCountProvider[] { failing, off, Counting("messages", 2) });

			var counts = await service.GetBadgeCountsAsync(Member(), null, AllowAll);

			counts.Select(c => (c.Key, c.Count)).Should().Equal(("messages", 2));
		}

		[Test]
		public async Task an_abandoned_request_starts_no_further_counts()
		{
			using var aborted = new CancellationTokenSource();
			var calls = new FakeProvider("calls", null, _ =>
			{
				aborted.Cancel();
				return Task.FromResult(new BadgeCount("calls", 1));
			});
			var messages = Counting("messages", 2);
			var service = new BadgeCountsService(new IBadgeCountProvider[] { calls, messages });

			var counts = await service.GetBadgeCountsAsync(Member(), null, AllowAll, aborted.Token);

			counts.Select(c => c.Key).Should().Equal("calls");
			messages.Calls.Should().Be(0);
		}

		[Test]
		public async Task nothing_is_counted_without_a_member()
		{
			var calls = Counting("calls", 3);
			var service = new BadgeCountsService(new IBadgeCountProvider[] { calls });

			(await service.GetBadgeCountsAsync(new BadgeCountContext { DepartmentId = DepartmentId }, null, AllowAll)).Should().BeEmpty();
			calls.Calls.Should().Be(0);
		}

		[Test]
		public void a_duplicate_key_keeps_one_provider()
		{
			new BadgeCountsService(new IBadgeCountProvider[] { Counting("calls", 1), Counting("Calls", 2) }).Keys.Should().Equal("calls");
		}

		[Test]
		public void counts_are_never_negative()
		{
			new BadgeCount("calls", -3).Count.Should().Be(0);
		}

		/// <summary>Every shipped provider names a real v4 policy (a typo would hide its badge for everyone) and a unique key.</summary>
		[Test]
		public void every_provider_in_the_services_assembly_has_a_unique_key_and_a_real_policy()
		{
			var providerTypes = typeof(BadgeCountsService).Assembly.GetTypes()
				.Where(t => typeof(IBadgeCountProvider).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
				.ToList();
			var policies = typeof(ResgridResources).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.IsLiteral && f.FieldType == typeof(string))
				.Select(f => (string)f.GetRawConstantValue())
				.ToHashSet();
			var keys = new List<string>();

			providerTypes.Should().NotBeEmpty();

			foreach (var type in providerTypes)
			{
				var provider = (IBadgeCountProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);

				keys.Add(provider.Key);
				if (provider.RequiredPolicy != null)
					policies.Should().Contain(provider.RequiredPolicy, $"{type.Name} must name a ResgridResources policy");
			}

			keys.Should().OnlyHaveUniqueItems();
			keys.Should().Contain(new[] { BadgeCountKeys.Calls, BadgeCountKeys.Messages, BadgeCountKeys.Chats, BadgeCountKeys.Weather });
		}
	}

	[TestFixture]
	[NonParallelizable]
	public class BadgeCountProviderTests
	{
		private const int DepartmentId = 4;
		private const string UserId = "member-a";

		private bool _previousCacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_previousCacheEnabled = Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = _previousCacheEnabled;
		}

		private static BadgeCountContext Member(int? unitId = null) => new BadgeCountContext { DepartmentId = DepartmentId, UserId = UserId, ActiveUnitId = unitId };

		[Test]
		public async Task calls_department_wide_viewers_get_the_count_query_not_the_call_list()
		{
			var calls = new Mock<ICallsService>();
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(DispatchScope.DepartmentWide(DepartmentId, UserId, DispatchScopeReasons.ScopingDisabled));
			calls.Setup(x => x.GetActiveCallsForDepartmentAsync(DepartmentId)).ReturnsAsync(7);

			var count = await new CallsBadgeCountProvider(calls.Object, scope.Object, Mock.Of<ICacheProvider>()).GetCountAsync(Member());

			count.Count.Should().Be(7);
			calls.Verify(x => x.GetActiveCallsByDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task calls_group_scoped_members_count_only_what_dispatch_scope_shows_them()
		{
			var all = new List<Call> { new Call { CallId = 1 }, new Call { CallId = 2 }, new Call { CallId = 3 } };
			var calls = new Mock<ICallsService>();
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(new DispatchScope { DepartmentId = DepartmentId, UserId = UserId });
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(all);
			scope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, all)).ReturnsAsync(new List<Call> { all[1] });

			var count = await new CallsBadgeCountProvider(calls.Object, scope.Object, Mock.Of<ICacheProvider>()).GetCountAsync(Member());

			count.Count.Should().Be(1);
			calls.Verify(x => x.GetActiveCallsForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task calls_cached_counts_key_on_the_department_calls_version()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = true;
			var calls = new Mock<ICallsService>();
			var scope = new Mock<IDispatchScopeService>();
			var cache = new Mock<ICacheProvider>();
			scope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(DispatchScope.DepartmentWide(DepartmentId, UserId, DispatchScopeReasons.ScopingDisabled));
			cache.Setup(x => x.GetStringAsync(BadgeCountCacheKeys.OpenCallsVersion(DepartmentId))).ReturnsAsync("v2");
			cache.Setup(x => x.RetrieveAsync(BadgeCountCacheKeys.OpenCalls(DepartmentId, "all", "v2"), It.IsAny<Func<Task<int>>>(), BadgeCountCacheKeys.CountCacheLength)).ReturnsAsync(4);

			var count = await new CallsBadgeCountProvider(calls.Object, scope.Object, cache.Object).GetCountAsync(Member());

			count.Count.Should().Be(4);
			calls.Verify(x => x.GetActiveCallsForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task messages_count_the_unread_inbox()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(x => x.GetUnreadMessagesCountByUserIdAsync(UserId)).ReturnsAsync(3);

			(await new MessagesBadgeCountProvider(messages.Object).GetCountAsync(Member())).Count.Should().Be(3);
		}

		private static (ChatsBadgeCountProvider provider, Mock<IChatChannelService> channels, Mock<IChatPermissionService> permissions, Mock<IFeatureToggleService> flags) Chat()
		{
			var channels = new Mock<IChatChannelService>();
			var permissions = new Mock<IChatPermissionService>();
			var authorization = new Mock<IAuthorizationService>();
			var flags = new Mock<IFeatureToggleService>();
			authorization.Setup(x => x.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(true);
			flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			return (new ChatsBadgeCountProvider(channels.Object, permissions.Object, authorization.Object, flags.Object, Mock.Of<ICacheProvider>()), channels, permissions, flags);
		}

		[Test]
		public async Task chats_count_nothing_when_chat_is_off_for_the_department()
		{
			var (provider, channels, _, flags) = Chat();
			flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			(await provider.GetCountAsync(Member())).Should().BeNull();
			channels.Verify(x => x.GetUnreadMessageCountForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
		}

		[Test]
		public async Task chats_count_the_active_unit_only_when_the_member_crews_it()
		{
			var (provider, channels, permissions, _) = Chat();
			permissions.Setup(x => x.CanSendAsUnitAsync(UserId, 12, DepartmentId)).ReturnsAsync(false);
			permissions.Setup(x => x.CanSendAsUnitAsync(UserId, 13, DepartmentId)).ReturnsAsync(true);
			channels.Setup(x => x.GetUnreadMessageCountForUserAsync(DepartmentId, UserId, null)).ReturnsAsync(5);
			channels.Setup(x => x.GetUnreadMessageCountForUserAsync(DepartmentId, UserId, 13)).ReturnsAsync(9);

			(await provider.GetCountAsync(Member(12))).Count.Should().Be(5);
			(await provider.GetCountAsync(Member(13))).Count.Should().Be(9);
		}

		private static CheckInTimerStatus Timer(string status) => new CheckInTimerStatus { Status = status };

		/// <summary>The timer service reports "Green", "Warning" and "Critical" (past due); the badge counts the last two.</summary>
		[Test]
		public async Task check_ins_count_warning_and_critical_timers_in_one_batch()
		{
			var active = new List<Call>
			{
				new Call { CallId = 1, DepartmentId = DepartmentId, CheckInTimersEnabled = true },
				new Call { CallId = 2, DepartmentId = DepartmentId, CheckInTimersEnabled = true },
				new Call { CallId = 3, DepartmentId = DepartmentId, CheckInTimersEnabled = false }
			};
			var calls = new Mock<ICallsService>();
			var scope = new Mock<IDispatchScopeService>();
			var timers = new Mock<ICheckInTimerService>();
			scope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(DispatchScope.DepartmentWide(DepartmentId, UserId, DispatchScopeReasons.ScopingDisabled));
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(active);
			timers.Setup(x => x.GetActiveTimerStatusesForCallsAsync(It.IsAny<IEnumerable<Call>>())).ReturnsAsync(new Dictionary<int, List<CheckInTimerStatus>>
			{
				[1] = new List<CheckInTimerStatus> { Timer("Green"), Timer("Warning"), Timer("Critical") },
				[2] = new List<CheckInTimerStatus> { Timer("Critical"), null }
			});

			var count = await new CheckInsBadgeCountProvider(calls.Object, scope.Object, timers.Object, Mock.Of<ICacheProvider>()).GetCountAsync(Member());

			count.Count.Should().Be(3);
			timers.Verify(x => x.GetActiveTimerStatusesForCallsAsync(It.Is<IEnumerable<Call>>(batch => batch.Select(c => c.CallId).SequenceEqual(new[] { 1, 2 }))), Times.Once);
			timers.Verify(x => x.GetActiveTimerStatusesForCallAsync(It.IsAny<Call>()), Times.Never);
		}

		[Test]
		public async Task check_ins_read_no_timers_when_no_visible_call_has_them()
		{
			var active = new List<Call> { new Call { CallId = 1, DepartmentId = DepartmentId, CheckInTimersEnabled = true } };
			var calls = new Mock<ICallsService>();
			var scope = new Mock<IDispatchScopeService>();
			var timers = new Mock<ICheckInTimerService>();
			scope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(new DispatchScope { DepartmentId = DepartmentId, UserId = UserId });
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(active);
			scope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>())).ReturnsAsync(new List<Call>());

			var count = await new CheckInsBadgeCountProvider(calls.Object, scope.Object, timers.Object, Mock.Of<ICacheProvider>()).GetCountAsync(Member());

			count.Count.Should().Be(0);
			timers.VerifyNoOtherCalls();
		}

		[Test]
		public async Task weather_sums_extreme_and_severe_and_keeps_them_apart()
		{
			var weather = new Mock<IWeatherAlertService>();
			weather.Setup(x => x.GetActiveSevereAlertCountsAsync(DepartmentId, false)).ReturnsAsync(new WeatherAlertSeverityCounts { Extreme = 1, Severe = 2 });

			var count = await new WeatherBadgeCountProvider(weather.Object).GetCountAsync(Member());

			count.Count.Should().Be(3);
			count.Breakdown.Should().Equal(new Dictionary<string, int> { ["extreme"] = 1, ["severe"] = 2 });
		}
	}
}
