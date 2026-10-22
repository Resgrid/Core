using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.RunCards;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Applies run card templates (conditional run cards). Unit resources map by suggested name first, then by unit class
	/// (an "any of" class target keeps working when the department adds a type of that class); roles map by name only.
	/// Triggers map by the department's priority and call type meanings. The card is saved disabled for review.
	/// </summary>
	public class RunCardTemplatesService : IRunCardTemplatesService
	{
		private readonly IRunCardsService _runCardsService;
		private readonly IResponsePackagesService _responsePackagesService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public RunCardTemplatesService(IRunCardsService runCardsService, IResponsePackagesService responsePackagesService, IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService, ICallsService callsService, IDepartmentSettingsService departmentSettingsService)
		{
			_runCardsService = runCardsService;
			_responsePackagesService = responsePackagesService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
		}

		private sealed class DepartmentLookups
		{
			public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();
			public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();
			public List<DepartmentCallPriority> Priorities { get; set; } = new List<DepartmentCallPriority>();
			public List<CallType> CallTypes { get; set; } = new List<CallType>();
			public List<ResponsePackage> Packages { get; set; } = new List<ResponsePackage>();
			public DispatchResponseCodesConfig Codes { get; set; } = new DispatchResponseCodesConfig();
		}

		public async Task<RunCardTemplatePreview> PreviewAsync(int departmentId, string templateId)
		{
			var template = RunCardTemplateCatalog.GetById(templateId);

			if (template == null)
				return null;

			var lookups = await LoadLookupsAsync(departmentId);
			return BuildPreview(template, lookups);
		}

		public async Task<RunCardTemplateApplyResult> ApplyAsync(int departmentId, string templateId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var template = RunCardTemplateCatalog.GetById(templateId);

			if (template == null)
				return new RunCardTemplateApplyResult { Success = false, Message = "Unknown template." };

			var lookups = await LoadLookupsAsync(departmentId);
			var preview = BuildPreview(template, lookups);

			if (!preview.CanApply)
				return new RunCardTemplateApplyResult { Success = false, Preview = preview, Message = "Some resources have no match in this department: " + string.Join("; ", preview.Unmatched) };

			// Codes first, so package rows can refer to them.
			var codeIds = await EnsureCodesAsync(departmentId, template, lookups, cancellationToken);

			// Substitute packages before the packages that point at them.
			var packageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var substituteKeys = new HashSet<string>(template.Packages.SelectMany(p => p.Rows).Where(r => r.SubstitutePackageKey != null).Select(r => r.SubstitutePackageKey), StringComparer.OrdinalIgnoreCase);
			var createdPackageIds = new List<int>();

			try
			{
				return await CreatePackagesAndCardAsync(departmentId, template, userId, lookups, preview, codeIds, packageIds, substituteKeys, createdPackageIds, cancellationToken);
			}
			catch (RunCardValidationException ex)
			{
				Logging.LogException(ex, $"Run card template '{template.Id}' produced an invalid card for department {departmentId}.");
				await RemoveCreatedPackagesAsync(departmentId, createdPackageIds);
				return new RunCardTemplateApplyResult { Success = false, Preview = preview, Message = ex.Message };
			}
			catch
			{
				await RemoveCreatedPackagesAsync(departmentId, createdPackageIds);
				throw;
			}
		}

		/// <summary>
		/// The template's packages (reusing ones the department already has by name), then the disabled card. Every package
		/// it creates is added to <paramref name="createdPackageIds"/> as soon as it exists, so a failed apply can remove them.
		/// </summary>
		private async Task<RunCardTemplateApplyResult> CreatePackagesAndCardAsync(int departmentId, RunCardTemplate template, string userId, DepartmentLookups lookups,
			RunCardTemplatePreview preview, Dictionary<string, int> codeIds, Dictionary<string, int> packageIds, HashSet<string> substituteKeys,
			List<int> createdPackageIds, CancellationToken cancellationToken)
		{
			foreach (var package in template.Packages.OrderBy(p => substituteKeys.Contains(p.Key) ? 0 : 1))
			{
				var existing = lookups.Packages.FirstOrDefault(p => string.Equals(p.Name?.Trim(), package.Name, StringComparison.OrdinalIgnoreCase));

				if (existing != null)
				{
					packageIds[package.Key] = existing.ResponsePackageId;
					continue;
				}

				var rows = new List<ResponsePackageRow>();
				var sort = 0;
				foreach (var row in package.Rows)
				{
					rows.Add(new ResponsePackageRow
					{
						Label = row.Label,
						FillMode = (int)row.FillMode,
						RequiredCount = row.FillMode == RequirementFillModes.EnsureFirstArriving ? 1 : row.RequiredCount,
						StatusFilter = (int)row.StatusFilter,
						ResponseCodeId = row.ResponseCode != null && codeIds.TryGetValue(row.ResponseCode, out var codeId) ? codeId : (int?)null,
						SubstitutePackageId = row.SubstitutePackageKey != null && packageIds.TryGetValue(row.SubstitutePackageKey, out var substituteId) ? substituteId : (int?)null,
						KeepOutstanding = row.KeepOutstanding,
						SortOrder = sort++,
						Targets = row.Targets.SelectMany(t => ResolveTarget(t, lookups, null)).ToList()
					});
				}

				var saved = await _responsePackagesService.SavePackageAsync(new ResponsePackage
				{
					DepartmentId = departmentId,
					Name = package.Name,
					Description = $"From the '{template.Name}' run card template.",
					Rows = rows
				}, userId, cancellationToken);

				createdPackageIds.Add(saved.ResponsePackageId);
				packageIds[package.Key] = saved.ResponsePackageId;
				lookups.Packages.Add(saved);
			}

			var cards = await _runCardsService.GetAllRunCardsForDepartmentAsync(departmentId, true) ?? new List<RunCard>();
			var name = template.Name;
			for (var i = 2; cards.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
				name = $"{template.Name} ({i})";

			var card = new RunCard
			{
				DepartmentId = departmentId,
				Name = name,
				Description = template.Description,
				IsDisabled = true,
				DispatchModeOverride = template.DispatchModeOverride,
				RelaxUnfilledRows = template.RelaxUnfilledRows,
				AddedOn = DateTime.UtcNow,
				AddedByUserId = userId,
				Triggers = BuildTriggers(template, lookups, null),
				AlarmLevels = template.Levels.Select(level => new RunCardAlarmLevel
				{
					AlarmLevel = level.AlarmLevel,
					Name = level.Name,
					ResponsePackageId = level.PackageKey != null && packageIds.TryGetValue(level.PackageKey, out var levelPackageId) ? levelPackageId : (int?)null,
					UnitRequirements = new List<RunCardUnitRequirement>(),
					RoleRequirements = new List<RunCardRoleRequirement>(),
					DecisionNodes = level.Nodes.Select((node, index) => new RunCardDecisionNode
					{
						NodeKey = node.Key,
						IsRoot = node.IsRoot,
						NodeKind = (int)node.Kind,
						Label = node.Label,
						ConditionType = node.Kind == RunCardDecisionNodeKinds.Condition ? (int)node.ConditionType : 0,
						SubjectStatusFilter = (int)node.SubjectStatusFilter,
						ReferenceStatusFilter = (int)node.ReferenceStatusFilter,
						ThresholdSeconds = node.Kind == RunCardDecisionNodeKinds.Condition ? node.ThresholdSeconds : null,
						RequiredCount = Math.Max(1, node.RequiredCount),
						YesNodeKey = node.YesKey,
						NoNodeKey = node.NoKey,
						ResponsePackageId = node.PackageKey != null && packageIds.TryGetValue(node.PackageKey, out var nodePackageId) ? nodePackageId : (int?)null,
						AdviceText = node.AdviceText,
						SortOrder = index,
						SubjectTargets = node.SubjectTargets.SelectMany(t => ResolveTarget(t, lookups, null)).ToList(),
						ReferenceTargets = node.ReferenceTargets.SelectMany(t => ResolveTarget(t, lookups, null)).ToList()
					}).ToList()
				}).ToList(),
				AvailabilitySelections = new List<RunCardAvailabilitySelection>()
			};

			var savedCard = await _runCardsService.SaveRunCardAsync(card, cancellationToken);
			return new RunCardTemplateApplyResult { Success = true, RunCardId = savedCard.RunCardId, Preview = preview, Message = "Created disabled: test it on the Test tab, then enable it." };
		}

		/// <summary>
		/// Removes the packages a failed apply created, newest first (a package naming another as its substitute goes before
		/// it), so a retry builds them again instead of reusing leftovers by name. The codes it added stay: they are the
		/// department's catalog, and a retry reuses them.
		/// </summary>
		private async Task RemoveCreatedPackagesAsync(int departmentId, List<int> createdPackageIds)
		{
			foreach (var packageId in Enumerable.Reverse(createdPackageIds))
			{
				try
				{
					await _responsePackagesService.DeletePackageAsync(departmentId, packageId, CancellationToken.None);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Removing response package {packageId} after a failed run card template apply for department {departmentId} failed.");
				}
			}
		}

		public async Task<List<RunCardTemplateSuggestion>> GetSuggestionsAsync(int departmentId, int maximum = 6)
		{
			var lookups = await LoadLookupsAsync(departmentId);
			var cards = (await _runCardsService.GetAllRunCardsForDepartmentAsync(departmentId) ?? new List<RunCard>()).Where(c => !c.IsDisabled).ToList();
			var coveredTypeIds = new HashSet<int>(cards.SelectMany(c => c.Triggers ?? new List<RunCardTrigger>()).Where(t => t.CallTypeId.HasValue).Select(t => t.CallTypeId.Value));
			var suggestions = new List<RunCardTemplateSuggestion>();

			foreach (var callType in lookups.CallTypes.Where(t => t.BaseType > 0 && !coveredTypeIds.Contains(t.CallTypeId)))
			{
				foreach (var template in RunCardTemplateCatalog.All.Where(t => t.CallTypeClass.HasValue && (int)t.CallTypeClass.Value == callType.BaseType))
				{
					if (suggestions.Any(s => s.Template.Id == template.Id))
						continue;

					var targets = template.AllTargets().ToList();
					var resolved = targets.Count(t => ResolveTarget(t, lookups, null).Any());

					suggestions.Add(new RunCardTemplateSuggestion
					{
						Template = template,
						Reason = $"No run card covers '{callType.Type}'.",
						ResolvedTargets = resolved,
						TotalTargets = targets.Count
					});
				}
			}

			return suggestions
				.OrderByDescending(s => s.ResolvedTargets == s.TotalTargets)
				.ThenByDescending(s => s.TotalTargets == 0 ? 1 : (double)s.ResolvedTargets / s.TotalTargets)
				.ThenBy(s => s.Template.Name)
				.Take(Math.Max(1, maximum))
				.ToList();
		}

		#region Mapping

		private async Task<DepartmentLookups> LoadLookupsAsync(int departmentId)
		{
			return new DepartmentLookups
			{
				UnitTypes = (await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>()).Where(t => t != null).ToList(),
				Roles = (await _personnelRolesService.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>()).Where(r => r != null).ToList(),
				Priorities = (await _callsService.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>()).Where(p => p != null).ToList(),
				CallTypes = (await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>()).Where(t => t != null).ToList(),
				Packages = (await _responsePackagesService.GetPackagesForDepartmentAsync(departmentId, true) ?? new List<ResponsePackage>()).ToList(),
				Codes = await _departmentSettingsService.GetDispatchResponseCodesAsync(departmentId, true) ?? new DispatchResponseCodesConfig()
			};
		}

		private RunCardTemplatePreview BuildPreview(RunCardTemplate template, DepartmentLookups lookups)
		{
			var preview = new RunCardTemplatePreview { Template = template };
			var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var target in template.AllTargets())
			{
				var key = $"{target.Kind}|{target.UnitClass}|{string.Join(",", target.SuggestedNames)}";
				if (!described.Add(key))
					continue;

				var resolved = ResolveTarget(target, lookups, preview);

				if (!resolved.Any())
					preview.Unmatched.Add(DescribeTemplateTarget(target));
			}

			foreach (var unmatched in DescribeIndistinctComparisons(template, lookups))
			{
				if (!preview.Unmatched.Contains(unmatched))
					preview.Unmatched.Add(unmatched);
			}

			foreach (var trigger in BuildTriggers(template, lookups, preview))
			{
				var priority = lookups.Priorities.FirstOrDefault(p => p.DepartmentCallPriorityId == trigger.Priority)?.Name;
				var type = lookups.CallTypes.FirstOrDefault(t => t.CallTypeId == trigger.CallTypeId)?.Type;
				preview.Triggers.Add(string.Join(" + ", new[] { priority, type }.Where(x => !string.IsNullOrWhiteSpace(x))));
			}

			foreach (var package in template.Packages)
			{
				if (lookups.Packages.Any(p => string.Equals(p.Name?.Trim(), package.Name, StringComparison.OrdinalIgnoreCase)))
				{
					preview.PackagesReused.Add(package.Name);
					preview.Warnings.Add($"The existing package '{package.Name}' will be used as it is; compare its rows with the template.");
				}
				else
				{
					preview.PackagesCreated.Add(package.Name);
				}
			}

			var set = ResponseCodeTemplateCatalog.GetById(template.ResponseCodeSetId);
			if (set != null)
			{
				var used = new HashSet<string>(template.Packages.SelectMany(p => p.Rows).Where(r => r.ResponseCode != null).Select(r => r.ResponseCode), StringComparer.OrdinalIgnoreCase);
				foreach (var code in set.Codes.Where(c => used.Contains(c.Code) && lookups.Codes.Codes.All(x => !string.Equals(x.Code, c.Code, StringComparison.OrdinalIgnoreCase))))
					preview.CodesAdded.Add($"{code.Code} ({code.Name})");
			}

			return preview;
		}

		/// <summary>
		/// "Arrives before" conditions whose two sides map to the same units here. A named unit type with no match falls back
		/// to its whole class ("Quint" to Aerial), so a department with the class but not the name gets a condition comparing
		/// the class with itself: meaningless, and refused by the card's validation after the packages were written. Each side
		/// that fell back is named as the missing resource.
		/// </summary>
		private static IEnumerable<string> DescribeIndistinctComparisons(RunCardTemplate template, DepartmentLookups lookups)
		{
			var conditions = template.Levels.SelectMany(l => l.Nodes)
				.Where(n => n.Kind == RunCardDecisionNodeKinds.Condition && n.ConditionType == RunCardConditionTypes.ArrivesBefore);

			foreach (var node in conditions)
			{
				var subject = node.SubjectTargets.SelectMany(t => ResolveTarget(t, lookups, null)).ToList();
				var reference = node.ReferenceTargets.SelectMany(t => ResolveTarget(t, lookups, null)).ToList();

				// The same rule as RunCardDecisionGraph.Validate (templates set no status meanings).
				if (!subject.Any() || !reference.Any() || node.SubjectStatusFilter != node.ReferenceStatusFilter || !RunCardDecisionGraph.SameTargets(subject, reference))
					continue;

				var fellBack = node.SubjectTargets.Where(t => ResolvesByClassOnly(t, lookups)).Select(t => Tuple.Create(t, node.ReferenceTargets))
					.Concat(node.ReferenceTargets.Where(t => ResolvesByClassOnly(t, lookups)).Select(t => Tuple.Create(t, node.SubjectTargets)))
					.ToList();

				if (!fellBack.Any())
				{
					yield return $"'{node.Label}': both sides map to the same units";
					continue;
				}

				foreach (var side in fellBack)
					yield return $"{DescribeTemplateTarget(side.Item1)} (needed to tell it apart from {string.Join(", ", side.Item2.Select(DescribeTemplateTarget))})";
			}
		}

		/// <summary>A unit type target that matched no type by name, so it maps to its whole class.</summary>
		private static bool ResolvesByClassOnly(RunCardTemplateTarget target, DepartmentLookups lookups)
		{
			return target.Kind == RunCardTargetKinds.UnitType
				&& !lookups.UnitTypes.Any(t => target.SuggestedNames.Any(n => string.Equals(t.Type?.Trim(), n, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary>The department targets a template target maps to; empty when nothing matches.</summary>
		private static List<RunCardTarget> ResolveTarget(RunCardTemplateTarget target, DepartmentLookups lookups, RunCardTemplatePreview preview)
		{
			switch (target.Kind)
			{
				case RunCardTargetKinds.AnyUnit:
					return new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.AnyUnit } };
				case RunCardTargetKinds.AnyPerson:
					return new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.AnyPerson } };
				case RunCardTargetKinds.PersonnelRole:
				{
					var roles = lookups.Roles.Where(r => target.SuggestedNames.Any(n => string.Equals(r.Name?.Trim(), n, StringComparison.OrdinalIgnoreCase))).ToList();
					if (roles.Any())
						preview?.Mappings.Add($"{DescribeTemplateTarget(target)} → {string.Join(", ", roles.Select(r => r.Name))}");
					return roles.Select(r => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.PersonnelRole, TargetRefId = r.PersonnelRoleId }).ToList();
				}
				default:
				{
					var byName = lookups.UnitTypes.Where(t => target.SuggestedNames.Any(n => string.Equals(t.Type?.Trim(), n, StringComparison.OrdinalIgnoreCase))).ToList();

					// A class target accepts every type of the class, so the named types only matter when they have no class.
					if (target.Kind == RunCardTargetKinds.UnitClass && target.UnitClass.HasValue && lookups.UnitTypes.Any(t => t.BaseType == (int)target.UnitClass.Value))
					{
						var classTypes = lookups.UnitTypes.Where(t => t.BaseType == (int)target.UnitClass.Value).ToList();
						preview?.Mappings.Add($"{DescribeTemplateTarget(target)} → {string.Join(", ", classTypes.Select(t => t.Type))}");
						var targets = new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitClass, TargetRefId = (int)target.UnitClass.Value } };
						targets.AddRange(byName.Where(t => t.BaseType != (int)target.UnitClass.Value).Select(t => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = t.UnitTypeId }));
						return targets;
					}

					if (byName.Any())
					{
						preview?.Mappings.Add($"{DescribeTemplateTarget(target)} → {string.Join(", ", byName.Select(t => t.Type))}");
						return byName.Select(t => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = t.UnitTypeId }).ToList();
					}

					if (target.UnitClass.HasValue && lookups.UnitTypes.Any(t => t.BaseType == (int)target.UnitClass.Value))
					{
						var classTypes = lookups.UnitTypes.Where(t => t.BaseType == (int)target.UnitClass.Value).ToList();
						preview?.Mappings.Add($"{DescribeTemplateTarget(target)} → {string.Join(", ", classTypes.Select(t => t.Type))}");
						return new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitClass, TargetRefId = (int)target.UnitClass.Value } };
					}

					return new List<RunCardTarget>();
				}
			}
		}

		private static string DescribeTemplateTarget(RunCardTemplateTarget target)
		{
			switch (target.Kind)
			{
				case RunCardTargetKinds.PersonnelRole:
					return $"Role '{string.Join("' or '", target.SuggestedNames)}'";
				case RunCardTargetKinds.UnitClass:
					return $"{target.UnitClass?.GetDisplayString()} (unit class)";
				case RunCardTargetKinds.UnitType:
					return $"Unit type '{string.Join("' or '", target.SuggestedNames)}'" + (target.UnitClass.HasValue ? $" or class {target.UnitClass.Value.GetDisplayString()}" : "");
				default:
					return target.Describe();
			}
		}

		private static List<RunCardTrigger> BuildTriggers(RunCardTemplate template, DepartmentLookups lookups, RunCardTemplatePreview preview)
		{
			var priorities = template.Priority.HasValue
				? lookups.Priorities.Where(p => p.BaseType == (int)template.Priority.Value || (p.IsSystemPriority && p.DepartmentCallPriorityId == (int)template.Priority.Value)).ToList()
				: lookups.Priorities.ToList();
			var callTypes = template.CallTypeClass.HasValue
				? lookups.CallTypes.Where(t => t.BaseType == (int)template.CallTypeClass.Value).ToList()
				: new List<CallType>();

			var triggers = new List<RunCardTrigger>();

			if (!priorities.Any())
			{
				preview?.Warnings.Add(template.Priority.HasValue
					? $"No call priority means '{template.Priority.Value}'; set your priorities' meanings or add a trigger by hand."
					: "The department has no call priorities; add a trigger by hand.");
				return triggers;
			}

			if (template.CallTypeClass.HasValue && !callTypes.Any())
				preview?.Warnings.Add($"No call type means '{template.CallTypeClass.Value.GetDisplayString()}'; the card triggers on priority alone until you add call types.");

			foreach (var priority in priorities)
			{
				if (callTypes.Any())
				{
					foreach (var callType in callTypes)
						triggers.Add(new RunCardTrigger { TriggerType = 2, Priority = priority.DepartmentCallPriorityId, CallTypeId = callType.CallTypeId });
				}
				else
				{
					triggers.Add(new RunCardTrigger { TriggerType = 0, Priority = priority.DepartmentCallPriorityId });
				}
			}

			return triggers;
		}

		/// <summary>Adds the template's codes the department does not have yet; returns code text → catalog id.</summary>
		private async Task<Dictionary<string, int>> EnsureCodesAsync(int departmentId, RunCardTemplate template, DepartmentLookups lookups, CancellationToken cancellationToken)
		{
			var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var set = ResponseCodeTemplateCatalog.GetById(template.ResponseCodeSetId);

			if (set == null)
				return ids;

			var used = new HashSet<string>(template.Packages.SelectMany(p => p.Rows).Where(r => r.ResponseCode != null).Select(r => r.ResponseCode), StringComparer.OrdinalIgnoreCase);
			var catalog = lookups.Codes;
			var changed = false;
			var nextId = catalog.Codes.Any() ? catalog.Codes.Max(c => c.Id) + 1 : 1;

			foreach (var code in set.Codes.Where(c => used.Contains(c.Code)))
			{
				var existing = catalog.Codes.FirstOrDefault(c => string.Equals(c.Code, code.Code, StringComparison.OrdinalIgnoreCase));

				if (existing == null)
				{
					if (catalog.Codes.Count >= DispatchResponseCodesConfig.MaximumCodes)
						continue;

					existing = new DispatchResponseCode { Id = nextId++, Code = code.Code, Name = code.Name, Color = code.Color, SortOrder = catalog.Codes.Count };
					catalog.Codes.Add(existing);
					changed = true;
				}

				ids[code.Code] = existing.Id;
			}

			if (changed)
				await _departmentSettingsService.SetDispatchResponseCodesAsync(departmentId, catalog, cancellationToken);

			return ids;
		}

		#endregion
	}
}
