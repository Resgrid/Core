using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="ICallGroupAssignmentService" />
	public class CallGroupAssignmentService : ICallGroupAssignmentService
	{
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly ICallDispatchesRepository _callDispatchesRepository;
		private readonly ICallDispatchGroupRepository _callDispatchGroupRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;

		public CallGroupAssignmentService(IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService,
			ICallDispatchesRepository callDispatchesRepository, ICallDispatchGroupRepository callDispatchGroupRepository,
			ICallDispatchUnitRepository callDispatchUnitRepository)
		{
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_callDispatchesRepository = callDispatchesRepository;
			_callDispatchGroupRepository = callDispatchGroupRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
		}

		public async Task<Dictionary<int, CallGroupAssignment>> AssignCallsToGroupsAsync(int departmentId, IReadOnlyCollection<Call> calls,
			IReadOnlyDictionary<int, CallGroupResources> responders = null)
		{
			var result = new Dictionary<int, CallGroupAssignment>();
			var departmentCalls = calls?.Where(x => x != null && x.DepartmentId == departmentId)
				.GroupBy(x => x.CallId).Select(g => g.First()).ToList() ?? new List<Call>();
			if (departmentCalls.Count == 0)
				return result;

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(departmentId) ?? new List<DepartmentGroup>();
			// Past calls: the units that worked them, deleted ones included.
			var units = await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(departmentId) ?? new List<Unit>();
			var assigner = new CallGroupAssigner(groups, units);

			var dispatched = departmentCalls.ToDictionary(x => x.CallId, x => new CallGroupResources());
			var loggedFrom = departmentCalls.Min(x => x.LoggedOn);
			var loggedTo = departmentCalls.Max(x => x.LoggedOn);

			foreach (var dispatch in await _callDispatchesRepository.GetCallDispatchesForCallsInRangeAsync(departmentId, loggedFrom, loggedTo) ?? Enumerable.Empty<CallDispatch>())
			{
				if (dispatch != null && !string.IsNullOrWhiteSpace(dispatch.UserId) && dispatched.TryGetValue(dispatch.CallId, out var resources))
					resources.UserIds.Add(dispatch.UserId);
			}

			foreach (var dispatch in await _callDispatchGroupRepository.GetCallDispatchGroupsForCallsInRangeAsync(departmentId, loggedFrom, loggedTo) ?? Enumerable.Empty<CallDispatchGroup>())
			{
				if (dispatch != null && dispatched.TryGetValue(dispatch.CallId, out var resources))
					resources.GroupIds.Add(dispatch.DepartmentGroupId);
			}

			foreach (var dispatch in await _callDispatchUnitRepository.GetCallUnitDispatchesForCallsInRangeAsync(departmentId, loggedFrom, loggedTo) ?? Enumerable.Empty<CallDispatchUnit>())
			{
				if (dispatch != null && dispatched.TryGetValue(dispatch.CallId, out var resources))
					resources.UnitIds.Add(dispatch.UnitId);
			}

			foreach (var call in departmentCalls)
			{
				CallGroupResources responded = null;
				responders?.TryGetValue(call.CallId, out responded);

				result[call.CallId] = assigner.Assign(call.CallId, call.GeoLocationData, dispatched[call.CallId], responded);
			}

			return result;
		}
	}
}
