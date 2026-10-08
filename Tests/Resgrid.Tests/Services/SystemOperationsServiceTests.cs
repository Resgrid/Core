using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class SystemOperationsServiceTests
	{
		private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

		private Mock<ISystemOperationRequestsRepository> _requests;
		private Mock<ICacheProvider> _cache;
		private Mock<IDepartmentsService> _departments;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<ICallsService> _calls;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<ICustomStateService> _customStates;
		private Mock<IUserStateService> _userStates;
		private Mock<IFeatureToggleService> _featureToggles;
		private SystemOperationsService _service;
		private bool _originalCacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = true;

			_requests = new Mock<ISystemOperationRequestsRepository>();
			_requests.Setup(x => x.InsertAsync(It.IsAny<SystemOperationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SystemOperationRequest r, CancellationToken _, bool _) => r);
			_cache = new Mock<ICacheProvider>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(12, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 12 });
			_subscriptions = new Mock<ISubscriptionsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(x => x.GetAllGroupsForDepartmentUnlimitedThinAsync(It.IsAny<int>())).ReturnsAsync(new List<DepartmentGroup>());
			_calls = new Mock<ICallsService>();
			_actionLogs = new Mock<IActionLogsService>();
			_customStates = new Mock<ICustomStateService>();
			_userStates = new Mock<IUserStateService>();
			_featureToggles = new Mock<IFeatureToggleService>();

			_service = new SystemOperationsService(_requests.Object, _cache.Object, _departments.Object, _subscriptions.Object, _groups.Object,
				_calls.Object, _actionLogs.Object, _customStates.Object, _userStates.Object, _featureToggles.Object, new FixedTimeProvider(Now));
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
		}

		[Test]
		public async Task Request_queues_a_pending_row_with_the_requester_and_reason()
		{
			var result = await _service.RequestAsync(SystemOperationTypes.RebuildSecurityMatrices, null, SystemOperationSources.BackOffice,
				"  ops@resgrid.com ", "  Redis outage recovery  ");

			result.Succeeded.Should().BeTrue();
			result.Created.Should().BeTrue();
			result.Request.Status.Should().Be((int)SystemOperationStatuses.Pending);
			result.Request.OperationType.Should().Be((int)SystemOperationTypes.RebuildSecurityMatrices);
			result.Request.Source.Should().Be((int)SystemOperationSources.BackOffice);
			result.Request.RequestedBy.Should().Be("ops@resgrid.com");
			result.Request.Reason.Should().Be("Redis outage recovery");
			result.Request.RequestedOn.Should().Be(Now.UtcDateTime);
			result.Request.TargetDepartmentId.Should().BeNull();
			Guid.TryParse(result.Request.SystemOperationRequestId, out _).Should().BeTrue();
		}

		[Test]
		public async Task Request_matching_one_already_waiting_returns_it_instead_of_queueing_twice()
		{
			var waiting = new SystemOperationRequest { SystemOperationRequestId = "waiting", Status = (int)SystemOperationStatuses.Pending };
			_requests.Setup(x => x.GetPendingAsync((int)SystemOperationTypes.ClearDepartmentCaches, 12)).ReturnsAsync(waiting);

			var result = await _service.RequestAsync(SystemOperationTypes.ClearDepartmentCaches, 12, SystemOperationSources.BackOffice, "ops", "stale");

			result.Succeeded.Should().BeTrue();
			result.Created.Should().BeFalse();
			result.Request.Should().BeSameAs(waiting);
			_requests.Verify(x => x.InsertAsync(It.IsAny<SystemOperationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Request_refuses_a_department_target_for_a_system_wide_operation()
		{
			var result = await _service.RequestAsync(SystemOperationTypes.ChatRetention, 12, SystemOperationSources.BackOffice, "ops", "why");

			result.Succeeded.Should().BeFalse();
			result.Error.Should().Contain("cannot target one department");
			_requests.Verify(x => x.InsertAsync(It.IsAny<SystemOperationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Request_refuses_a_department_that_does_not_exist()
		{
			var result = await _service.RequestAsync(SystemOperationTypes.RebuildSecurityMatrices, 99, SystemOperationSources.BackOffice, "ops", "why");

			result.Succeeded.Should().BeFalse();
			result.Error.Should().Contain("99");
		}

		[Test]
		public async Task Request_refuses_an_operation_the_catalog_does_not_know()
		{
			var result = await _service.RequestAsync((SystemOperationTypes)999, null, SystemOperationSources.BackOffice, "ops", "why");

			result.Succeeded.Should().BeFalse();
			result.Error.Should().Contain("999");
		}

		[Test]
		public async Task Request_with_a_department_records_the_target()
		{
			var result = await _service.RequestAsync(SystemOperationTypes.RebuildSecurityMatrices, 12, SystemOperationSources.BackOffice, "ops", "one department");

			result.Created.Should().BeTrue();
			result.Request.TargetDepartmentId.Should().Be(12);
		}

		[Test]
		public async Task Request_trims_an_overlong_reason_to_the_column_size()
		{
			var result = await _service.RequestAsync(SystemOperationTypes.ReportingRollup, null, SystemOperationSources.BackOffice, "ops",
				new string('r', SystemOperationRequest.ReasonMaxLength + 50));

			result.Request.Reason.Length.Should().Be(SystemOperationRequest.ReasonMaxLength);
		}

		[Test]
		public async Task Complete_records_failed_or_completed()
		{
			await _service.CompleteRequestAsync("a", true, "done");
			await _service.CompleteRequestAsync("b", false, "boom");

			_requests.Verify(x => x.FinishAsync("a", (int)SystemOperationStatuses.Completed, "done", Now.UtcDateTime, It.IsAny<CancellationToken>()));
			_requests.Verify(x => x.FinishAsync("b", (int)SystemOperationStatuses.Failed, "boom", Now.UtcDateTime, It.IsAny<CancellationToken>()));
		}

		[Test]
		public async Task Abandoned_requests_are_those_without_a_heartbeat_for_the_abandon_window()
		{
			await _service.FailAbandonedRequestsAsync();

			_requests.Verify(x => x.FailAbandonedAsync(Now.UtcDateTime - SystemOperationsService.AbandonedAfter, It.IsAny<string>(), Now.UtcDateTime,
				It.IsAny<CancellationToken>()));
		}

		[Test]
		public void The_heartbeat_is_well_inside_the_abandon_window()
		{
			// Several missed beats in a row, not one slow one, should be what fails a run.
			(Resgrid.Workers.Console.Tasks.SystemOperationsTask.HeartbeatInterval * 4).Should().BeLessThan(SystemOperationsService.AbandonedAfter);
		}

		[Test]
		public async Task Cache_data_loss_is_detected_when_the_sentinel_comes_back_as_our_own_marker()
		{
			_cache.Setup(x => x.IsConnected()).Returns(true);
			_cache.Setup(x => x.GetOrAddStringAsync(SystemOperationsService.CacheSentinelKey, It.IsAny<string>(), SystemOperationsService.CacheSentinelLifetime))
				.ReturnsAsync((string _, string marker, TimeSpan _) => marker);

			(await _service.DetectCacheDataLossAsync()).Should().BeTrue();
		}

		[Test]
		public async Task Cache_data_loss_is_not_detected_when_the_sentinel_was_already_there()
		{
			_cache.Setup(x => x.IsConnected()).Returns(true);
			_cache.Setup(x => x.GetOrAddStringAsync(SystemOperationsService.CacheSentinelKey, It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync("2026-10-01T00:00:00.0000000Z|abc");

			(await _service.DetectCacheDataLossAsync()).Should().BeFalse();
		}

		[Test]
		public async Task An_unreachable_cache_is_never_reported_as_data_loss()
		{
			_cache.Setup(x => x.IsConnected()).Returns(true);
			_cache.Setup(x => x.GetOrAddStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string)null);

			(await _service.DetectCacheDataLossAsync()).Should().BeFalse();

			_cache.Setup(x => x.IsConnected()).Returns(false);
			(await _service.DetectCacheDataLossAsync()).Should().BeFalse();
		}

		[Test]
		public async Task A_disabled_cache_is_never_checked()
		{
			SystemBehaviorConfig.CacheEnabled = false;
			_cache.Setup(x => x.IsConnected()).Returns(true);

			(await _service.DetectCacheDataLossAsync()).Should().BeFalse();
			_cache.Verify(x => x.GetOrAddStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
		}

		[Test]
		public async Task Cache_status_reports_when_the_cache_has_held_its_data_since()
		{
			_cache.Setup(x => x.IsConnected()).Returns(true);
			_cache.Setup(x => x.GetStringAsync(SystemOperationsService.CacheSentinelKey)).ReturnsAsync("2026-10-07T09:15:00.0000000Z|abc");

			var status = await _service.GetCacheStatusAsync();

			status.CacheEnabled.Should().BeTrue();
			status.Connected.Should().BeTrue();
			status.DataPresentSinceUtc.Should().Be(new DateTime(2026, 10, 7, 9, 15, 0, DateTimeKind.Utc));
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not a time|abc")]
		public void A_missing_or_foreign_sentinel_has_no_time(string value)
		{
			SystemOperationsService.ParseSentinel(value).Should().BeNull();
		}

		[Test]
		public async Task Clearing_department_caches_keeps_going_past_a_failing_group()
		{
			_calls.Setup(x => x.InvalidateCallPrioritiesForDepartmentInCache(12)).Throws(new InvalidOperationException("redis"));
			_groups.Setup(x => x.GetAllGroupsForDepartmentUnlimitedThinAsync(12))
				.ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3 }, new DepartmentGroup { DepartmentGroupId = 4 } });

			var failed = await _service.ClearDepartmentCachesAsync(12);

			failed.Should().ContainSingle().Which.Should().Be("call priorities");
			_departments.Verify(x => x.InvalidateAllDepartmentsCache(12));
			_subscriptions.Verify(x => x.ClearCacheForCurrentPayment(12));
			_groups.Verify(x => x.InvalidateGroupInCache(3));
			_groups.Verify(x => x.InvalidateGroupInCache(4));
			_actionLogs.Verify(x => x.InvalidateActionLogs(12));
			_customStates.Verify(x => x.InvalidateCustomStateInCache(12));
			_userStates.Verify(x => x.InvalidateLatestStatesForDepartmentCache(12));
			_featureToggles.Verify(x => x.InvalidateDepartmentOverrideCacheAsync(12));
		}

		private sealed class FixedTimeProvider : TimeProvider
		{
			private readonly DateTimeOffset _now;

			public FixedTimeProvider(DateTimeOffset now)
			{
				_now = now;
			}

			public override DateTimeOffset GetUtcNow() => _now;
		}
	}
}
