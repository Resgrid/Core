using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Destination rules (<see cref="IDestinationRulesService"/>).</summary>
	public class DestinationRulesService : IDestinationRulesService
	{
		private readonly IDestinationRulesRepository _destinationRulesRepository;
		private readonly IMappingService _mappingService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;

		public DestinationRulesService(IDestinationRulesRepository destinationRulesRepository, IMappingService mappingService, ICallsService callsService,
			IUnitsService unitsService)
		{
			_destinationRulesRepository = destinationRulesRepository;
			_mappingService = mappingService;
			_callsService = callsService;
			_unitsService = unitsService;
		}

		public async Task<List<DestinationRule>> GetRulesForDepartmentAsync(int departmentId)
		{
			var rules = await _destinationRulesRepository.GetAllByDepartmentIdAsync(departmentId);

			return DestinationRuleResolver.Order((rules ?? Enumerable.Empty<DestinationRule>()).Where(x => x != null && x.DepartmentId == departmentId));
		}

		public async Task<DestinationRule> GetRuleByIdAsync(int departmentId, string destinationRuleId)
		{
			if (string.IsNullOrWhiteSpace(destinationRuleId))
				return null;

			var rule = await _destinationRulesRepository.GetByIdAsync(destinationRuleId.Trim());

			return rule != null && rule.DepartmentId == departmentId ? rule : null;
		}

		public async Task<DestinationRuleSaveResult> SaveRuleAsync(int departmentId, DestinationRule rule, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (rule == null)
				return Fail(DestinationRuleSaveErrors.NameRequired);

			var name = rule.Name?.Trim();
			if (string.IsNullOrWhiteSpace(name))
				return Fail(DestinationRuleSaveErrors.NameRequired);

			if (name.Length > DestinationRule.MaxNameLength)
				return Fail(DestinationRuleSaveErrors.NameTooLong);

			DestinationRule target;
			if (string.IsNullOrWhiteSpace(rule.DestinationRuleId))
			{
				target = new DestinationRule { DepartmentId = departmentId, CreatedByUserId = userId, CreatedOn = DateTime.UtcNow };
			}
			else
			{
				target = await GetRuleByIdAsync(departmentId, rule.DestinationRuleId);
				if (target == null)
					return Fail(DestinationRuleSaveErrors.NotFound);

				target.UpdatedByUserId = userId;
				target.UpdatedOn = DateTime.UtcNow;
			}

			if (rule.CallTypeId.HasValue)
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
				if (!callTypes.Any(x => x != null && x.CallTypeId == rule.CallTypeId.Value && x.DepartmentId == departmentId))
					return Fail(DestinationRuleSaveErrors.UnknownCallType);
			}

			if (rule.CallPriority.HasValue)
			{
				var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
				if (!priorities.Any(x => x != null && x.DepartmentCallPriorityId == rule.CallPriority.Value))
					return Fail(DestinationRuleSaveErrors.UnknownPriority);
			}

			if (rule.UnitTypeId.HasValue)
			{
				var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();
				if (!unitTypes.Any(x => x != null && x.UnitTypeId == rule.UnitTypeId.Value && x.DepartmentId == departmentId))
					return Fail(DestinationRuleSaveErrors.UnknownUnitType);
			}

			var poiTypeIds = rule.GetPoiTypeIds();
			if (poiTypeIds.Count > 0)
			{
				var destinationTypes = await DestinationPoiTypeIdsAsync(departmentId);
				if (poiTypeIds.Any(x => !destinationTypes.Contains(x)))
					return Fail(DestinationRuleSaveErrors.UnknownDestinationType);
			}

			var required = rule.GetRequiredCapabilityIds();
			var preferred = rule.GetPreferredCapabilityIds().Where(x => !required.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
			if (required.Count > 0 || preferred.Count > 0)
			{
				var capabilities = new HashSet<string>((await _mappingService.GetPoiCapabilitiesForDepartmentAsync(departmentId) ?? new List<PoiCapability>())
					.Where(x => x != null && x.DepartmentId == departmentId)
					.Select(x => x.PoiCapabilityId), StringComparer.OrdinalIgnoreCase);

				if (required.Concat(preferred).Any(x => !capabilities.Contains(x)))
					return Fail(DestinationRuleSaveErrors.UnknownCapability);
			}

			if (poiTypeIds.Count == 0 && required.Count == 0 && preferred.Count == 0)
				return Fail(DestinationRuleSaveErrors.NothingRequired);

			target.Name = name;
			target.CallTypeId = rule.CallTypeId;
			target.CallPriority = rule.CallPriority;
			target.UnitTypeId = rule.UnitTypeId;
			target.PoiTypeIds = poiTypeIds.Count > 0 ? string.Join(",", poiTypeIds) : null;
			target.RequiredCapabilityIds = required.Count > 0 ? string.Join(",", required) : null;
			target.PreferredCapabilityIds = preferred.Count > 0 ? string.Join(",", preferred) : null;

			var saved = await _destinationRulesRepository.SaveOrUpdateAsync(target, cancellationToken);

			return new DestinationRuleSaveResult { Rule = saved ?? target, Error = DestinationRuleSaveErrors.None };
		}

		public async Task<bool> DeleteRuleAsync(int departmentId, string destinationRuleId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var rule = await GetRuleByIdAsync(departmentId, destinationRuleId);
			if (rule == null)
				return false;

			return await _destinationRulesRepository.DeleteAsync(rule, cancellationToken);
		}

		public async Task<DestinationRequirements> ResolveAsync(int departmentId, DestinationContext context)
		{
			var rules = await GetRulesForDepartmentAsync(departmentId);
			if (rules.Count == 0)
				return DestinationRequirements.None;

			var destinationTypes = await DestinationPoiTypeIdsAsync(departmentId);
			var capabilities = (await _mappingService.GetPoiCapabilitiesForDepartmentAsync(departmentId) ?? new List<PoiCapability>())
				.Where(x => x != null)
				.Select(x => x.PoiCapabilityId)
				.ToList();

			return DestinationRuleResolver.Resolve(rules, context, destinationTypes, capabilities);
		}

		public async Task<DestinationContext> BuildContextAsync(int departmentId, int? callTypeId, string callTypeName, int? callPriority, int? unitId)
		{
			var context = new DestinationContext { CallPriority = callPriority };

			if (callTypeId.HasValue || !string.IsNullOrWhiteSpace(callTypeName))
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
				var callType = (callTypeId.HasValue ? callTypes.FirstOrDefault(x => x != null && x.CallTypeId == callTypeId.Value) : null)
					?? CallTypeLink.FindByName(callTypes, callTypeName);

				context.CallTypeId = callType?.CallTypeId;
			}

			if (unitId.HasValue && unitId.Value > 0)
			{
				var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);
				if (unit != null && unit.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(unit.Type))
				{
					var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();
					context.UnitTypeId = unitTypes
						.Where(x => x != null && string.Equals(x.Type?.Trim(), unit.Type.Trim(), StringComparison.OrdinalIgnoreCase))
						.OrderBy(x => x.UnitTypeId)
						.Select(x => (int?)x.UnitTypeId)
						.FirstOrDefault();
				}
			}

			return context;
		}

		private async Task<HashSet<int>> DestinationPoiTypeIdsAsync(int departmentId)
		{
			var types = await _mappingService.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>();

			return new HashSet<int>(types.Where(x => x != null && x.IsDestination && x.DepartmentId == departmentId).Select(x => x.PoiTypeId));
		}

		private static DestinationRuleSaveResult Fail(DestinationRuleSaveErrors error)
		{
			return new DestinationRuleSaveResult { Error = error };
		}
	}
}
