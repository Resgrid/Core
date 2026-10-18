using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The deferred effects of import writes (wizard plan section 11.5): once per chunk, one invalidation per cache family
	/// and one SecurityRefreshEvent per visibility type touched; once per run, the apps resync, one Unified Search
	/// department rebuild request, a full department cache clear and one summary audit per entity. Every step is best
	/// effort: a Redis or broker failure is logged and the reconcile phase clears the department's caches again.
	/// </summary>
	public class ImportEffectsRunner
	{
		private readonly IEventAggregator _events;
		private readonly IDepartmentsService _departments;
		private readonly IUsersService _users;
		private readonly IUserProfileService _profiles;
		private readonly ILimitsService _limits;
		private readonly ICustomStateService _customStates;
		private readonly ICallsService _calls;
		private readonly ISearchIndexMaintenanceService _search;
		private readonly ISystemOperationsService _systemOperations;

		public ImportEffectsRunner(IEventAggregator events, IDepartmentsService departments, IUsersService users, IUserProfileService profiles, ILimitsService limits,
			ICustomStateService customStates, ICallsService calls, ISearchIndexMaintenanceService search, ISystemOperationsService systemOperations)
		{
			_events = events;
			_departments = departments;
			_users = users;
			_profiles = profiles;
			_limits = limits;
			_customStates = customStates;
			_calls = calls;
			_search = search;
			_systemOperations = systemOperations;
		}

		/// <summary>After a chunk commits (or is discarded, so no cache keeps a row that never committed).</summary>
		public async Task RunChunkEffectsAsync(int departmentId, ImportEffects touched)
		{
			if (touched == ImportEffects.None)
				return;

			var refresh = new HashSet<SecurityCacheTypes>();
			if (touched.HasFlag(ImportEffects.PersonnelVisibility) || touched.HasFlag(ImportEffects.RoleVisibility))
			{
				refresh.Add(SecurityCacheTypes.WhoCanViewPersonnel);
				refresh.Add(SecurityCacheTypes.WhoCanViewPersonnelLocations);
			}
			if (touched.HasFlag(ImportEffects.UnitVisibility))
			{
				refresh.Add(SecurityCacheTypes.WhoCanViewUnits);
				refresh.Add(SecurityCacheTypes.WhoCanViewUnitLocations);
			}
			if (touched.HasFlag(ImportEffects.GroupVisibility))
				foreach (SecurityCacheTypes type in Enum.GetValues(typeof(SecurityCacheTypes)))
					refresh.Add(type);

			foreach (var type in refresh)
				Try(() => _events.SendMessage(new SecurityRefreshEvent { DepartmentId = departmentId, Type = type }));

			if (touched.HasFlag(ImportEffects.DepartmentUsersCache))
			{
				Try(() => _departments.InvalidateDepartmentUsersInCache(departmentId));
				Try(() => _departments.InvalidatePersonnelNamesInCache(departmentId));
				Try(() => _departments.InvalidateDepartmentMembers());
				Try(() => _users.ClearCacheForDepartment(departmentId));
			}
			if (touched.HasFlag(ImportEffects.ProfilesCache))
				Try(() => _profiles.ClearAllUserProfilesFromCache(departmentId));
			if (touched.HasFlag(ImportEffects.LimitsCache))
				await TryAsync(() => _limits.InvalidateDepartmentsEntityLimitsCache(departmentId));
			if (touched.HasFlag(ImportEffects.CustomStatesCache))
				Try(() => _customStates.InvalidateCustomStateInCache(departmentId));
			if (touched.HasFlag(ImportEffects.CallTypesCache))
				Try(() => _calls.InvalidateCallPrioritiesForDepartmentInCache(departmentId));
		}

		/// <summary>After the last chunk of the run.</summary>
		public async Task RunRunEffectsAsync(int departmentId, ImportEffects touched, CancellationToken cancellationToken)
		{
			if (touched.HasFlag(ImportEffects.AppsResync))
				Try(() => _events.SendMessage(new DepartmentSettingsUpdateEvent { DepartmentId = departmentId }));
			if (touched.HasFlag(ImportEffects.SearchRebuild))
				await TryAsync(() => _search.RequestRebuildAsync(departmentId, cancellationToken));
			await ClearDepartmentCachesAsync(departmentId);
		}

		public async Task ClearDepartmentCachesAsync(int departmentId)
		{
			try
			{
				var failed = await _systemOperations.ClearDepartmentCachesAsync(departmentId);
				if (failed?.Count > 0)
					Logging.LogInfo($"Department import: cache groups not cleared for department {departmentId}: {string.Join(", ", failed)}");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		/// <summary>One summary audit per entity per run instead of one audit event per row (section 11.2.2).</summary>
		public void Audit(int departmentId, string userId, AuditLogTypes type, string summaryJson)
		{
			Try(() => _events.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = summaryJson,
				Successful = true,
				ServerName = Environment.MachineName
			}));
		}

		private static void Try(Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private static async Task TryAsync(Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}
