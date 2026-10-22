using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class RunCardsService : IRunCardsService
	{
		private const string RunCardsCacheKey = "RunCardsForDep_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(7);

		private readonly IRunCardsRepository _runCardsRepository;
		private readonly IRunCardTriggersRepository _runCardTriggersRepository;
		private readonly IRunCardAlarmLevelsRepository _runCardAlarmLevelsRepository;
		private readonly IRunCardUnitRequirementsRepository _runCardUnitRequirementsRepository;
		private readonly IRunCardRoleRequirementsRepository _runCardRoleRequirementsRepository;
		private readonly IRunCardAvailabilitySelectionsRepository _runCardAvailabilitySelectionsRepository;
		private readonly IStationCoverageRequirementsRepository _stationCoverageRequirementsRepository;
		private readonly ICallTypesRepository _callTypesRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICustomStateService _customStateService;
		private readonly IRunCardDecisionNodesRepository _runCardDecisionNodesRepository;
		private readonly IRunCardTargetsRepository _runCardTargetsRepository;
		private readonly IResponsePackagesRepository _responsePackagesRepository;

		public RunCardsService(IRunCardsRepository runCardsRepository, IRunCardTriggersRepository runCardTriggersRepository,
			IRunCardAlarmLevelsRepository runCardAlarmLevelsRepository, IRunCardUnitRequirementsRepository runCardUnitRequirementsRepository,
			IRunCardRoleRequirementsRepository runCardRoleRequirementsRepository, IRunCardAvailabilitySelectionsRepository runCardAvailabilitySelectionsRepository,
			IStationCoverageRequirementsRepository stationCoverageRequirementsRepository, ICallTypesRepository callTypesRepository,
			ICacheProvider cacheProvider, IUnitOfWork unitOfWork, IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService, IDepartmentGroupsService departmentGroupsService,
			ICustomStateService customStateService, IRunCardDecisionNodesRepository runCardDecisionNodesRepository,
			IRunCardTargetsRepository runCardTargetsRepository, IResponsePackagesRepository responsePackagesRepository)
		{
			_runCardDecisionNodesRepository = runCardDecisionNodesRepository;
			_runCardTargetsRepository = runCardTargetsRepository;
			_responsePackagesRepository = responsePackagesRepository;
			_runCardsRepository = runCardsRepository;
			_runCardTriggersRepository = runCardTriggersRepository;
			_runCardAlarmLevelsRepository = runCardAlarmLevelsRepository;
			_runCardUnitRequirementsRepository = runCardUnitRequirementsRepository;
			_runCardRoleRequirementsRepository = runCardRoleRequirementsRepository;
			_runCardAvailabilitySelectionsRepository = runCardAvailabilitySelectionsRepository;
			_stationCoverageRequirementsRepository = stationCoverageRequirementsRepository;
			_callTypesRepository = callTypesRepository;
			_cacheProvider = cacheProvider;
			_unitOfWork = unitOfWork;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_customStateService = customStateService;
		}

		public async Task<List<RunCard>> GetAllRunCardsForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<RunCard>> getRunCards()
			{
				var cards = await _runCardsRepository.GetAllByDepartmentIdAsync(departmentId);

				if (cards == null)
					return new List<RunCard>();

				var list = cards.ToList();
				var nodeTargets = list.Any() ? await GetDepartmentNodeTargetsAsync(departmentId) : new List<RunCardTarget>();

				foreach (var card in list)
					await HydrateRunCardAsync(card, nodeTargets);

				return list;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(string.Format(RunCardsCacheKey, departmentId), getRunCards, CacheLength);

			return await getRunCards();
		}

		public async Task<RunCard> GetRunCardByIdAsync(int runCardId)
		{
			var card = await _runCardsRepository.GetByIdAsync(runCardId);

			if (card == null)
				return null;

			await HydrateRunCardAsync(card, await GetDepartmentNodeTargetsAsync(card.DepartmentId));

			return card;
		}

		private async Task<List<RunCardTarget>> GetDepartmentNodeTargetsAsync(int departmentId)
		{
			return (await _runCardTargetsRepository.GetAllByDepartmentIdAsync(departmentId))?
				.Where(t => t.OwnerKind == (int)RunCardTargetOwners.NodeSubject || t.OwnerKind == (int)RunCardTargetOwners.NodeReference)
				.ToList() ?? new List<RunCardTarget>();
		}

		public async Task<RunCard> SaveRunCardAsync(RunCard runCard, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (runCard == null)
				throw new ArgumentNullException(nameof(runCard));

			// Alarm levels are 1-based and one row per level per card (enforced by
			// UX_RunCardAlarmLevels_Card_Level). Catch both here so an API client gets a
			// stated reason instead of a unique-index violation, and so a level below 1 —
			// which the engine can never match, since escalation starts at 1 — cannot be
			// stored as silently dead configuration.
			if (runCard.AlarmLevels != null)
			{
				if (runCard.AlarmLevels.Any(l => l.AlarmLevel < 1))
					throw new ArgumentException("Run card alarm levels start at 1.", nameof(runCard));

				if (runCard.AlarmLevels.GroupBy(l => l.AlarmLevel).Any(g => g.Count() > 1))
					throw new ArgumentException("A run card cannot define the same alarm level twice.", nameof(runCard));
			}

			await ValidateRunCardReferencesAsync(runCard);
			await ValidateRunCardChildOwnershipAsync(runCard);
			await ValidateConditionalLevelsAsync(runCard);

			var isNew = runCard.RunCardId == 0;

			// Snapshot the incoming graph, then persist the header without letting the
			// repository cascade (child sync is managed explicitly below so nested
			// requirement rows and level renumbering behave deterministically).
			var triggers = runCard.Triggers?.ToList() ?? new List<RunCardTrigger>();
			var alarmLevels = runCard.AlarmLevels?.ToList() ?? new List<RunCardAlarmLevel>();
			var selections = runCard.AvailabilitySelections?.ToList() ?? new List<RunCardAvailabilitySelection>();

			// The header and five child tables are rewritten as one graph; a failure part way
			// through would otherwise leave, say, the old triggers deleted and the new ones
			// unwritten — a card that matches nothing — or alarm levels gone with their
			// requirements orphaned.
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _runCardsRepository.SaveOrUpdateAsync(runCard, cancellationToken, true);

				// Triggers
				var existingTriggers = isNew
					? new List<RunCardTrigger>()
					: (await _runCardTriggersRepository.GetTriggersByRunCardIdAsync(runCard.RunCardId)).ToList();

				foreach (var removed in existingTriggers.Where(e => triggers.All(t => t.RunCardTriggerId != e.RunCardTriggerId)))
					await _runCardTriggersRepository.DeleteAsync(removed, cancellationToken);

				foreach (var trigger in triggers)
				{
					trigger.RunCardId = runCard.RunCardId;
					await _runCardTriggersRepository.SaveOrUpdateAsync(trigger, cancellationToken, true);
				}

				// Alarm levels + their requirements
				var existingLevels = isNew
					? new List<RunCardAlarmLevel>()
					: (await _runCardAlarmLevelsRepository.GetAlarmLevelsByRunCardIdAsync(runCard.RunCardId)).ToList();
				var existingUnitReqs = isNew
					? new List<RunCardUnitRequirement>()
					: (await _runCardUnitRequirementsRepository.GetUnitRequirementsByRunCardIdAsync(runCard.RunCardId)).ToList();
				var existingRoleReqs = isNew
					? new List<RunCardRoleRequirement>()
					: (await _runCardRoleRequirementsRepository.GetRoleRequirementsByRunCardIdAsync(runCard.RunCardId)).ToList();
				var existingNodes = isNew
					? new List<RunCardDecisionNode>()
					: ((await _runCardDecisionNodesRepository.GetNodesByRunCardIdAsync(runCard.RunCardId))?.ToList() ?? new List<RunCardDecisionNode>());
				var existingNodeTargets = isNew || !existingNodes.Any()
					? new List<RunCardTarget>()
					: await GetDepartmentNodeTargetsAsync(runCard.DepartmentId);

				foreach (var removedLevel in existingLevels.Where(e => alarmLevels.All(l => l.RunCardAlarmLevelId != e.RunCardAlarmLevelId)))
				{
					foreach (var req in existingUnitReqs.Where(r => r.RunCardAlarmLevelId == removedLevel.RunCardAlarmLevelId))
						await _runCardUnitRequirementsRepository.DeleteAsync(req, cancellationToken);

					foreach (var req in existingRoleReqs.Where(r => r.RunCardAlarmLevelId == removedLevel.RunCardAlarmLevelId))
						await _runCardRoleRequirementsRepository.DeleteAsync(req, cancellationToken);

					foreach (var node in existingNodes.Where(n => n.RunCardAlarmLevelId == removedLevel.RunCardAlarmLevelId))
						await DeleteNodeAsync(node, existingNodeTargets, cancellationToken);

					await _runCardAlarmLevelsRepository.DeleteAsync(removedLevel, cancellationToken);
				}

				foreach (var level in alarmLevels)
				{
					var unitReqs = level.UnitRequirements?.ToList() ?? new List<RunCardUnitRequirement>();
					var roleReqs = level.RoleRequirements?.ToList() ?? new List<RunCardRoleRequirement>();

					level.RunCardId = runCard.RunCardId;
					await _runCardAlarmLevelsRepository.SaveOrUpdateAsync(level, cancellationToken, true);

					foreach (var removed in existingUnitReqs.Where(e => e.RunCardAlarmLevelId == level.RunCardAlarmLevelId
							&& unitReqs.All(r => r.RunCardUnitRequirementId != e.RunCardUnitRequirementId)))
						await _runCardUnitRequirementsRepository.DeleteAsync(removed, cancellationToken);

					foreach (var req in unitReqs)
					{
						req.RunCardAlarmLevelId = level.RunCardAlarmLevelId;
						await _runCardUnitRequirementsRepository.SaveOrUpdateAsync(req, cancellationToken, true);
					}

					foreach (var removed in existingRoleReqs.Where(e => e.RunCardAlarmLevelId == level.RunCardAlarmLevelId
							&& roleReqs.All(r => r.RunCardRoleRequirementId != e.RunCardRoleRequirementId)))
						await _runCardRoleRequirementsRepository.DeleteAsync(removed, cancellationToken);

					foreach (var req in roleReqs)
					{
						req.RunCardAlarmLevelId = level.RunCardAlarmLevelId;
						await _runCardRoleRequirementsRepository.SaveOrUpdateAsync(req, cancellationToken, true);
					}

					await SyncDecisionNodesAsync(runCard.DepartmentId, level, existingNodes, existingNodeTargets, cancellationToken);
				}

				// Availability selections
				var existingSelections = isNew
					? new List<RunCardAvailabilitySelection>()
					: (await _runCardAvailabilitySelectionsRepository.GetSelectionsByRunCardIdAsync(runCard.RunCardId)).ToList();

				foreach (var removed in existingSelections.Where(e => selections.All(s => s.RunCardAvailabilitySelectionId != e.RunCardAvailabilitySelectionId)))
					await _runCardAvailabilitySelectionsRepository.DeleteAsync(removed, cancellationToken);

				foreach (var selection in selections)
				{
					selection.RunCardId = runCard.RunCardId;
					await _runCardAvailabilitySelectionsRepository.SaveOrUpdateAsync(selection, cancellationToken, true);
				}

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			// Invalidate only once the graph is committed, so a reader cannot repopulate the
			// cache from a transaction that later rolls back.
			await InvalidateRunCardsInCacheAsync(runCard.DepartmentId);

			return runCard;
		}

		/// <summary>
		/// The child rows are written with SaveOrUpdateAsync, which treats a non-zero primary
		/// key as an update keyed on that id alone — nothing scopes the statement to this card.
		/// A submitted id belonging to another card would therefore be rewritten and reparented
		/// here, across departments, so every non-zero child id is required to already be part
		/// of this card's stored graph. On a new card the ids are cleared instead, since there
		/// is no graph to belong to and every child must insert.
		/// </summary>
		private async Task ValidateRunCardChildOwnershipAsync(RunCard runCard)
		{
			var triggers = runCard.Triggers ?? new List<RunCardTrigger>();
			var levels = runCard.AlarmLevels ?? new List<RunCardAlarmLevel>();
			var selections = runCard.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>();
			var unitRequirements = levels.SelectMany(l => l.UnitRequirements ?? new List<RunCardUnitRequirement>()).ToList();
			var roleRequirements = levels.SelectMany(l => l.RoleRequirements ?? new List<RunCardRoleRequirement>()).ToList();

			if (runCard.RunCardId == 0)
			{
				foreach (var trigger in triggers)
					trigger.RunCardTriggerId = 0;

				foreach (var level in levels)
					level.RunCardAlarmLevelId = 0;

				foreach (var requirement in unitRequirements)
					requirement.RunCardUnitRequirementId = 0;

				foreach (var requirement in roleRequirements)
					requirement.RunCardRoleRequirementId = 0;

				foreach (var selection in selections)
					selection.RunCardAvailabilitySelectionId = 0;

				return;
			}

			var stored = await GetRunCardByIdAsync(runCard.RunCardId);

			if (stored == null || stored.DepartmentId != runCard.DepartmentId)
				throw new ArgumentException("The run card does not belong to this department.", nameof(runCard));

			var storedLevels = stored.AlarmLevels ?? new List<RunCardAlarmLevel>();

			var storedTriggerIds = new HashSet<int>((stored.Triggers ?? new List<RunCardTrigger>()).Select(t => t.RunCardTriggerId));
			var storedLevelIds = new HashSet<int>(storedLevels.Select(l => l.RunCardAlarmLevelId));
			var storedUnitRequirementIds = new HashSet<int>(storedLevels.SelectMany(l => l.UnitRequirements ?? new List<RunCardUnitRequirement>()).Select(r => r.RunCardUnitRequirementId));
			var storedRoleRequirementIds = new HashSet<int>(storedLevels.SelectMany(l => l.RoleRequirements ?? new List<RunCardRoleRequirement>()).Select(r => r.RunCardRoleRequirementId));
			var storedSelectionIds = new HashSet<int>((stored.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>()).Select(s => s.RunCardAvailabilitySelectionId));

			if (triggers.Any(t => t.RunCardTriggerId != 0 && !storedTriggerIds.Contains(t.RunCardTriggerId)))
				throw new ArgumentException("A trigger does not belong to this run card.", nameof(runCard));

			if (levels.Any(l => l.RunCardAlarmLevelId != 0 && !storedLevelIds.Contains(l.RunCardAlarmLevelId)))
				throw new ArgumentException("An alarm level does not belong to this run card.", nameof(runCard));

			if (unitRequirements.Any(r => r.RunCardUnitRequirementId != 0 && !storedUnitRequirementIds.Contains(r.RunCardUnitRequirementId)))
				throw new ArgumentException("A unit type requirement does not belong to this run card.", nameof(runCard));

			if (roleRequirements.Any(r => r.RunCardRoleRequirementId != 0 && !storedRoleRequirementIds.Contains(r.RunCardRoleRequirementId)))
				throw new ArgumentException("A personnel role requirement does not belong to this run card.", nameof(runCard));

			if (selections.Any(s => s.RunCardAvailabilitySelectionId != 0 && !storedSelectionIds.Contains(s.RunCardAvailabilitySelectionId)))
				throw new ArgumentException("A status selection does not belong to this run card.", nameof(runCard));
		}

		/// <summary>
		/// Conditional run cards: a level's decision graph must be a valid chart (one start, every node reachable, no loops,
		/// both branches on every condition), its targets must name the department's own unit types and roles, and the
		/// packages its outcomes and the level itself reference must be the department's. Rejected with the reasons, so the
		/// editor can show them.
		/// </summary>
		private async Task ValidateConditionalLevelsAsync(RunCard runCard)
		{
			var levels = runCard.AlarmLevels ?? new List<RunCardAlarmLevel>();
			var anyGraph = levels.Any(RunCardDecisionGraph.HasGraph);
			var anyPackage = levels.Any(l => l.ResponsePackageId.HasValue);

			if (!anyGraph && !anyPackage)
				return;

			var errors = new List<string>();
			var packageIds = new HashSet<int>(((await _responsePackagesRepository.GetAllByDepartmentIdAsync(runCard.DepartmentId)) ?? Enumerable.Empty<ResponsePackage>())
				.Select(p => p.ResponsePackageId));

			HashSet<int> unitTypeIds = null;
			HashSet<int> roleIds = null;

			if (anyGraph)
			{
				unitTypeIds = await GetDepartmentUnitTypeIdsAsync(runCard.DepartmentId);
				roleIds = await GetDepartmentRoleIdsAsync(runCard.DepartmentId);
			}

			foreach (var level in levels)
			{
				if (level.ResponsePackageId.HasValue && !packageIds.Contains(level.ResponsePackageId.Value))
					errors.Add($"Alarm level {level.AlarmLevel}: the response package does not belong to this department.");

				if (!RunCardDecisionGraph.HasGraph(level))
					continue;

				var nodes = level.DecisionNodes.Where(n => n != null).ToList();

				foreach (var error in RunCardDecisionGraph.Validate(nodes))
					errors.Add($"Alarm level {level.AlarmLevel}: {error}");

				foreach (var node in nodes)
				{
					var nodeName = string.IsNullOrWhiteSpace(node.Label) ? (node.IsOutcome ? "An outcome" : "A condition") : $"'{node.Label.Trim()}'";

					if (node.IsCondition)
					{
						foreach (var error in RunCardTargetRules.Validate(node.SubjectTargets, unitTypeIds, roleIds, nodeName))
							errors.Add($"Alarm level {level.AlarmLevel}: {error}");

						if (node.ConditionType == (int)RunCardConditionTypes.ArrivesBefore)
						{
							foreach (var error in RunCardTargetRules.Validate(node.ReferenceTargets, unitTypeIds, roleIds, nodeName))
								errors.Add($"Alarm level {level.AlarmLevel}: {error}");
						}
					}

					if (node.IsOutcome && node.ResponsePackageId.HasValue && !packageIds.Contains(node.ResponsePackageId.Value))
						errors.Add($"Alarm level {level.AlarmLevel}: {nodeName} uses a response package that does not belong to this department.");

					if (!string.IsNullOrEmpty(node.Label) && node.Label.Length > 200)
						errors.Add($"Alarm level {level.AlarmLevel}: {nodeName} has a label longer than 200 characters.");

					if (!string.IsNullOrEmpty(node.AdviceText) && node.AdviceText.Length > 1000)
						errors.Add($"Alarm level {level.AlarmLevel}: {nodeName} has advice longer than 1000 characters.");
				}
			}

			if (errors.Any())
				throw new RunCardValidationException(errors);
		}

		/// <summary>
		/// Writes one level's decision nodes. Nodes are matched to the stored ones by key within the level (the key is what
		/// the Yes/No pointers use), so a node edited in place keeps its row; nodes no longer posted are deleted with their
		/// targets. Targets are rewritten whole per node. Child ids posted by the client are never trusted: only the stored
		/// row matched by key supplies an id.
		/// </summary>
		private async Task SyncDecisionNodesAsync(int departmentId, RunCardAlarmLevel level, List<RunCardDecisionNode> existingNodes,
			List<RunCardTarget> existingNodeTargets, CancellationToken cancellationToken)
		{
			var posted = (level.DecisionNodes ?? new List<RunCardDecisionNode>()).Where(n => n != null).ToList();
			var stored = existingNodes.Where(n => n.RunCardAlarmLevelId == level.RunCardAlarmLevelId).ToList();

			foreach (var removed in stored.Where(s => posted.All(p => !string.Equals(p.NodeKey, s.NodeKey, StringComparison.OrdinalIgnoreCase))))
				await DeleteNodeAsync(removed, existingNodeTargets, cancellationToken);

			var sort = 0;
			foreach (var node in posted)
			{
				var match = stored.FirstOrDefault(s => string.Equals(s.NodeKey, node.NodeKey, StringComparison.OrdinalIgnoreCase));
				var subjectTargets = (node.SubjectTargets ?? new List<RunCardTarget>()).Where(t => t != null).ToList();
				var referenceTargets = (node.ReferenceTargets ?? new List<RunCardTarget>()).Where(t => t != null).ToList();

				node.RunCardDecisionNodeId = match?.RunCardDecisionNodeId ?? 0;
				node.RunCardAlarmLevelId = level.RunCardAlarmLevelId;
				node.SortOrder = sort++;

				if (node.NodeKind == (int)RunCardDecisionNodeKinds.Outcome)
				{
					node.ConditionType = (int)RunCardConditionTypes.None;
					node.YesNodeKey = null;
					node.NoNodeKey = null;
					subjectTargets.Clear();
					referenceTargets.Clear();
				}
				else
				{
					node.ResponsePackageId = null;
					node.AdviceText = null;

					if (node.ConditionType != (int)RunCardConditionTypes.ArrivesBefore)
						referenceTargets.Clear();
				}

				await _runCardDecisionNodesRepository.SaveOrUpdateAsync(node, cancellationToken, true);

				foreach (var old in existingNodeTargets.Where(t => t.OwnerId == node.RunCardDecisionNodeId
					&& (t.OwnerKind == (int)RunCardTargetOwners.NodeSubject || t.OwnerKind == (int)RunCardTargetOwners.NodeReference)))
					await _runCardTargetsRepository.DeleteAsync(old, cancellationToken);

				node.SubjectTargets = await WriteTargetsAsync(departmentId, RunCardTargetOwners.NodeSubject, node.RunCardDecisionNodeId, subjectTargets, cancellationToken);
				node.ReferenceTargets = await WriteTargetsAsync(departmentId, RunCardTargetOwners.NodeReference, node.RunCardDecisionNodeId, referenceTargets, cancellationToken);
			}
		}

		private async Task<List<RunCardTarget>> WriteTargetsAsync(int departmentId, RunCardTargetOwners owner, int ownerId, List<RunCardTarget> targets,
			CancellationToken cancellationToken)
		{
			var written = new List<RunCardTarget>();

			foreach (var target in targets)
			{
				var copy = new RunCardTarget
				{
					DepartmentId = departmentId,
					OwnerKind = (int)owner,
					OwnerId = ownerId,
					TargetKind = target.TargetKind,
					TargetRefId = target.TargetKind == (int)RunCardTargetKinds.AnyUnit || target.TargetKind == (int)RunCardTargetKinds.AnyPerson ? null : target.TargetRefId
				};

				written.Add(await _runCardTargetsRepository.SaveOrUpdateAsync(copy, cancellationToken, true));
			}

			return written;
		}

		private async Task DeleteNodeAsync(RunCardDecisionNode node, List<RunCardTarget> existingNodeTargets, CancellationToken cancellationToken)
		{
			foreach (var target in existingNodeTargets.Where(t => t.OwnerId == node.RunCardDecisionNodeId
				&& (t.OwnerKind == (int)RunCardTargetOwners.NodeSubject || t.OwnerKind == (int)RunCardTargetOwners.NodeReference)))
				await _runCardTargetsRepository.DeleteAsync(target, cancellationToken);

			await _runCardDecisionNodesRepository.DeleteAsync(node, cancellationToken);
		}

		/// <summary>
		/// Every id on a run card comes from the client, so each one is checked against the
		/// card's own department before it is stored. A foreign HomeStationGroupId is the
		/// sharpest case — the engine dereferences it to anchor the station cascade without
		/// a department check of its own, which would let another department's station
		/// coordinates steer selection. The rest would store as configuration that silently
		/// matches nothing, since the engine only ever scores against its own department's
		/// units, roles and statuses.
		/// </summary>
		private async Task ValidateRunCardReferencesAsync(RunCard runCard)
		{
			if (runCard.HomeStationGroupId.HasValue
				&& !(await GetDepartmentStationIdsAsync(runCard.DepartmentId)).Contains(runCard.HomeStationGroupId.Value))
				throw new ArgumentException("The home station does not belong to this department.", nameof(runCard));

			var triggerCallTypeIds = (runCard.Triggers ?? new List<RunCardTrigger>())
				.Where(t => t.CallTypeId.HasValue)
				.Select(t => t.CallTypeId.Value)
				.Distinct()
				.ToList();

			if (triggerCallTypeIds.Any())
			{
				var callTypeIds = await GetDepartmentCallTypeIdsAsync(runCard.DepartmentId);

				if (triggerCallTypeIds.Any(id => !callTypeIds.Contains(id)))
					throw new ArgumentException("A trigger references a call type from another department.", nameof(runCard));
			}

			var levels = runCard.AlarmLevels ?? new List<RunCardAlarmLevel>();
			var selections = runCard.AvailabilitySelections ?? new List<RunCardAvailabilitySelection>();

			var referencedUnitTypeIds = levels
				.SelectMany(l => l.UnitRequirements ?? new List<RunCardUnitRequirement>())
				.Select(r => r.UnitTypeId)
				.Concat(selections.Where(s => s.UnitTypeId.HasValue).Select(s => s.UnitTypeId.Value))
				.Distinct()
				.ToList();

			if (referencedUnitTypeIds.Any())
			{
				var unitTypeIds = await GetDepartmentUnitTypeIdsAsync(runCard.DepartmentId);

				if (referencedUnitTypeIds.Any(id => !unitTypeIds.Contains(id)))
					throw new ArgumentException("A unit type does not belong to this department.", nameof(runCard));
			}

			var referencedRoleIds = levels
				.SelectMany(l => l.RoleRequirements ?? new List<RunCardRoleRequirement>())
				.Select(r => r.PersonnelRoleId)
				.Distinct()
				.ToList();

			if (referencedRoleIds.Any())
			{
				var roleIds = await GetDepartmentRoleIdsAsync(runCard.DepartmentId);

				if (referencedRoleIds.Any(id => !roleIds.Contains(id)))
					throw new ArgumentException("A personnel role does not belong to this department.", nameof(runCard));
			}

			// Only custom selections carry a department-scoped id; built-in ones are enum
			// values shared by every department and have no ownership to check.
			var customStateIds = selections.Where(s => s.IsCustomState).Select(s => s.StateId).Distinct().ToList();

			if (customStateIds.Any())
			{
				var owned = await GetDepartmentActiveCustomStateDetailIdsAsync(runCard.DepartmentId);

				if (customStateIds.Any(id => !owned.Contains(id)))
					throw new ArgumentException("A status selection does not belong to this department.", nameof(runCard));
			}
		}

		/// <summary>
		/// Nothing ties a stored run card to the unit types, roles, call types, stations and
		/// status details it names, so deleting one of those leaves the card holding an id
		/// that ValidateRunCardReferencesAsync now rejects. The editor cannot show such an id
		/// (it only lists what the department still has), so it cannot be removed by hand
		/// either, and every save of the card fails. This strips exactly what the validator
		/// would reject, using the same lookups, so a graph loaded for editing can be saved.
		/// Each stripped entry could never have matched anything; none of it is persisted
		/// until the caller saves.
		/// </summary>
		public async Task<int> RemoveDetachedReferencesAsync(RunCard runCard)
		{
			if (runCard == null)
				return 0;

			var removed = 0;

			if (runCard.HomeStationGroupId.HasValue
				&& !(await GetDepartmentStationIdsAsync(runCard.DepartmentId)).Contains(runCard.HomeStationGroupId.Value))
			{
				runCard.HomeStationGroupId = null;
				removed++;
			}

			var triggers = runCard.Triggers?.ToList() ?? new List<RunCardTrigger>();

			if (triggers.Any(t => t.CallTypeId.HasValue))
			{
				var callTypeIds = await GetDepartmentCallTypeIdsAsync(runCard.DepartmentId);
				var kept = new List<RunCardTrigger>();

				foreach (var trigger in triggers)
				{
					if (!trigger.CallTypeId.HasValue || callTypeIds.Contains(trigger.CallTypeId.Value))
					{
						kept.Add(trigger);
						continue;
					}

					removed++;

					// A priority-only trigger never reads its call type, so it keeps matching with
					// the dead id cleared; a type trigger could never match again and is dropped.
					if (trigger.TriggerType == (int)RunCardTriggerTypes.CallPriority)
					{
						trigger.CallTypeId = null;
						kept.Add(trigger);
					}
				}

				runCard.Triggers = kept;
			}

			var levels = runCard.AlarmLevels?.ToList() ?? new List<RunCardAlarmLevel>();
			var selections = runCard.AvailabilitySelections?.ToList() ?? new List<RunCardAvailabilitySelection>();

			if (levels.Any(l => l.UnitRequirements != null && l.UnitRequirements.Any()) || selections.Any(s => s.UnitTypeId.HasValue))
			{
				var unitTypeIds = await GetDepartmentUnitTypeIdsAsync(runCard.DepartmentId);

				foreach (var level in levels.Where(l => l.UnitRequirements != null))
				{
					var keptRequirements = level.UnitRequirements.Where(r => unitTypeIds.Contains(r.UnitTypeId)).ToList();
					removed += level.UnitRequirements.Count - keptRequirements.Count;
					level.UnitRequirements = keptRequirements;
				}

				var keptSelections = selections.Where(s => !s.UnitTypeId.HasValue || unitTypeIds.Contains(s.UnitTypeId.Value)).ToList();
				removed += selections.Count - keptSelections.Count;
				selections = keptSelections;
			}

			if (levels.Any(l => l.RoleRequirements != null && l.RoleRequirements.Any()))
			{
				var roleIds = await GetDepartmentRoleIdsAsync(runCard.DepartmentId);

				foreach (var level in levels.Where(l => l.RoleRequirements != null))
				{
					var keptRequirements = level.RoleRequirements.Where(r => roleIds.Contains(r.PersonnelRoleId)).ToList();
					removed += level.RoleRequirements.Count - keptRequirements.Count;
					level.RoleRequirements = keptRequirements;
				}
			}

			if (selections.Any(s => s.IsCustomState))
			{
				var owned = await GetDepartmentActiveCustomStateDetailIdsAsync(runCard.DepartmentId);

				var keptSelections = selections.Where(s => !s.IsCustomState || owned.Contains(s.StateId)).ToList();
				removed += selections.Count - keptSelections.Count;
				selections = keptSelections;
			}

			if (runCard.AvailabilitySelections != null)
				runCard.AvailabilitySelections = selections;

			// Conditional run cards: decision node targets and package references to things the department deleted.
			if (levels.Any(l => RunCardDecisionGraph.HasGraph(l) || l.ResponsePackageId.HasValue))
			{
				var unitTypeIds = await GetDepartmentUnitTypeIdsAsync(runCard.DepartmentId);
				var roleIds = await GetDepartmentRoleIdsAsync(runCard.DepartmentId);
				var packageIds = new HashSet<int>(((await _responsePackagesRepository.GetAllByDepartmentIdAsync(runCard.DepartmentId)) ?? Enumerable.Empty<ResponsePackage>())
					.Select(p => p.ResponsePackageId));

				foreach (var level in levels)
				{
					if (level.ResponsePackageId.HasValue && !packageIds.Contains(level.ResponsePackageId.Value))
					{
						level.ResponsePackageId = null;
						removed++;
					}

					foreach (var node in level.DecisionNodes ?? new List<RunCardDecisionNode>())
					{
						removed += RunCardTargetRules.RemoveDetached(node.SubjectTargets, unitTypeIds, roleIds);
						removed += RunCardTargetRules.RemoveDetached(node.ReferenceTargets, unitTypeIds, roleIds);

						if (node.ResponsePackageId.HasValue && !packageIds.Contains(node.ResponsePackageId.Value))
						{
							node.ResponsePackageId = null;
							removed++;
						}
					}
				}
			}

			return removed;
		}

		private async Task<HashSet<int>> GetDepartmentStationIdsAsync(int departmentId)
		{
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId);

			return new HashSet<int>((stations ?? new List<DepartmentGroup>()).Select(s => s.DepartmentGroupId));
		}

		private async Task<HashSet<int>> GetDepartmentCallTypeIdsAsync(int departmentId)
		{
			var callTypes = await _callTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			return new HashSet<int>((callTypes ?? Enumerable.Empty<CallType>()).Select(t => t.CallTypeId));
		}

		private async Task<HashSet<int>> GetDepartmentUnitTypeIdsAsync(int departmentId)
		{
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId);

			return new HashSet<int>((unitTypes ?? new List<UnitType>()).Select(t => t.UnitTypeId));
		}

		private async Task<HashSet<int>> GetDepartmentRoleIdsAsync(int departmentId)
		{
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(departmentId);

			return new HashSet<int>((roles ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId));
		}

		private async Task<HashSet<int>> GetDepartmentActiveCustomStateDetailIdsAsync(int departmentId)
		{
			var owned = new HashSet<int>();

			foreach (var state in await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(departmentId) ?? new List<CustomState>())
				AddActiveDetailIds(owned, state);

			AddActiveDetailIds(owned, await _customStateService.GetActivePersonnelStateForDepartmentAsync(departmentId));
			AddActiveDetailIds(owned, await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId));

			return owned;
		}

		private static void AddActiveDetailIds(HashSet<int> ids, CustomState state)
		{
			if (state == null)
				return;

			foreach (var detail in state.GetActiveDetails() ?? new List<CustomStateDetail>())
				ids.Add(detail.CustomStateDetailId);
		}

		public async Task<bool> DeleteRunCardAsync(int runCardId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var card = await GetRunCardByIdAsync(runCardId);

			if (card == null)
				return false;

			// Children are removed before the header, so a partial failure would strand rows
			// pointing at a card that no longer exists (or leave a card with no alarm levels).
			var nodeTargets = (card.AlarmLevels ?? Enumerable.Empty<RunCardAlarmLevel>()).Any(RunCardDecisionGraph.HasGraph)
				? await GetDepartmentNodeTargetsAsync(card.DepartmentId)
				: new List<RunCardTarget>();

			_unitOfWork.CreateOrGetConnection();
			try
			{
				foreach (var level in card.AlarmLevels ?? Enumerable.Empty<RunCardAlarmLevel>())
				{
					foreach (var req in level.UnitRequirements ?? Enumerable.Empty<RunCardUnitRequirement>())
						await _runCardUnitRequirementsRepository.DeleteAsync(req, cancellationToken);

					foreach (var req in level.RoleRequirements ?? Enumerable.Empty<RunCardRoleRequirement>())
						await _runCardRoleRequirementsRepository.DeleteAsync(req, cancellationToken);

					foreach (var node in level.DecisionNodes ?? Enumerable.Empty<RunCardDecisionNode>())
						await DeleteNodeAsync(node, nodeTargets, cancellationToken);

					await _runCardAlarmLevelsRepository.DeleteAsync(level, cancellationToken);
				}

				foreach (var trigger in card.Triggers ?? Enumerable.Empty<RunCardTrigger>())
					await _runCardTriggersRepository.DeleteAsync(trigger, cancellationToken);

				foreach (var selection in card.AvailabilitySelections ?? Enumerable.Empty<RunCardAvailabilitySelection>())
					await _runCardAvailabilitySelectionsRepository.DeleteAsync(selection, cancellationToken);

				await _runCardsRepository.DeleteAsync(card, cancellationToken);

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await InvalidateRunCardsInCacheAsync(card.DepartmentId);

			return true;
		}

		public Task<RunCard> GetMatchingRunCardAsync(int departmentId, int priority, string callTypeName)
		{
			return GetMatchingRunCardAsync(departmentId, priority, callTypeName, null);
		}

		public async Task<RunCard> GetMatchingRunCardAsync(int departmentId, int priority, string callTypeName, int? linkedCallTypeId)
		{
			var cards = await GetAllRunCardsForDepartmentAsync(departmentId);

			if (cards == null || !cards.Any())
				return null;

			// The name wins when it matches a type; the call's link covers a type renamed after the call was logged.
			var callTypeId = await ResolveCallTypeIdAsync(departmentId, callTypeName) ?? linkedCallTypeId;
			var now = DateTime.UtcNow;

			RunCard bestCard = null;
			int bestSpecificity = 0;

			foreach (var card in cards.Where(c => !c.IsDisabled))
			{
				var specificity = GetTriggerMatchSpecificity(card, priority, callTypeId, now);

				if (!specificity.HasValue)
					continue;

				// Specificity wins; ties break to the newest card (highest id).
				if (specificity.Value > bestSpecificity
					|| (specificity.Value == bestSpecificity && bestCard != null && card.RunCardId > bestCard.RunCardId))
				{
					bestCard = card;
					bestSpecificity = specificity.Value;
				}
			}

			return bestCard;
		}

		/// <summary>
		/// Evaluates a card's triggers against a call's priority and resolved call type id.
		/// Returns the strongest matching trigger's specificity (3 = priority+type,
		/// 2 = type, 1 = priority) or null when no trigger matches. Trigger time windows
		/// follow DispatchProtocol semantics: a window bound that is null is open-ended.
		/// </summary>
		public static int? GetTriggerMatchSpecificity(RunCard card, int priority, int? callTypeId, DateTime utcNow)
		{
			if (card?.Triggers == null || !card.Triggers.Any())
				return null;

			int? best = null;

			foreach (var trigger in card.Triggers)
			{
				if (trigger.StartsOn.HasValue && trigger.StartsOn.Value > utcNow)
					continue;

				if (trigger.EndsOn.HasValue && trigger.EndsOn.Value < utcNow)
					continue;

				int? specificity = null;

				switch ((RunCardTriggerTypes)trigger.TriggerType)
				{
					case RunCardTriggerTypes.CallPriority:
						if (trigger.Priority.HasValue && trigger.Priority.Value == priority)
							specificity = 1;
						break;
					case RunCardTriggerTypes.CallType:
						if (trigger.CallTypeId.HasValue && callTypeId.HasValue && trigger.CallTypeId.Value == callTypeId.Value)
							specificity = 2;
						break;
					case RunCardTriggerTypes.CallPriorityAndType:
						if (trigger.Priority.HasValue && trigger.Priority.Value == priority
							&& trigger.CallTypeId.HasValue && callTypeId.HasValue && trigger.CallTypeId.Value == callTypeId.Value)
							specificity = 3;
						break;
				}

				if (specificity.HasValue && (!best.HasValue || specificity.Value > best.Value))
					best = specificity;
			}

			return best;
		}

		public async Task<List<StationCoverageRequirement>> GetStationCoverageRequirementsForDepartmentAsync(int departmentId)
		{
			var requirements = await _stationCoverageRequirementsRepository.GetAllByDepartmentIdAsync(departmentId);

			if (requirements == null)
				return new List<StationCoverageRequirement>();

			return requirements.ToList();
		}

		public async Task<StationCoverageRequirement> SaveStationCoverageRequirementAsync(StationCoverageRequirement requirement, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (requirement == null)
				throw new ArgumentNullException(nameof(requirement));

			if (!requirement.UnitTypeId.HasValue && !requirement.PersonnelRoleId.HasValue)
				throw new ArgumentException("A station coverage requirement needs a unit type or a personnel role.", nameof(requirement));

			if (requirement.UnitTypeId.HasValue && requirement.PersonnelRoleId.HasValue)
				throw new ArgumentException("A station coverage requirement cannot target both a unit type and a personnel role.", nameof(requirement));

			// Same client-supplied ids as a run card, same ownership bar.
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(requirement.DepartmentId);

			if (stations == null || stations.All(s => s.DepartmentGroupId != requirement.DepartmentGroupId))
				throw new ArgumentException("The station does not belong to this department.", nameof(requirement));

			if (requirement.UnitTypeId.HasValue)
			{
				var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(requirement.DepartmentId);

				if (unitTypes == null || unitTypes.All(t => t.UnitTypeId != requirement.UnitTypeId.Value))
					throw new ArgumentException("The unit type does not belong to this department.", nameof(requirement));
			}

			if (requirement.PersonnelRoleId.HasValue)
			{
				var roles = await _personnelRolesService.GetRolesForDepartmentAsync(requirement.DepartmentId);

				if (roles == null || roles.All(r => r.PersonnelRoleId != requirement.PersonnelRoleId.Value))
					throw new ArgumentException("The personnel role does not belong to this department.", nameof(requirement));
			}

			return await _stationCoverageRequirementsRepository.SaveOrUpdateAsync(requirement, cancellationToken, true);
		}

		public async Task<bool> DeleteStationCoverageRequirementAsync(int stationCoverageRequirementId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var requirement = await _stationCoverageRequirementsRepository.GetByIdAsync(stationCoverageRequirementId);

			if (requirement == null || requirement.DepartmentId != departmentId)
				return false;

			return await _stationCoverageRequirementsRepository.DeleteAsync(requirement, cancellationToken);
		}

		public async Task<Dictionary<int, DateTime>> GetLastUnitDispatchTimesAsync(int departmentId)
		{
			var rows = await _runCardsRepository.GetLastUnitDispatchTimesByDepartmentAsync(departmentId);

			if (rows == null)
				return new Dictionary<int, DateTime>();

			return rows.ToDictionary(x => x.UnitId, x => x.LastDispatchedOn);
		}

		public async Task<Dictionary<string, DateTime>> GetLastUserDispatchTimesAsync(int departmentId)
		{
			var rows = await _runCardsRepository.GetLastUserDispatchTimesByDepartmentAsync(departmentId);

			if (rows == null)
				return new Dictionary<string, DateTime>();

			return rows.Where(x => !string.IsNullOrWhiteSpace(x.UserId))
				.ToDictionary(x => x.UserId, x => x.LastDispatchedOn);
		}

		private async Task HydrateRunCardAsync(RunCard card, List<RunCardTarget> nodeTargets)
		{
			var triggers = await _runCardTriggersRepository.GetTriggersByRunCardIdAsync(card.RunCardId);
			card.Triggers = triggers?.ToList() ?? new List<RunCardTrigger>();

			var levels = (await _runCardAlarmLevelsRepository.GetAlarmLevelsByRunCardIdAsync(card.RunCardId))?.ToList()
				?? new List<RunCardAlarmLevel>();
			var unitReqs = (await _runCardUnitRequirementsRepository.GetUnitRequirementsByRunCardIdAsync(card.RunCardId))?.ToList()
				?? new List<RunCardUnitRequirement>();
			var roleReqs = (await _runCardRoleRequirementsRepository.GetRoleRequirementsByRunCardIdAsync(card.RunCardId))?.ToList()
				?? new List<RunCardRoleRequirement>();
			var nodes = (await _runCardDecisionNodesRepository.GetNodesByRunCardIdAsync(card.RunCardId))?.ToList()
				?? new List<RunCardDecisionNode>();

			foreach (var node in nodes)
			{
				node.SubjectTargets = (nodeTargets ?? new List<RunCardTarget>())
					.Where(t => t.OwnerKind == (int)RunCardTargetOwners.NodeSubject && t.OwnerId == node.RunCardDecisionNodeId).OrderBy(t => t.RunCardTargetId).ToList();
				node.ReferenceTargets = (nodeTargets ?? new List<RunCardTarget>())
					.Where(t => t.OwnerKind == (int)RunCardTargetOwners.NodeReference && t.OwnerId == node.RunCardDecisionNodeId).OrderBy(t => t.RunCardTargetId).ToList();
			}

			foreach (var level in levels)
			{
				level.UnitRequirements = unitReqs.Where(r => r.RunCardAlarmLevelId == level.RunCardAlarmLevelId).ToList();
				level.RoleRequirements = roleReqs.Where(r => r.RunCardAlarmLevelId == level.RunCardAlarmLevelId).ToList();
				level.DecisionNodes = nodes.Where(n => n.RunCardAlarmLevelId == level.RunCardAlarmLevelId).ToList();
			}

			card.AlarmLevels = levels;

			var selections = await _runCardAvailabilitySelectionsRepository.GetSelectionsByRunCardIdAsync(card.RunCardId);
			card.AvailabilitySelections = selections?.ToList() ?? new List<RunCardAvailabilitySelection>();
		}

		private async Task<int?> ResolveCallTypeIdAsync(int departmentId, string callTypeName)
		{
			if (string.IsNullOrWhiteSpace(callTypeName))
				return null;

			var types = await _callTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			return CallTypeLink.FindByName(types, callTypeName)?.CallTypeId;
		}

		private async Task InvalidateRunCardsInCacheAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(RunCardsCacheKey, departmentId));
		}
	}
}
