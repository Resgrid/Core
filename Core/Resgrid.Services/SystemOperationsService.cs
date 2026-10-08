using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The request side of BackOffice -> System Operations and the bookkeeping worker 76 does around each run. The
	/// operations themselves run in the worker (SystemOperationRunner); only the department cache clear lives here,
	/// because it is nothing but service invalidations.
	/// </summary>
	public class SystemOperationsService : ISystemOperationsService
	{
		/// <summary>
		/// Written once into a cache that holds its data and touched by every worker check after that. Finding it missing
		/// means the cache lost everything (a restart without persistence, a flush, a failover to an empty replica).
		/// </summary>
		public const string CacheSentinelKey = "SystemOperations_CacheSentinel";

		/// <summary>Sliding: every check resets it, so only a cache that lost its data (or a worker down this long) misses it.</summary>
		public static readonly TimeSpan CacheSentinelLifetime = TimeSpan.FromDays(30);

		/// <summary>The running worker heartbeats every minute; five missed beats in a row means it stopped.</summary>
		public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(5);

		public const string SystemRequester = "system";

		private const int MaxRecentRequests = 500;

		private readonly ISystemOperationRequestsRepository _requestsRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly ICustomStateService _customStateService;
		private readonly IUserStateService _userStateService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly TimeProvider _timeProvider;

		public SystemOperationsService(ISystemOperationRequestsRepository requestsRepository, ICacheProvider cacheProvider,
			IDepartmentsService departmentsService, ISubscriptionsService subscriptionsService, IDepartmentGroupsService departmentGroupsService,
			ICallsService callsService, IActionLogsService actionLogsService, ICustomStateService customStateService,
			IUserStateService userStateService, IFeatureToggleService featureToggleService, TimeProvider timeProvider)
		{
			_requestsRepository = requestsRepository;
			_cacheProvider = cacheProvider;
			_departmentsService = departmentsService;
			_subscriptionsService = subscriptionsService;
			_departmentGroupsService = departmentGroupsService;
			_callsService = callsService;
			_actionLogsService = actionLogsService;
			_customStateService = customStateService;
			_userStateService = userStateService;
			_featureToggleService = featureToggleService;
			_timeProvider = timeProvider ?? TimeProvider.System;
		}

		private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

		public async Task<SystemOperationRequestResult> RequestAsync(SystemOperationTypes operationType, int? targetDepartmentId,
			SystemOperationSources source, string requestedBy, string reason, CancellationToken cancellationToken = default)
		{
			var descriptor = SystemOperationCatalog.Get(operationType);

			if (descriptor == null)
				return new SystemOperationRequestResult { Error = $"Unknown system operation {(int)operationType}." };

			if (string.IsNullOrWhiteSpace(requestedBy))
				return new SystemOperationRequestResult { Error = "The requester is required." };

			if (targetDepartmentId.HasValue)
			{
				if (!descriptor.SupportsDepartmentScope)
					return new SystemOperationRequestResult { Error = $"{descriptor.Name} always covers the whole system; it cannot target one department." };

				if (targetDepartmentId.Value <= 0 || await _departmentsService.GetDepartmentByIdAsync(targetDepartmentId.Value) == null)
					return new SystemOperationRequestResult { Error = $"Department {targetDepartmentId.Value} does not exist." };
			}

			// A waiting request covers this one: it has not started, so it will read the same current state when it runs.
			// A running one does not: whatever changed since it started is why someone asked again.
			var pending = await _requestsRepository.GetPendingAsync((int)operationType, targetDepartmentId);

			if (pending != null)
				return new SystemOperationRequestResult { Created = false, Request = pending };

			var request = new SystemOperationRequest
			{
				SystemOperationRequestId = Guid.NewGuid().ToString(),
				OperationType = (int)operationType,
				TargetDepartmentId = targetDepartmentId,
				Status = (int)SystemOperationStatuses.Pending,
				Source = (int)source,
				RequestedBy = Truncate(requestedBy.Trim(), 256),
				Reason = Truncate(reason?.Trim(), SystemOperationRequest.ReasonMaxLength),
				RequestedOn = UtcNow
			};

			request = await _requestsRepository.InsertAsync(request, cancellationToken);

			return new SystemOperationRequestResult { Created = true, Request = request };
		}

		public async Task<List<SystemOperationRequest>> GetRecentRequestsAsync(int count = 50)
		{
			return await _requestsRepository.GetRecentAsync(Math.Clamp(count, 1, MaxRecentRequests));
		}

		public async Task<SystemOperationRequest> GetRequestByIdAsync(string requestId)
		{
			if (string.IsNullOrWhiteSpace(requestId))
				return null;

			return await _requestsRepository.GetByIdAsync(requestId);
		}

		public async Task<bool> CancelPendingRequestAsync(string requestId, string cancelledBy, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(requestId))
				return false;

			return await _requestsRepository.CancelPendingAsync(requestId, cancelledBy, UtcNow, cancellationToken);
		}

		public async Task<SystemOperationRequest> ClaimNextRequestAsync(string workerName, CancellationToken cancellationToken = default)
		{
			return await _requestsRepository.ClaimNextPendingAsync(workerName, UtcNow, cancellationToken);
		}

		public async Task<bool> ReportProgressAsync(string requestId, string progress, CancellationToken cancellationToken = default)
		{
			return await _requestsRepository.HeartbeatAsync(requestId, progress, UtcNow, cancellationToken);
		}

		public async Task<bool> CompleteRequestAsync(string requestId, bool succeeded, string result, CancellationToken cancellationToken = default)
		{
			var status = succeeded ? SystemOperationStatuses.Completed : SystemOperationStatuses.Failed;

			return await _requestsRepository.FinishAsync(requestId, (int)status, result, UtcNow, cancellationToken);
		}

		public async Task<int> FailAbandonedRequestsAsync(CancellationToken cancellationToken = default)
		{
			var now = UtcNow;

			return await _requestsRepository.FailAbandonedAsync(now - AbandonedAfter,
				"The worker running this request stopped before it finished (restart or crash). Request it again.", now, cancellationToken);
		}

		public async Task<bool> DetectCacheDataLossAsync()
		{
			if (!Config.SystemBehaviorConfig.CacheEnabled || !_cacheProvider.IsConnected())
				return false;

			var marker = UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" + Guid.NewGuid().ToString("N");
			var stored = await _cacheProvider.GetOrAddStringAsync(CacheSentinelKey, marker, CacheSentinelLifetime);

			// Null is "unreachable", not "missing". Our own marker coming back means the key was absent and is now ours, so
			// exactly one concurrent checker sees the loss.
			return stored != null && string.Equals(stored, marker, StringComparison.Ordinal);
		}

		public async Task<SystemOperationsCacheStatus> GetCacheStatusAsync()
		{
			var status = new SystemOperationsCacheStatus
			{
				CacheEnabled = Config.SystemBehaviorConfig.CacheEnabled,
				Connected = _cacheProvider.IsConnected()
			};

			if (!status.CacheEnabled || !status.Connected)
				return status;

			status.DataPresentSinceUtc = ParseSentinel(await _cacheProvider.GetStringAsync(CacheSentinelKey));

			return status;
		}

		public async Task<List<string>> ClearDepartmentCachesAsync(int departmentId)
		{
			// Best effort: one failing group must not keep the rest stale, so each is tried on its own. Mirrors the
			// BackOffice department page's "Clear caches" list.
			var failed = new List<string>();

			await StepAsync("department, users, personnel names, entity limits", () => _departmentsService.InvalidateAllDepartmentsCache(departmentId));
			Step("current payment and plan", () => _subscriptionsService.ClearCacheForCurrentPayment(departmentId));
			await StepAsync("groups", async () =>
			{
				foreach (var group in await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId) ?? new List<DepartmentGroup>())
					await _departmentGroupsService.InvalidateGroupInCache(group.DepartmentGroupId);
			});
			Step("call priorities", () => _callsService.InvalidateCallPrioritiesForDepartmentInCache(departmentId));
			Step("action logs", () => _actionLogsService.InvalidateActionLogs(departmentId));
			Step("custom states", () => _customStateService.InvalidateCustomStateInCache(departmentId));
			Step("latest personnel states", () => _userStateService.InvalidateLatestStatesForDepartmentCache(departmentId));
			await StepAsync("feature-flag overrides", () => _featureToggleService.InvalidateDepartmentOverrideCacheAsync(departmentId));

			return failed;

			void Step(string name, Action invalidate)
			{
				try
				{
					invalidate();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Clearing the {name} cache failed for department {departmentId}.");
					failed.Add(name);
				}
			}

			async Task StepAsync(string name, Func<Task> invalidate)
			{
				try
				{
					await invalidate();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Clearing the {name} cache failed for department {departmentId}.");
					failed.Add(name);
				}
			}
		}

		/// <summary>The time half of a sentinel value ("{utc O}|{nonce}"), or null when it is missing or not ours.</summary>
		public static DateTime? ParseSentinel(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var separator = value.IndexOf('|');
			var time = separator > 0 ? value.Substring(0, separator) : value;

			return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
				? parsed.ToUniversalTime()
				: null;
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
