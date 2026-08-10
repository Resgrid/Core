using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <inheritdoc />
	public class IncidentBoardNarrator : IIncidentBoardNarrator
	{
		/// <summary>Default number of timeline entries returned when the question didn't say how many.</summary>
		private const int DefaultTimelineEntries = 10;

		/// <summary>Hard cap on any single list so an assistant reply stays readable on a phone.</summary>
		private const int MaxListItems = 25;

		/// <summary>NIMS guidance: one supervisor should manage between three and seven resources.</summary>
		private const int SpanOfControlCeiling = 7;

		private readonly IUserProfileService _userProfileService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IIncidentCommandService _incidentCommandService;

		public IncidentBoardNarrator(
			IUserProfileService userProfileService,
			IUnitsService unitsService,
			IDepartmentsService departmentsService,
			IIncidentCommandService incidentCommandService)
		{
			_userProfileService = userProfileService;
			_unitsService = unitsService;
			_departmentsService = departmentsService;
			_incidentCommandService = incidentCommandService;
		}

		#region Status / size-up

		public async Task<string> DescribeStatusAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var names = await GetPersonNamesAsync(session);
			var board = context.Board;
			var command = board.Command;

			var sb = new StringBuilder();
			sb.AppendLine(Header(context));
			sb.AppendLine(ChatbotResources.Get("Incident_Elapsed", culture, FormatDuration(DateTime.UtcNow - command.EstablishedOn)));

			var commander = ResolveName(names, command.CurrentCommanderUserId);
			if (!string.IsNullOrWhiteSpace(commander))
				sb.AppendLine(ChatbotResources.Get("Incident_Commander", culture, commander));

			if (!string.IsNullOrWhiteSpace(command.CommandPostLocationText))
				sb.AppendLine(ChatbotResources.Get("Incident_CommandPost", culture, command.CommandPostLocationText));

			if (!string.IsNullOrWhiteSpace(command.StagingLocationText))
				sb.AppendLine(ChatbotResources.Get("Incident_Staging", culture, command.StagingLocationText));

			var liveNodes = LiveNodes(board);
			var liveAssignments = LiveAssignments(board);
			var unitCount = liveAssignments.Count(a => IsUnitKind(a.ResourceKind));
			var personnelCount = liveAssignments.Count - unitCount;
			var unassigned = liveAssignments.Count(a => string.IsNullOrWhiteSpace(a.CommandStructureNodeId));

			sb.AppendLine(ChatbotResources.Get("Incident_ResourceCounts", culture, unitCount, personnelCount, liveNodes.Count, unassigned));

			var par = ParBuckets(board);
			if (par.Total > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_ParSummary", culture, par.Total, par.Critical, par.Warning));

			var objectives = board.Objectives ?? new List<TacticalObjective>();
			if (objectives.Count > 0)
			{
				var complete = objectives.Count(o => o.Status == (int)TacticalObjectiveStatus.Complete);
				sb.AppendLine(ChatbotResources.Get("Incident_ObjectiveSummary", culture, complete, objectives.Count));
			}

			var openNeeds = (board.Needs ?? new List<IncidentNeed>())
				.Count(n => n.Status == (int)IncidentNeedStatus.Open || n.Status == (int)IncidentNeedStatus.PartiallyMet);
			if (openNeeds > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_OpenNeeds", culture, openNeeds));

			var dueTimers = (board.Timers ?? new List<IncidentTimer>())
				.Count(t => t.Status == (int)IncidentTimerStatus.Due);
			if (dueTimers > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_TimersDue", culture, dueTimers));

			if (!string.IsNullOrWhiteSpace(command.ImportantInformation))
				sb.AppendLine(ChatbotResources.Get("Incident_Important", culture, command.ImportantInformation.Truncate(240)));

			if (command.EstimatedEndOn.HasValue)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				sb.AppendLine(ChatbotResources.Get("Incident_EstimatedEnd", culture, command.EstimatedEndOn.Value.TimeConverterToString(department)));
			}

			return sb.ToString().TrimEnd();
		}

		#endregion Status / size-up

		#region Accountability (PAR)

		public Task<string> DescribeParAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var rows = context.Board.Accountability ?? new List<PersonnelCallCheckInStatus>();

			if (rows.Count == 0)
				return Task.FromResult(ChatbotResources.Get("Incident_ParNone", culture, IncidentLabel(context)));

			var critical = rows.Where(r => IsCritical(r)).OrderBy(r => r.MinutesRemaining).ToList();
			var warning = rows.Where(r => IsWarning(r)).OrderBy(r => r.MinutesRemaining).ToList();
			var green = rows.Count - critical.Count - warning.Count;

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_ParHeader", culture, IncidentLabel(context), rows.Count));
			sb.AppendLine(ChatbotResources.Get("Incident_ParCounts", culture, green, warning.Count, critical.Count));

			if (critical.Count > 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_ParCriticalHeader", culture));
				foreach (var row in critical.Take(MaxListItems))
					sb.AppendLine(ChatbotResources.Get("Incident_ParOverdueRow", culture, PersonLabel(row), Math.Abs((int)Math.Round(row.MinutesRemaining))));
			}

			if (warning.Count > 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_ParWarningHeader", culture));
				foreach (var row in warning.Take(MaxListItems))
					sb.AppendLine(ChatbotResources.Get("Incident_ParDueRow", culture, PersonLabel(row), Math.Max(0, (int)Math.Round(row.MinutesRemaining))));
			}

			if (critical.Count == 0 && warning.Count == 0)
				sb.AppendLine(ChatbotResources.Get("Incident_ParAllGood", culture));

			return Task.FromResult(sb.ToString().TrimEnd());
		}

		#endregion Accountability (PAR)

		#region Resources

		public async Task<string> DescribeResourcesAsync(IncidentContext context, ChatbotSession session, string laneName)
		{
			var culture = session?.Culture;
			var board = context.Board;
			var liveAssignments = LiveAssignments(board);
			var liveNodes = LiveNodes(board);
			var resourceNames = await BuildResourceNameLookupAsync(context, session);

			// "Who is unassigned?" — everything tracked on the incident but not yet placed in a lane.
			if (!string.IsNullOrWhiteSpace(laneName) && laneName.Trim().Equals("unassigned", StringComparison.OrdinalIgnoreCase))
			{
				var pool = liveAssignments.Where(a => string.IsNullOrWhiteSpace(a.CommandStructureNodeId)).ToList();
				if (pool.Count == 0)
					return ChatbotResources.Get("Incident_NoUnassigned", culture, IncidentLabel(context));

				var poolSb = new StringBuilder();
				poolSb.AppendLine(ChatbotResources.Get("Incident_UnassignedHeader", culture, IncidentLabel(context), pool.Count));
				foreach (var assignment in pool.Take(MaxListItems))
					poolSb.AppendLine("- " + ResourceLabel(assignment, resourceNames));
				return poolSb.ToString().TrimEnd();
			}

			// A named lane — match on the lane's own name first, then on its ICS type word.
			if (!string.IsNullOrWhiteSpace(laneName))
			{
				var node = MatchNode(liveNodes, laneName);
				if (node == null)
					return ChatbotResources.Get("Incident_LaneNotFound", culture, laneName.Trim(),
						liveNodes.Count == 0 ? ChatbotResources.Get("Incident_NoLanes", culture) : string.Join(", ", liveNodes.Select(n => n.Name)));

				var inLane = liveAssignments.Where(a => string.Equals(a.CommandStructureNodeId, node.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase)).ToList();
				var names = await GetPersonNamesAsync(session);

				var laneSb = new StringBuilder();
				laneSb.AppendLine(ChatbotResources.Get("Incident_LaneHeader", culture, node.Name, NodeTypeName(node.NodeType), inLane.Count));

				var lead = LaneLead(node, names);
				if (!string.IsNullOrWhiteSpace(lead))
					laneSb.AppendLine(ChatbotResources.Get("Incident_LaneLead", culture, lead));

				if (inLane.Count == 0)
					laneSb.AppendLine(ChatbotResources.Get("Incident_LaneEmpty", culture));
				else
					foreach (var assignment in inLane.Take(MaxListItems))
						laneSb.AppendLine("- " + ResourceLabel(assignment, resourceNames) + FormatTimeInLane(assignment, culture));

				var laneObjective = FindObjective(board, node.PrimaryObjectiveId);
				if (laneObjective != null)
					laneSb.AppendLine(ChatbotResources.Get("Incident_LaneObjective", culture, laneObjective.Name, laneObjective.ProgressPercent));

				return laneSb.ToString().TrimEnd();
			}

			// Whole incident.
			if (liveAssignments.Count == 0 && context.AdHocUnits.Count == 0 && context.AdHocPersonnel.Count == 0)
				return ChatbotResources.Get("Incident_NoResources", culture, IncidentLabel(context));

			var units = liveAssignments.Count(a => IsUnitKind(a.ResourceKind));
			var people = liveAssignments.Count - units;

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_ResourcesHeader", culture, IncidentLabel(context), units, people));

			foreach (var node in liveNodes.Take(MaxListItems))
			{
				var inLane = liveAssignments.Where(a => string.Equals(a.CommandStructureNodeId, node.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase)).ToList();
				sb.AppendLine(ChatbotResources.Get("Incident_LaneLine", culture, node.Name, inLane.Count,
					inLane.Count == 0 ? string.Empty : string.Join(", ", inLane.Take(6).Select(a => ResourceLabel(a, resourceNames)))));
			}

			var unassignedCount = liveAssignments.Count(a => string.IsNullOrWhiteSpace(a.CommandStructureNodeId));
			if (unassignedCount > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_UnassignedLine", culture, unassignedCount));

			var external = context.AdHocUnits.Count(u => !u.ReleasedOn.HasValue) + context.AdHocPersonnel.Count(p => !p.ReleasedOn.HasValue);
			if (external > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_ExternalResources", culture, external));

			return sb.ToString().TrimEnd();
		}

		public async Task<string> DescribeSpanOfControlAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var board = context.Board;
			var liveNodes = LiveNodes(board);
			var liveAssignments = LiveAssignments(board);
			var names = await GetPersonNamesAsync(session);

			if (liveNodes.Count == 0)
				return ChatbotResources.Get("Incident_NoLanesYet", culture, IncidentLabel(context));

			var over = new List<string>();
			var under = new List<string>();
			var leaderless = new List<string>();

			foreach (var node in liveNodes)
			{
				var count = liveAssignments.Count(a => string.Equals(a.CommandStructureNodeId, node.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase));

				// The lane's own MaxUnits wins when the commander configured one; otherwise NIMS' 7.
				var ceiling = node.MaxUnits > 0 ? node.MaxUnits : SpanOfControlCeiling;
				if (count > ceiling)
					over.Add(ChatbotResources.Get("Incident_SpanOverRow", culture, node.Name, count, ceiling));

				if (node.MinUnits > 0 && count < node.MinUnits)
					under.Add(ChatbotResources.Get("Incident_SpanUnderRow", culture, node.Name, count, node.MinUnits));

				if (count > 0 && string.IsNullOrWhiteSpace(LaneLead(node, names)))
					leaderless.Add(node.Name);
			}

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_SpanHeader", culture, IncidentLabel(context), liveNodes.Count, liveAssignments.Count));

			if (over.Count == 0 && under.Count == 0 && leaderless.Count == 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_SpanAllGood", culture, SpanOfControlCeiling));
				return sb.ToString().TrimEnd();
			}

			foreach (var line in over)
				sb.AppendLine(line);
			foreach (var line in under)
				sb.AppendLine(line);

			if (leaderless.Count > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_SpanNoLead", culture, string.Join(", ", leaderless)));

			return sb.ToString().TrimEnd();
		}

		#endregion Resources

		#region Objectives / needs / timers

		public Task<string> DescribeObjectivesAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var objectives = (context.Board.Objectives ?? new List<TacticalObjective>())
				.OrderBy(o => o.SortOrder)
				.ToList();

			var playbook = IcsPlaybooks.Infer(context.Call, context.Command?.Name);
			var sb = new StringBuilder();

			if (objectives.Count == 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_NoObjectives", culture, IncidentLabel(context)));
			}
			else
			{
				var open = objectives.Where(o => o.Status != (int)TacticalObjectiveStatus.Complete).ToList();
				var complete = objectives.Count - open.Count;

				sb.AppendLine(ChatbotResources.Get("Incident_ObjectivesHeader", culture, IncidentLabel(context), complete, objectives.Count));

				foreach (var objective in open.Take(MaxListItems))
					sb.AppendLine("- " + FormatObjective(objective, culture));

				if (open.Count == 0)
					sb.AppendLine(ChatbotResources.Get("Incident_ObjectivesAllComplete", culture));
			}

			// Doctrine benchmarks for this incident type that aren't tracked on the board yet.
			var missing = playbook.Benchmarks
				.Where(benchmark => !objectives.Any(o => LooseMatch(o.Name, benchmark)))
				.Take(6)
				.ToList();

			if (missing.Count > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_MissingBenchmarks", culture, playbook.DisplayName, string.Join("; ", missing)));

			return Task.FromResult(sb.ToString().TrimEnd());
		}

		public async Task<string> DescribeNeedsAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var needs = (context.Board.Needs ?? new List<IncidentNeed>()).ToList();

			if (needs.Count == 0)
				return ChatbotResources.Get("Incident_NoNeeds", culture, IncidentLabel(context));

			var outstanding = needs
				.Where(n => n.Status == (int)IncidentNeedStatus.Open || n.Status == (int)IncidentNeedStatus.PartiallyMet)
				.OrderByDescending(n => n.Priority)
				.ThenBy(n => n.CreatedOn)
				.ToList();

			var met = needs.Count(n => n.Status == (int)IncidentNeedStatus.Met);
			var cancelled = needs.Count(n => n.Status == (int)IncidentNeedStatus.Cancelled);

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_NeedsHeader", culture, IncidentLabel(context), outstanding.Count, met, cancelled));

			if (outstanding.Count == 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_NeedsAllMet", culture));
				return sb.ToString().TrimEnd();
			}

			foreach (var need in outstanding.Take(MaxListItems))
			{
				var quantity = need.QuantityRequested > 0
					? ChatbotResources.Get("Incident_NeedQuantity", culture, need.QuantityFulfilled, need.QuantityRequested)
					: string.Empty;

				sb.AppendLine(ChatbotResources.Get("Incident_NeedRow", culture,
					need.Name,
					NeedCategoryName(need.Category),
					quantity,
					FormatDuration(DateTime.UtcNow - need.CreatedOn)));
			}

			await Task.CompletedTask;
			return sb.ToString().TrimEnd();
		}

		public Task<string> DescribeTimersAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var timers = (context.Board.Timers ?? new List<IncidentTimer>())
				.Where(t => t.Status != (int)IncidentTimerStatus.Stopped)
				.OrderBy(t => t.NextDueOn ?? DateTime.MaxValue)
				.ToList();

			if (timers.Count == 0)
				return Task.FromResult(ChatbotResources.Get("Incident_NoTimers", culture, IncidentLabel(context)));

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_TimersHeader", culture, IncidentLabel(context), timers.Count));

			foreach (var timer in timers.Take(MaxListItems))
			{
				if (timer.Status == (int)IncidentTimerStatus.Due)
				{
					sb.AppendLine(ChatbotResources.Get("Incident_TimerDueRow", culture, timer.Name));
					continue;
				}

				var remaining = timer.NextDueOn.HasValue ? timer.NextDueOn.Value - DateTime.UtcNow : (TimeSpan?)null;
				sb.AppendLine(remaining.HasValue && remaining.Value > TimeSpan.Zero
					? ChatbotResources.Get("Incident_TimerRunningRow", culture, timer.Name, FormatDuration(remaining.Value))
					: ChatbotResources.Get("Incident_TimerNoDueRow", culture, timer.Name));
			}

			return Task.FromResult(sb.ToString().TrimEnd());
		}

		#endregion Objectives / needs / timers

		#region Roles

		public async Task<string> DescribeRolesAsync(IncidentContext context, ChatbotSession session, string roleQuery)
		{
			var culture = session?.Culture;
			var names = await GetPersonNamesAsync(session);
			var active = (context.Board.Roles ?? new List<IncidentRoleAssignment>())
				.Where(r => !r.RemovedOn.HasValue)
				.ToList();

			// RIT/RIC is a lane on a Resgrid board, not an ICS position — answer from the structure.
			if (IncidentRoleVocabulary.IsRapidIntervention(roleQuery))
			{
				var ritNode = LiveNodes(context.Board)
					.FirstOrDefault(n => n.Name != null && (n.Name.IndexOf("rit", StringComparison.OrdinalIgnoreCase) >= 0
						|| n.Name.IndexOf("ric", StringComparison.OrdinalIgnoreCase) >= 0
						|| n.Name.IndexOf("rapid intervention", StringComparison.OrdinalIgnoreCase) >= 0));

				if (ritNode == null)
					return ChatbotResources.Get("Incident_NoRit", culture, IncidentLabel(context));

				var ritCount = LiveAssignments(context.Board)
					.Count(a => string.Equals(a.CommandStructureNodeId, ritNode.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase));

				return ChatbotResources.Get("Incident_RitFound", culture, ritNode.Name, ritCount);
			}

			if (!string.IsNullOrWhiteSpace(roleQuery))
			{
				var role = IncidentRoleVocabulary.Resolve(roleQuery);
				if (!role.HasValue)
					return ChatbotResources.Get("Incident_RoleUnknown", culture, roleQuery.Trim());

				// The Incident Commander is the command row's own field, not a role assignment.
				if (role.Value == IncidentRoleType.IncidentCommander)
				{
					var commander = ResolveName(names, context.Command.CurrentCommanderUserId);
					return string.IsNullOrWhiteSpace(commander)
						? ChatbotResources.Get("Incident_RoleUnfilled", culture, IncidentRoleVocabulary.DisplayName(role.Value), IncidentLabel(context))
						: ChatbotResources.Get("Incident_RoleFilled", culture, IncidentRoleVocabulary.DisplayName(role.Value), commander);
				}

				var holders = active.Where(r => r.RoleType == (int)role.Value).Select(r => ResolveName(names, r.UserId)).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

				return holders.Count == 0
					? ChatbotResources.Get("Incident_RoleUnfilled", culture, IncidentRoleVocabulary.DisplayName(role.Value), IncidentLabel(context))
					: ChatbotResources.Get("Incident_RoleFilled", culture, IncidentRoleVocabulary.DisplayName(role.Value), string.Join(", ", holders));
			}

			var playbook = IcsPlaybooks.Infer(context.Call, context.Command?.Name);
			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_RolesHeader", culture, IncidentLabel(context), active.Count));

			var commanderName = ResolveName(names, context.Command.CurrentCommanderUserId);
			if (!string.IsNullOrWhiteSpace(commanderName))
				sb.AppendLine("- " + ChatbotResources.Get("Incident_RoleRow", culture, IncidentRoleVocabulary.DisplayName(IncidentRoleType.IncidentCommander), commanderName));

			foreach (var assignment in active.OrderBy(r => r.RoleType).Take(MaxListItems))
				sb.AppendLine("- " + ChatbotResources.Get("Incident_RoleRow", culture, IncidentRoleVocabulary.DisplayName((IncidentRoleType)assignment.RoleType), ResolveName(names, assignment.UserId)));

			var filled = new HashSet<IncidentRoleType>(active.Select(r => (IncidentRoleType)r.RoleType));
			if (!string.IsNullOrWhiteSpace(commanderName))
				filled.Add(IncidentRoleType.IncidentCommander);

			var unfilled = IcsPlaybooks.KeyRolesFor(playbook).Where(r => !filled.Contains(r)).ToList();
			if (unfilled.Count > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_RolesUnfilled", culture, playbook.DisplayName,
					string.Join(", ", unfilled.Select(IncidentRoleVocabulary.DisplayName))));

			return sb.ToString().TrimEnd();
		}

		#endregion Roles

		#region Timeline / notes

		public async Task<string> DescribeTimelineAsync(IncidentContext context, ChatbotSession session, int? minutes, int? count)
		{
			var culture = session?.Culture;
			var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
			var names = await GetPersonNamesAsync(session);

			var entries = await _incidentCommandService.GetTimelineForCallAsync(session.DepartmentId, context.Call.CallId)
				?? new List<CommandLogEntry>();

			// Per-call reads span every command that has run on the call — keep it to this one.
			entries = entries
				.Where(e => string.Equals(e.IncidentCommandId, context.Command.IncidentCommandId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(e => e.OccurredOn)
				.ToList();

			if (minutes.HasValue && minutes.Value > 0)
			{
				var cutoff = DateTime.UtcNow.AddMinutes(-minutes.Value);
				entries = entries.Where(e => e.OccurredOn >= cutoff).ToList();

				if (entries.Count == 0)
					return ChatbotResources.Get("Incident_TimelineEmptyWindow", culture, minutes.Value, IncidentLabel(context));
			}

			if (entries.Count == 0)
				return ChatbotResources.Get("Incident_TimelineEmpty", culture, IncidentLabel(context));

			var take = Math.Min(count.GetValueOrDefault(DefaultTimelineEntries) <= 0 ? DefaultTimelineEntries : count.GetValueOrDefault(DefaultTimelineEntries), MaxListItems);

			var sb = new StringBuilder();
			sb.AppendLine(minutes.HasValue && minutes.Value > 0
				? ChatbotResources.Get("Incident_TimelineWindowHeader", culture, IncidentLabel(context), minutes.Value, entries.Count)
				: ChatbotResources.Get("Incident_TimelineHeader", culture, IncidentLabel(context), Math.Min(take, entries.Count)));

			foreach (var entry in entries.Take(take))
			{
				var who = ResolveName(names, entry.UserId);
				sb.AppendLine(ChatbotResources.Get("Incident_TimelineRow", culture,
					entry.OccurredOn.TimeConverterToString(department),
					entry.Description?.Truncate(160),
					string.IsNullOrWhiteSpace(who) ? string.Empty : " — " + who));
			}

			return sb.ToString().TrimEnd();
		}

		public async Task<string> DescribeNotesAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
			var names = await GetPersonNamesAsync(session);

			var notes = (context.Board.Notes ?? new List<IncidentNote>())
				.Where(n => !n.DeletedOn.HasValue)
				.OrderByDescending(n => n.CreatedOn)
				.ToList();

			if (notes.Count == 0)
				return ChatbotResources.Get("Incident_NoNotes", culture, IncidentLabel(context));

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_NotesHeader", culture, IncidentLabel(context), notes.Count));

			foreach (var note in notes.Take(MaxListItems))
			{
				sb.AppendLine(ChatbotResources.Get("Incident_NoteRow", culture,
					note.CreatedOn.TimeConverterToString(department),
					string.IsNullOrWhiteSpace(note.Title) ? note.Body?.Truncate(200) : note.Title + ": " + note.Body?.Truncate(160),
					ResolveName(names, note.CreatedByUserId)));
			}

			return sb.ToString().TrimEnd();
		}

		#endregion Timeline / notes

		#region Briefing / checklist / weather

		public async Task<string> DescribeBriefingAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;
			var board = context.Board;
			var command = board.Command;
			var names = await GetPersonNamesAsync(session);
			var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
			var resourceNames = await BuildResourceNameLookupAsync(context, session);
			var playbook = IcsPlaybooks.Infer(context.Call, command.Name);

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingHeader", culture, IncidentLabel(context)));
			sb.AppendLine();

			// 1. Situation.
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingSituation", culture));
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingType", culture, playbook.DisplayName));
			if (!string.IsNullOrWhiteSpace(context.Call?.Address))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingAddress", culture, context.Call.Address));
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingEstablished", culture,
				command.EstablishedOn.TimeConverterToString(department), FormatDuration(DateTime.UtcNow - command.EstablishedOn)));
			if (!string.IsNullOrWhiteSpace(command.ImportantInformation))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingImportant", culture, command.ImportantInformation.Truncate(400)));
			sb.AppendLine();

			// 2. Command.
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingCommand", culture));
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingCommander", culture,
				ResolveName(names, command.CurrentCommanderUserId) ?? ChatbotResources.Get("Incident_Unknown", culture)));
			if (!string.IsNullOrWhiteSpace(command.CommandPostLocationText))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingIcp", culture, command.CommandPostLocationText));
			if (!string.IsNullOrWhiteSpace(command.StagingLocationText))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingStaging", culture, command.StagingLocationText));
			if (!string.IsNullOrWhiteSpace(command.RehabLocationText))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingRehab", culture, command.RehabLocationText));

			var activeRoles = (board.Roles ?? new List<IncidentRoleAssignment>()).Where(r => !r.RemovedOn.HasValue).ToList();
			foreach (var role in activeRoles.OrderBy(r => r.RoleType).Take(MaxListItems))
				sb.AppendLine("- " + ChatbotResources.Get("Incident_RoleRow", culture, IncidentRoleVocabulary.DisplayName((IncidentRoleType)role.RoleType), ResolveName(names, role.UserId)));
			sb.AppendLine();

			// 3. Objectives.
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingObjectives", culture));
			var objectives = (board.Objectives ?? new List<TacticalObjective>()).OrderBy(o => o.SortOrder).ToList();
			if (objectives.Count == 0)
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingNoObjectives", culture));
			else
				foreach (var objective in objectives.Take(MaxListItems))
					sb.AppendLine("- " + FormatObjective(objective, culture));
			if (!string.IsNullOrWhiteSpace(command.IncidentActionPlan))
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingActionPlan", culture, command.IncidentActionPlan.Truncate(400)));
			sb.AppendLine();

			// 4. Organization and resources.
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingOrganization", culture));
			var liveNodes = LiveNodes(board);
			var liveAssignments = LiveAssignments(board);
			if (liveNodes.Count == 0)
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingNoLanes", culture));
			else
				foreach (var node in liveNodes.Take(MaxListItems))
				{
					var inLane = liveAssignments.Where(a => string.Equals(a.CommandStructureNodeId, node.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase)).ToList();
					sb.AppendLine("- " + ChatbotResources.Get("Incident_BriefingLaneRow", culture, node.Name, LaneLead(node, names) ?? ChatbotResources.Get("Incident_NoLead", culture), inLane.Count,
						string.Join(", ", inLane.Take(6).Select(a => ResourceLabel(a, resourceNames)))));
				}

			var unassignedCount = liveAssignments.Count(a => string.IsNullOrWhiteSpace(a.CommandStructureNodeId));
			if (unassignedCount > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_UnassignedLine", culture, unassignedCount));
			sb.AppendLine();

			// 5. Accountability.
			var par = ParBuckets(board);
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingAccountability", culture));
			sb.AppendLine(par.Total == 0
				? ChatbotResources.Get("Incident_BriefingNoPar", culture)
				: ChatbotResources.Get("Incident_ParCounts", culture, par.Total - par.Warning - par.Critical, par.Warning, par.Critical));
			sb.AppendLine();

			// 6. Outstanding needs.
			var outstanding = (board.Needs ?? new List<IncidentNeed>())
				.Where(n => n.Status == (int)IncidentNeedStatus.Open || n.Status == (int)IncidentNeedStatus.PartiallyMet)
				.OrderByDescending(n => n.Priority)
				.ToList();
			sb.AppendLine(ChatbotResources.Get("Incident_BriefingNeeds", culture));
			if (outstanding.Count == 0)
				sb.AppendLine(ChatbotResources.Get("Incident_BriefingNoNeeds", culture));
			else
				foreach (var need in outstanding.Take(MaxListItems))
					sb.AppendLine("- " + need.Name + (need.QuantityRequested > 0
						? " " + ChatbotResources.Get("Incident_NeedQuantity", culture, need.QuantityFulfilled, need.QuantityRequested)
						: string.Empty));

			return sb.ToString().TrimEnd();
		}

		public async Task<string> DescribeChecklistAsync(IncidentContext context, ChatbotSession session, string incidentTypeText)
		{
			var culture = session?.Culture;
			var playbook = IcsPlaybooks.Resolve(incidentTypeText) ?? IcsPlaybooks.Infer(context.Call, context.Command?.Name);
			var board = context.Board;
			var names = await GetPersonNamesAsync(session);

			var objectives = board.Objectives ?? new List<TacticalObjective>();
			var activeRoles = (board.Roles ?? new List<IncidentRoleAssignment>()).Where(r => !r.RemovedOn.HasValue).ToList();
			var liveNodes = LiveNodes(board);

			// The board can prove a handful of these outright; the rest are prompts for the commander.
			var satisfied = new List<string>();
			var outstanding = new List<string>();

			if (!string.IsNullOrWhiteSpace(ResolveName(names, board.Command.CurrentCommanderUserId)))
				satisfied.Add(ChatbotResources.Get("Incident_CheckCommand", culture));
			else
				outstanding.Add(ChatbotResources.Get("Incident_CheckCommand", culture));

			AddCheck(satisfied, outstanding, !string.IsNullOrWhiteSpace(board.Command.CommandPostLocationText)
				|| !string.IsNullOrWhiteSpace(board.Command.CommandPostLatitude), ChatbotResources.Get("Incident_CheckIcp", culture));

			AddCheck(satisfied, outstanding, !string.IsNullOrWhiteSpace(board.Command.IncidentActionPlan) || objectives.Count > 0,
				ChatbotResources.Get("Incident_CheckActionPlan", culture));

			AddCheck(satisfied, outstanding, activeRoles.Any(r => r.RoleType == (int)IncidentRoleType.SafetyOfficer),
				ChatbotResources.Get("Incident_CheckSafety", culture));

			AddCheck(satisfied, outstanding, (board.Accountability?.Count ?? 0) > 0 || (board.Timers?.Count ?? 0) > 0,
				ChatbotResources.Get("Incident_CheckPar", culture));

			AddCheck(satisfied, outstanding,
				!string.IsNullOrWhiteSpace(board.Command.StagingLocationText)
					|| liveNodes.Any(n => n.NodeType == (int)CommandNodeType.Staging)
					|| activeRoles.Any(r => r.RoleType == (int)IncidentRoleType.StagingAreaManager),
				ChatbotResources.Get("Incident_CheckStaging", culture));

			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_ChecklistHeader", culture, playbook.DisplayName, IncidentLabel(context)));

			if (satisfied.Count > 0)
				sb.AppendLine(ChatbotResources.Get("Incident_ChecklistDone", culture, string.Join("; ", satisfied)));

			if (outstanding.Count > 0)
			{
				sb.AppendLine(ChatbotResources.Get("Incident_ChecklistOutstanding", culture));
				foreach (var item in outstanding)
					sb.AppendLine("- " + item);
			}

			sb.AppendLine(ChatbotResources.Get("Incident_ChecklistConfirm", culture, playbook.DisplayName));
			foreach (var item in playbook.Checklist.Take(MaxListItems))
				sb.AppendLine("- " + item);

			sb.AppendLine(ChatbotResources.Get("Incident_ChecklistDisclaimer", culture));

			return sb.ToString().TrimEnd();
		}

		public async Task<string> DescribeWeatherAsync(IncidentContext context, ChatbotSession session)
		{
			var culture = session?.Culture;

			IncidentWeather weather;
			try
			{
				weather = await _incidentCommandService.GetWeatherForIncidentAsync(session.DepartmentId, context.Call.CallId);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"Chatbot incident weather read failed for call {context.Call.CallId}.");
				return ChatbotResources.Get("Incident_WeatherUnavailable", culture, IncidentLabel(context));
			}

			if (weather?.Current == null)
				return ChatbotResources.Get("Incident_WeatherUnavailable", culture, IncidentLabel(context));

			var current = weather.Current;
			var sb = new StringBuilder();
			sb.AppendLine(ChatbotResources.Get("Incident_WeatherHeader", culture, IncidentLabel(context)));

			if (!string.IsNullOrWhiteSpace(current.Description))
				sb.AppendLine(ChatbotResources.Get("Incident_WeatherConditions", culture, current.Description));

			if (current.TemperatureCelsius.HasValue)
				sb.AppendLine(ChatbotResources.Get("Incident_WeatherTemperature", culture,
					Math.Round(current.TemperatureCelsius.Value, 0),
					Math.Round(CelsiusToFahrenheit(current.TemperatureCelsius.Value), 0)));

			if (current.WindSpeedKph.HasValue)
				sb.AppendLine(ChatbotResources.Get("Incident_WeatherWind", culture,
					CompassPoint(current.WindDirectionDegrees),
					Math.Round(current.WindSpeedKph.Value, 0),
					Math.Round(KphToMph(current.WindSpeedKph.Value), 0),
					current.WindGustKph.HasValue
						? ChatbotResources.Get("Incident_WeatherGusts", culture, Math.Round(current.WindGustKph.Value, 0), Math.Round(KphToMph(current.WindGustKph.Value), 0))
						: string.Empty));

			if (current.RelativeHumidityPercent.HasValue)
				sb.AppendLine(ChatbotResources.Get("Incident_WeatherHumidity", culture, Math.Round(current.RelativeHumidityPercent.Value, 0)));

			// Wind direction is the operational fact on HazMat and wildland incidents — spell out the
			// downwind side rather than making the commander do the math on the radio.
			if (current.WindDirectionDegrees.HasValue)
				sb.AppendLine(ChatbotResources.Get("Incident_WeatherDownwind", culture, CompassPoint((current.WindDirectionDegrees.Value + 180m) % 360m)));

			return sb.ToString().TrimEnd();
		}

		#endregion Briefing / checklist / weather

		#region Grounding snapshot

		public async Task<string> BuildGroundingSnapshotAsync(IncidentContext context, ChatbotSession session)
		{
			if (context?.Board?.Command == null)
				return string.Empty;

			var board = context.Board;
			var command = board.Command;
			var names = await GetPersonNamesAsync(session);
			var resourceNames = await BuildResourceNameLookupAsync(context, session);
			var playbook = IcsPlaybooks.Infer(context.Call, command.Name);
			var liveNodes = LiveNodes(board);
			var liveAssignments = LiveAssignments(board);
			var par = ParBuckets(board);

			// Deliberately terse, machine-ish key/value lines: the model reads this, the commander doesn't.
			var sb = new StringBuilder();
			sb.AppendLine("INCIDENT SNAPSHOT (all times UTC, generated " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture) + ")");
			sb.AppendLine($"Name: {IncidentLabel(context)}");
			sb.AppendLine($"Type: {playbook.DisplayName}");
			if (!string.IsNullOrWhiteSpace(context.Call?.Address))
				sb.AppendLine($"Address: {context.Call.Address}");
			sb.AppendLine($"Established: {command.EstablishedOn:u} (running {FormatDuration(DateTime.UtcNow - command.EstablishedOn)})");
			sb.AppendLine($"Commander: {ResolveName(names, command.CurrentCommanderUserId) ?? "unknown"}");
			if (!string.IsNullOrWhiteSpace(command.CommandPostLocationText))
				sb.AppendLine($"Command post: {command.CommandPostLocationText}");
			if (!string.IsNullOrWhiteSpace(command.StagingLocationText))
				sb.AppendLine($"Staging: {command.StagingLocationText}");
			if (!string.IsNullOrWhiteSpace(command.ImportantInformation))
				sb.AppendLine($"Important information: {command.ImportantInformation.Truncate(300)}");
			if (!string.IsNullOrWhiteSpace(command.IncidentActionPlan))
				sb.AppendLine($"Action plan: {command.IncidentActionPlan.Truncate(400)}");

			sb.AppendLine($"Accountability: {par.Total} tracked, {par.Critical} overdue, {par.Warning} approaching");

			sb.AppendLine("Lanes:");
			if (liveNodes.Count == 0)
				sb.AppendLine("- none");
			else
				foreach (var node in liveNodes.Take(MaxListItems))
				{
					var inLane = liveAssignments.Where(a => string.Equals(a.CommandStructureNodeId, node.CommandStructureNodeId, StringComparison.OrdinalIgnoreCase)).ToList();
					sb.AppendLine($"- {node.Name} ({NodeTypeName(node.NodeType)}), lead: {LaneLead(node, names) ?? "none"}, {inLane.Count} resources: {string.Join(", ", inLane.Take(8).Select(a => ResourceLabel(a, resourceNames)))}");
				}

			var unassigned = liveAssignments.Where(a => string.IsNullOrWhiteSpace(a.CommandStructureNodeId)).ToList();
			if (unassigned.Count > 0)
				sb.AppendLine($"Unassigned pool ({unassigned.Count}): {string.Join(", ", unassigned.Take(10).Select(a => ResourceLabel(a, resourceNames)))}");

			sb.AppendLine("Objectives:");
			var objectives = (board.Objectives ?? new List<TacticalObjective>()).OrderBy(o => o.SortOrder).ToList();
			if (objectives.Count == 0)
				sb.AppendLine("- none");
			else
				foreach (var objective in objectives.Take(MaxListItems))
					sb.AppendLine($"- {objective.Name}: {ObjectiveStatusName(objective.Status)} ({objective.ProgressPercent}%)");

			sb.AppendLine("Needs:");
			var needs = (board.Needs ?? new List<IncidentNeed>()).ToList();
			if (needs.Count == 0)
				sb.AppendLine("- none");
			else
				foreach (var need in needs.Take(MaxListItems))
					sb.AppendLine($"- {need.Name} [{NeedCategoryName(need.Category)}]: {NeedStatusName(need.Status)} {need.QuantityFulfilled}/{need.QuantityRequested}");

			sb.AppendLine("ICS positions filled:");
			var activeRoles = (board.Roles ?? new List<IncidentRoleAssignment>()).Where(r => !r.RemovedOn.HasValue).ToList();
			if (activeRoles.Count == 0)
				sb.AppendLine("- none");
			else
				foreach (var role in activeRoles.Take(MaxListItems))
					sb.AppendLine($"- {IncidentRoleVocabulary.DisplayName((IncidentRoleType)role.RoleType)}: {ResolveName(names, role.UserId)}");

			var notes = (board.Notes ?? new List<IncidentNote>()).Where(n => !n.DeletedOn.HasValue).OrderByDescending(n => n.CreatedOn).Take(5).ToList();
			if (notes.Count > 0)
			{
				sb.AppendLine("Recent notes:");
				foreach (var note in notes)
					sb.AppendLine($"- {note.CreatedOn:u}: {note.Body?.Truncate(160)}");
			}

			return sb.ToString().TrimEnd();
		}

		#endregion Grounding snapshot

		#region Shared helpers

		/// <summary>"Structure fire (26-1)" — how the assistant refers to the incident in prose.</summary>
		public static string IncidentLabel(IncidentContext context)
		{
			var name = context?.Command?.Name;
			if (string.IsNullOrWhiteSpace(name))
				name = context?.Call?.Name;
			if (string.IsNullOrWhiteSpace(name))
				name = "the incident";

			var number = context?.Call?.Number;
			return string.IsNullOrWhiteSpace(number) ? name : $"{name} ({number})";
		}

		private string Header(IncidentContext context)
			=> IncidentLabel(context) + (context.Call?.Address == null ? string.Empty : " — " + context.Call.Address);

		private static List<CommandStructureNode> LiveNodes(IncidentCommandBoard board)
			=> (board.Nodes ?? new List<CommandStructureNode>())
				.Where(n => !n.DeletedOn.HasValue)
				.OrderBy(n => n.SortOrder)
				.ToList();

		private static List<ResourceAssignment> LiveAssignments(IncidentCommandBoard board)
			=> (board.Assignments ?? new List<ResourceAssignment>())
				.Where(a => !a.ReleasedOn.HasValue)
				.ToList();

		private static bool IsUnitKind(int kind)
			=> kind == (int)ResourceAssignmentKind.RealUnit
				|| kind == (int)ResourceAssignmentKind.LinkedDeptUnit
				|| kind == (int)ResourceAssignmentKind.AdHocUnit;

		private static bool IsCritical(PersonnelCallCheckInStatus row)
			=> string.Equals(row.Status, "Critical", StringComparison.OrdinalIgnoreCase) || row.NeedsCheckIn;

		private static bool IsWarning(PersonnelCallCheckInStatus row)
			=> !IsCritical(row) && string.Equals(row.Status, "Warning", StringComparison.OrdinalIgnoreCase);

		private static (int Total, int Warning, int Critical) ParBuckets(IncidentCommandBoard board)
		{
			var rows = board.Accountability ?? new List<PersonnelCallCheckInStatus>();
			return (rows.Count, rows.Count(IsWarning), rows.Count(IsCritical));
		}

		private static string PersonLabel(PersonnelCallCheckInStatus row)
			=> string.IsNullOrWhiteSpace(row.FullName) ? row.UserId : row.FullName;

		private async Task<Dictionary<string, UserProfile>> GetPersonNamesAsync(ChatbotSession session)
		{
			try
			{
				return await _userProfileService.GetAllProfilesForDepartmentAsync(session.DepartmentId)
					?? new Dictionary<string, UserProfile>();
			}
			catch (Exception ex)
			{
				// Names are cosmetic — a lookup failure degrades to raw ids rather than losing the answer.
				Resgrid.Framework.Logging.LogException(ex, "Chatbot incident narrator: profile lookup failed.");
				return new Dictionary<string, UserProfile>();
			}
		}

		private static string ResolveName(Dictionary<string, UserProfile> names, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;

			return names != null && names.TryGetValue(userId, out var profile) && profile != null
				? profile.FullName.AsFirstNameLastName
				: userId;
		}

		/// <summary>
		/// Display names for everything that can be assigned to a lane, keyed "<kind>:<resourceId>".
		/// Department units and personnel come from their rosters; ad-hoc (external) resources come from
		/// the incident itself, so they only resolve when the caller asked for them to be loaded.
		/// </summary>
		private async Task<Dictionary<string, string>> BuildResourceNameLookupAsync(IncidentContext context, ChatbotSession session)
		{
			var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			var profiles = await GetPersonNamesAsync(session);
			foreach (var pair in profiles)
			{
				var name = pair.Value?.FullName.AsFirstNameLastName;
				if (string.IsNullOrWhiteSpace(name))
					continue;

				lookup[Key(ResourceAssignmentKind.RealPersonnel, pair.Key)] = name;
				lookup[Key(ResourceAssignmentKind.LinkedDeptPersonnel, pair.Key)] = name;
			}

			try
			{
				var units = await _unitsService.GetUnitsForDepartmentAsync(session.DepartmentId) ?? new List<Unit>();
				foreach (var unit in units)
				{
					var id = unit.UnitId.ToString(CultureInfo.InvariantCulture);
					lookup[Key(ResourceAssignmentKind.RealUnit, id)] = unit.Name;
					lookup[Key(ResourceAssignmentKind.LinkedDeptUnit, id)] = unit.Name;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Chatbot incident narrator: unit roster lookup failed.");
			}

			foreach (var unit in context.AdHocUnits)
				lookup[Key(ResourceAssignmentKind.AdHocUnit, unit.IncidentAdHocUnitId)] = unit.Name;

			foreach (var person in context.AdHocPersonnel)
				lookup[Key(ResourceAssignmentKind.AdHocPersonnel, person.IncidentAdHocPersonnelId)] = person.Name;

			return lookup;
		}

		private static string Key(ResourceAssignmentKind kind, string resourceId) => $"{(int)kind}:{resourceId}";

		private static string ResourceLabel(ResourceAssignment assignment, Dictionary<string, string> lookup)
		{
			var key = $"{assignment.ResourceKind}:{assignment.ResourceId}";
			return lookup.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name : assignment.ResourceId;
		}

		private static string FormatTimeInLane(ResourceAssignment assignment, string culture)
		{
			var elapsed = DateTime.UtcNow - assignment.AssignedOn;
			return elapsed < TimeSpan.FromMinutes(1)
				? string.Empty
				: " " + ChatbotResources.Get("Incident_TimeInLane", culture, FormatDuration(elapsed));
		}

		private static string LaneLead(CommandStructureNode node, Dictionary<string, UserProfile> names)
		{
			if (!string.IsNullOrWhiteSpace(node.PrimaryLeadUserId))
				return ResolveName(names, node.PrimaryLeadUserId);

			if (!string.IsNullOrWhiteSpace(node.PrimaryLeadName))
				return node.PrimaryLeadName;

			return string.IsNullOrWhiteSpace(node.SupervisorUserId) ? null : ResolveName(names, node.SupervisorUserId);
		}

		/// <summary>
		/// Matches the lane the commander named. Exact name first, then containment either way so
		/// "division a" finds "Division A" and "division" finds the only division on the board.
		/// </summary>
		private static CommandStructureNode MatchNode(List<CommandStructureNode> nodes, string laneName)
		{
			var needle = laneName.Trim();

			var exact = nodes.FirstOrDefault(n => string.Equals(n.Name, needle, StringComparison.OrdinalIgnoreCase));
			if (exact != null)
				return exact;

			var contains = nodes.Where(n => !string.IsNullOrWhiteSpace(n.Name)
				&& (n.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
					|| needle.IndexOf(n.Name, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

			if (contains.Count > 0)
				return contains[0];

			// Last resort: the commander named an ICS type with no designator ("who's in staging"). The
			// needle must be a substring OF the type word, never the other way round — "Division Z" must
			// NOT resolve to Division A just because both are Divisions. Answering about the wrong lane on
			// a fireground is worse than saying the lane isn't there.
			var byType = nodes.Where(n => NodeTypeName(n.NodeType).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

			return byType.Count == 1 ? byType[0] : null;
		}

		private static TacticalObjective FindObjective(IncidentCommandBoard board, string objectiveId)
			=> string.IsNullOrWhiteSpace(objectiveId)
				? null
				: (board.Objectives ?? new List<TacticalObjective>())
					.FirstOrDefault(o => string.Equals(o.TacticalObjectiveId, objectiveId, StringComparison.OrdinalIgnoreCase));

		private static string FormatObjective(TacticalObjective objective, string culture)
		{
			var status = ObjectiveStatusName(objective.Status);
			var overdue = objective.TargetCompleteOn.HasValue
				&& objective.Status != (int)TacticalObjectiveStatus.Complete
				&& objective.TargetCompleteOn.Value < DateTime.UtcNow;

			return ChatbotResources.Get("Incident_ObjectiveRow", culture,
				objective.Name,
				status,
				objective.ProgressPercent,
				overdue ? ChatbotResources.Get("Incident_ObjectiveOverdue", culture) : string.Empty);
		}

		/// <summary>
		/// Loose benchmark matching: a doctrine benchmark counts as tracked when the objective's name
		/// shares its distinctive words. Keeps "Primary search all clear" from being reported missing
		/// just because the commander typed "Primary all clear".
		/// </summary>
		private static bool LooseMatch(string objectiveName, string benchmark)
		{
			if (string.IsNullOrWhiteSpace(objectiveName) || string.IsNullOrWhiteSpace(benchmark))
				return false;

			var a = objectiveName.ToLowerInvariant();
			var b = benchmark.ToLowerInvariant();

			if (a.Contains(b) || b.Contains(a))
				return true;

			var benchmarkWords = b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Where(w => w.Length > 3)
				.ToList();

			if (benchmarkWords.Count == 0)
				return false;

			var hits = benchmarkWords.Count(w => a.Contains(w));
			return hits >= Math.Max(1, benchmarkWords.Count - 1);
		}

		private static void AddCheck(List<string> satisfied, List<string> outstanding, bool isSatisfied, string label)
		{
			if (isSatisfied)
				satisfied.Add(label);
			else
				outstanding.Add(label);
		}

		private static string NodeTypeName(int nodeType)
			=> Enum.IsDefined(typeof(CommandNodeType), nodeType)
				? SplitPascalCase(((CommandNodeType)nodeType).ToString())
				: "Lane";

		private static string ObjectiveStatusName(int status)
			=> Enum.IsDefined(typeof(TacticalObjectiveStatus), status)
				? SplitPascalCase(((TacticalObjectiveStatus)status).ToString())
				: "Pending";

		private static string NeedStatusName(int status)
			=> Enum.IsDefined(typeof(IncidentNeedStatus), status)
				? SplitPascalCase(((IncidentNeedStatus)status).ToString())
				: "Open";

		private static string NeedCategoryName(int category)
			=> Enum.IsDefined(typeof(IncidentNeedCategory), category)
				? SplitPascalCase(((IncidentNeedCategory)category).ToString())
				: "Other";

		private static string SplitPascalCase(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return value;

			var sb = new StringBuilder();
			for (var i = 0; i < value.Length; i++)
			{
				if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
					sb.Append(' ');
				sb.Append(value[i]);
			}

			return sb.ToString();
		}

		/// <summary>Radio-friendly duration: "1h 12m" / "23m" / "45s".</summary>
		public static string FormatDuration(TimeSpan span)
		{
			if (span < TimeSpan.Zero)
				span = span.Negate();

			if (span.TotalMinutes < 1)
				return $"{(int)span.TotalSeconds}s";

			if (span.TotalHours < 1)
				return $"{(int)span.TotalMinutes}m";

			return $"{(int)span.TotalHours}h {span.Minutes}m";
		}

		private static decimal CelsiusToFahrenheit(decimal celsius) => (celsius * 9m / 5m) + 32m;

		private static decimal KphToMph(decimal kph) => kph * 0.621371m;

		/// <summary>16-point compass label for a bearing; "unknown" when there's no bearing.</summary>
		private static string CompassPoint(decimal? degrees)
		{
			if (!degrees.HasValue)
				return "unknown";

			var points = new[] { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
			var normalized = ((degrees.Value % 360m) + 360m) % 360m;
			var index = (int)Math.Round(normalized / 22.5m) % 16;
			return points[index];
		}

		#endregion Shared helpers
	}
}
