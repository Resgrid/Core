using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Live ETAs of navigating units (maplibre-mapping-routing-navigation-plan.md §9.4). Each unit's latest report is one
	/// Redis key that expires two minutes after it was written; a per-department index of unit ids lets a screen that
	/// opens mid-navigation find them. Changes go out over the eventing topic as <c>unitNavigationUpdated</c>.
	/// </summary>
	public class UnitNavigationStateService : IUnitNavigationStateService
	{
		public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(2);
		private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(10);

		private const string StateKey = "UnitNav1_{0}_{1}";
		private const string IndexKey = "UnitNavIdx1_{0}";

		// Base types that end a response: the unit arrived, was released or went back in service.
		private static readonly HashSet<int> ResponseEndingBaseTypes = new HashSet<int>
		{
			(int)ActionBaseTypes.Available,
			(int)ActionBaseTypes.OnScene,
			(int)ActionBaseTypes.Cleared,
			(int)ActionBaseTypes.Returning,
			(int)ActionBaseTypes.Unavailable,
			(int)ActionBaseTypes.InQuarters
		};

		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;

		public UnitNavigationStateService(ICacheProvider cacheProvider, IEventAggregator eventAggregator)
		{
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
		}

		public async Task<UnitNavigationState> ReportAsync(UnitNavigationState report)
		{
			if (report == null || report.DepartmentId <= 0 || report.UnitId <= 0)
				throw new ArgumentException("A navigation report needs a department and a unit.", nameof(report));

			report.ReportedOn = DateTime.UtcNow;

			if (report.State == (int)UnitNavigationStates.Arrived || report.State == (int)UnitNavigationStates.Ended)
			{
				await _cacheProvider.RemoveAsync(Key(report.DepartmentId, report.UnitId));
				await AnnounceAsync(report);
				return report;
			}

			await _cacheProvider.SetStringAsync(Key(report.DepartmentId, report.UnitId), JsonSerializer.Serialize(report), StateLifetime);
			await AddToIndexAsync(report.DepartmentId, report.UnitId);
			await AnnounceAsync(report);
			return report;
		}

		public async Task<UnitNavigationState> GetForUnitAsync(int departmentId, int unitId)
		{
			var state = Parse(await _cacheProvider.GetStringAsync(Key(departmentId, unitId)));
			return state != null && state.DepartmentId == departmentId && IsLive(state) ? state : null;
		}

		public async Task<List<UnitNavigationState>> GetActiveAsync(int departmentId)
		{
			var unitIds = await ReadIndexAsync(departmentId);
			var states = new List<UnitNavigationState>();

			foreach (var unitId in unitIds)
			{
				var state = await GetForUnitAsync(departmentId, unitId);
				if (state != null)
					states.Add(state);
			}

			// Drop units whose state expired so the index stays small.
			if (states.Count != unitIds.Count)
				await WriteIndexAsync(departmentId, states.Select(s => s.UnitId).ToList());

			return states;
		}

		public async Task ClearAsync(int departmentId, int unitId)
		{
			var current = await GetForUnitAsync(departmentId, unitId);
			if (current == null)
				return;

			await _cacheProvider.RemoveAsync(Key(departmentId, unitId));
			current.State = (int)UnitNavigationStates.Ended;
			current.ReportedOn = DateTime.UtcNow;
			await AnnounceAsync(current);
		}

		public async Task ClearForUnitStatusAsync(int departmentId, int unitId, int statusBaseType)
		{
			if (!ResponseEndingBaseTypes.Contains(statusBaseType))
				return;

			var current = await GetForUnitAsync(departmentId, unitId);

			// Route navigation is not a response: a status change does not end it.
			if (current == null || !current.CallId.HasValue)
				return;

			await ClearAsync(departmentId, unitId);
		}

		private static bool IsLive(UnitNavigationState state)
		{
			return DateTime.UtcNow - state.ReportedOn <= StateLifetime;
		}

		private async Task AnnounceAsync(UnitNavigationState state)
		{
			try
			{
				var payload = JsonSerializer.Serialize(new
				{
					unitId = state.UnitId,
					callId = state.CallId,
					routeInstanceId = state.RouteInstanceId,
					state = state.State
				});

				await _eventAggregator.SendMessageAsync(new UnitNavigationUpdatedEvent { DepartmentId = state.DepartmentId, UnitId = state.UnitId, Payload = payload });
			}
			catch (Exception ex)
			{
				// The state is stored; a broker outage only delays screens until their next reload.
				Logging.LogException(ex, $"Announcing navigation of unit {state.UnitId}");
			}
		}

		private async Task AddToIndexAsync(int departmentId, int unitId)
		{
			var unitIds = await ReadIndexAsync(departmentId);
			if (unitIds.Contains(unitId))
			{
				// Keep the index alive as long as anyone navigates.
				await WriteIndexAsync(departmentId, unitIds);
				return;
			}

			unitIds.Add(unitId);
			await WriteIndexAsync(departmentId, unitIds);
		}

		private async Task<List<int>> ReadIndexAsync(int departmentId)
		{
			try
			{
				var text = await _cacheProvider.GetStringAsync(string.Format(IndexKey, departmentId));
				return string.IsNullOrWhiteSpace(text) ? new List<int>() : JsonSerializer.Deserialize<List<int>>(text) ?? new List<int>();
			}
			catch (JsonException)
			{
				return new List<int>();
			}
		}

		private Task<bool> WriteIndexAsync(int departmentId, List<int> unitIds)
		{
			return unitIds.Count == 0
				? _cacheProvider.RemoveAsync(string.Format(IndexKey, departmentId))
				: _cacheProvider.SetStringAsync(string.Format(IndexKey, departmentId), JsonSerializer.Serialize(unitIds.Distinct().ToList()), IndexLifetime);
		}

		private static UnitNavigationState Parse(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			try
			{
				return JsonSerializer.Deserialize<UnitNavigationState>(text);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static string Key(int departmentId, int unitId)
		{
			return string.Format(StateKey, departmentId, unitId);
		}
	}
}
