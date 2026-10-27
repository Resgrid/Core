using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>A position's qualification requirement in the Phase D shape, keyed by CertificationType.Code (plan §9.2, R22).</summary>
	public sealed class IncidentPositionRequirement
	{
		public string Code { get; set; }
		public bool IsMandatory { get; set; } = true;
		/// <summary>Requirements sharing a group are an OR-set.</summary>
		public string AnyOfGroup { get; set; }
		public bool AllowTrainee { get; set; }
		public int GraceDays { get; set; }

		public static List<IncidentPositionRequirement> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<IncidentPositionRequirement>();
			try { return JsonConvert.DeserializeObject<List<IncidentPositionRequirement>>(json) ?? new List<IncidentPositionRequirement>(); }
			catch (JsonException) { return new List<IncidentPositionRequirement>(); }
		}
	}

	/// <summary>
	/// Plan §9.2: the standard-plus-custom position catalog, effective-dated assignments for users, participants and placeholders,
	/// the organization chart, and standing team rosters with computed readiness and one-action activation. Positions confer no
	/// authorization (§12.4); IC's IncidentRoleAssignment is never written — the board-role link is stored here.
	/// </summary>
	public class IncidentOrganizationService : IIncidentOrganizationService
	{
		private readonly IIncidentLedgerService<IncidentPositionDefinition> _positions;
		private readonly IIncidentLedgerService<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<IncidentParticipant> _participants;
		private readonly IIncidentLedgerService<DepartmentIncidentTeam> _teams;
		private readonly IIncidentLedgerService<DepartmentIncidentTeamSlot> _slots;
		private readonly IIncidentLedgerService<DepartmentIncidentTeamCandidate> _candidates;
		private readonly ICertificationService _certifications;
		private readonly IUserProfileService _profiles;
		private readonly IContactsService _contacts;
		private readonly IDepartmentsService _departments;
		private readonly IIncidentEventPublisher _events;
		private readonly IIncidentNotificationRoutingService _routing;
		private readonly IIncidentAuditService _audit;

		public IncidentOrganizationService(IIncidentLedgerService<IncidentPositionDefinition> positions, IIncidentLedgerService<IncidentPositionAssignment> assignments,
			IIncidentLedgerService<IncidentActivation> activations, IIncidentLedgerService<IncidentParticipant> participants,
			IIncidentLedgerService<DepartmentIncidentTeam> teams, IIncidentLedgerService<DepartmentIncidentTeamSlot> slots,
			IIncidentLedgerService<DepartmentIncidentTeamCandidate> candidates, ICertificationService certifications, IUserProfileService profiles,
			IContactsService contacts, IDepartmentsService departments, IIncidentEventPublisher events, IIncidentNotificationRoutingService routing,
			IIncidentAuditService audit)
		{
			_positions = positions;
			_assignments = assignments;
			_activations = activations;
			_participants = participants;
			_teams = teams;
			_slots = slots;
			_candidates = candidates;
			_certifications = certifications;
			_profiles = profiles;
			_contacts = contacts;
			_departments = departments;
			_events = events;
			_routing = routing;
			_audit = audit;
		}

		public async Task<IReadOnlyList<IncidentPositionDefinition>> GetPositionsAsync(IncidentActor actor, string activationId = null, CancellationToken cancellationToken = default)
		{
			var query = new IncidentLedgerQuery().Order("Section", false, "SortOrder").Page(0, 2000);
			if (string.IsNullOrWhiteSpace(activationId))
				query.DepartmentCatalogOnly = true;
			else
			{
				query.ActivationIds = new[] { activationId };
				query.IncludeDepartmentCatalog = true;
			}
			return await _positions.ListAsync(actor, query, cancellationToken);
		}

		public async Task<int> SeedCatalogAsync(IncidentActor actor, IncidentPositionSources catalog, CancellationToken cancellationToken = default)
		{
			var existing = new HashSet<string>((await GetPositionsAsync(actor, null, cancellationToken)).Select(p => p.Code), StringComparer.OrdinalIgnoreCase);
			var added = 0;
			var order = existing.Count;
			foreach (var position in IncidentPositionCatalog.For(catalog))
			{
				if (existing.Contains(position.Code))
					continue;
				await _positions.SaveAsync(actor, new IncidentPositionDefinition
				{
					Code = position.Code,
					Name = position.Name,
					Section = (int)position.Section,
					SupervisorCode = position.Supervisor,
					SourceCatalog = (int)catalog,
					SourceVersion = IncidentPositionCatalog.Version,
					IsStandard = true,
					IsActive = true,
					SortOrder = order++
				}, null, cancellationToken);
				existing.Add(position.Code);
				added++;
			}
			return added;
		}

		public async Task<IncidentQualificationCheck> CheckQualificationAsync(IncidentActor actor, IncidentPositionDefinition position, string userId, CancellationToken cancellationToken = default)
			=> await CheckAsync(actor, position, userId, DateTime.UtcNow, cancellationToken);

		private async Task<IncidentQualificationCheck> CheckAsync(IncidentActor actor, IncidentPositionDefinition position, string userId, DateTime atUtc, CancellationToken cancellationToken)
		{
			var settings = await _certifications.GetCertificationSettingsAsync(actor.DepartmentId);
			var check = new IncidentQualificationCheck { EnforcementMode = settings?.EnforcementMode ?? (int)CertificationEnforcementModes.Off };
			var requirements = IncidentPositionRequirement.Parse(position?.RequirementsJson);
			if (requirements.Count == 0 || check.EnforcementMode == (int)CertificationEnforcementModes.Off)
			{
				check.State = IncidentQualificationStates.NotRequired;
				return check;
			}
			if (string.IsNullOrWhiteSpace(userId))
			{
				// External people are checked against their keyed snapshot at check-in, not against department certifications.
				check.State = IncidentQualificationStates.Exception;
				check.Missing.AddRange(requirements.Where(r => r.IsMandatory).Select(r => r.Code));
				check.Blocked = false;
				return check;
			}

			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(actor.DepartmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !string.IsNullOrWhiteSpace(t.Code)).ToDictionary(t => t.DepartmentCertificationTypeId, t => t.Code);
			var held = (await _certifications.GetCertificationsByUserIdAsync(userId) ?? new List<PersonnelCertification>())
				.Where(c => c.DepartmentId == actor.DepartmentId && !c.IsDeleted && c.DepartmentCertificationTypeId.HasValue && types.ContainsKey(c.DepartmentCertificationTypeId.Value))
				.Select(c => (Code: types[c.DepartmentCertificationTypeId.Value], c.Status, c.ExpiresOn)).ToList();

			var trainee = false;
			foreach (var group in requirements.Where(r => r.IsMandatory).GroupBy(r => string.IsNullOrWhiteSpace(r.AnyOfGroup) ? "#" + r.Code : r.AnyOfGroup))
			{
				var satisfied = false;
				var expiring = false;
				var asTrainee = false;
				foreach (var requirement in group)
					foreach (var cert in held.Where(h => string.Equals(h.Code, requirement.Code, StringComparison.OrdinalIgnoreCase)))
					{
						var current = cert.ExpiresOn == null || cert.ExpiresOn.Value.AddDays(Math.Max(0, requirement.GraceDays)) >= atUtc;
						if (cert.Status == (int)PersonnelCertificationStatuses.Active && current)
						{
							satisfied = true;
							expiring = cert.ExpiresOn.HasValue && cert.ExpiresOn.Value < atUtc;
						}
						else if (cert.Status == (int)PersonnelCertificationStatuses.Trainee && requirement.AllowTrainee && current)
							asTrainee = true;
					}

				if (satisfied)
				{
					if (expiring) check.Expiring.Add(group.First().Code);
				}
				else if (asTrainee)
					trainee = true;
				else
					check.Missing.Add(string.Join("|", group.Select(r => r.Code)));
			}

			if (check.Missing.Count > 0)
			{
				check.State = IncidentQualificationStates.Exception;
				check.Blocked = check.EnforcementMode == (int)CertificationEnforcementModes.Enforce;
			}
			else
				check.State = trainee ? IncidentQualificationStates.Trainee : IncidentQualificationStates.Qualified;
			return check;
		}

		public async Task<IncidentAssignmentResult> AssignAsync(IncidentActor actor, IncidentPositionAssignment assignment, bool overrideQualification, CancellationToken cancellationToken = default)
		{
			var position = await _positions.GetAsync(actor, assignment.IncidentPositionDefinitionId, false, cancellationToken) ?? throw new IncidentRuleException("PositionNotFound");
			var activation = await _activations.GetAsync(actor, assignment.IncidentActivationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var subject = (IncidentAssignmentSubjectTypes)assignment.SubjectType;
			if (subject == IncidentAssignmentSubjectTypes.User && string.IsNullOrWhiteSpace(assignment.UserId)
				|| subject == IncidentAssignmentSubjectTypes.Participant && string.IsNullOrWhiteSpace(assignment.IncidentParticipantId)
				|| subject == IncidentAssignmentSubjectTypes.Placeholder && string.IsNullOrWhiteSpace(assignment.PlaceholderLabel))
				throw new IncidentRuleException("AssignmentSubjectRequired");
			if (subject == IncidentAssignmentSubjectTypes.User)
			{
				var member = await _departments.GetDepartmentMemberAsync(assignment.UserId, actor.DepartmentId, false);
				if (!Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(member, actor.DepartmentId))
					throw new IncidentRuleException("NotAMember");
			}
			if (subject == IncidentAssignmentSubjectTypes.Participant && await _participants.GetAsync(actor, assignment.IncidentParticipantId, false, cancellationToken) == null)
				throw new IncidentRuleException("ParticipantNotFound");

			var check = subject == IncidentAssignmentSubjectTypes.Placeholder ? new IncidentQualificationCheck { State = IncidentQualificationStates.NotRequired }
				: await CheckAsync(actor, position, subject == IncidentAssignmentSubjectTypes.User ? assignment.UserId : null, assignment.StartOn == default ? DateTime.UtcNow : assignment.StartOn, cancellationToken);
			if (check.Blocked && !(overrideQualification && actor.IsDepartmentAdmin))
				throw new IncidentRuleException("QualificationEnforced", string.Join(", ", check.Missing));

			assignment.QualificationState = (int)(check.Blocked ? IncidentQualificationStates.Waived : check.State);
			if (check.Missing.Count > 0)
				assignment.QualificationNote = AuthorityTrim("Missing: " + string.Join(", ", check.Missing), 500);
			if (assignment.StartOn == default)
				assignment.StartOn = DateTime.UtcNow;
			if (assignment.AssignmentKind == (int)IncidentAssignmentKinds.Trainee)
				assignment.IsTrainee = true;

			var conflicts = await ConflictsAsync(actor, assignment, cancellationToken);
			var isNew = string.IsNullOrWhiteSpace(assignment.IncidentPositionAssignmentId);
			var saved = await _assignments.SaveAsync(actor, assignment, isNew ? (int?)null : assignment.RowVersion, cancellationToken);
			await PublishAssignedAsync(actor, activation, position, saved, isNew ? "Assigned" : "Changed", cancellationToken);
			return new IncidentAssignmentResult { Assignment = saved, Qualification = check, Conflicts = conflicts };
		}

		public async Task<IncidentPositionAssignment> EndAssignmentAsync(IncidentActor actor, string assignmentId, DateTime? endUtc, CancellationToken cancellationToken = default)
		{
			var assignment = await _assignments.GetAsync(actor, assignmentId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var end = endUtc ?? DateTime.UtcNow;
			if (end < assignment.StartOn)
				throw new IncidentRuleException("EndBeforeStart");
			assignment.EndOn = end;
			var saved = await _assignments.SaveAsync(actor, assignment, assignment.RowVersion, cancellationToken);
			var position = await _positions.GetAsync(actor, saved.IncidentPositionDefinitionId, false, cancellationToken);
			var activation = await _activations.GetAsync(actor, saved.IncidentActivationId, false, cancellationToken);
			await PublishAssignedAsync(actor, activation, position, saved, "Ended", cancellationToken);
			return saved;
		}

		public async Task<IReadOnlyList<IncidentPositionAssignment>> GetHoldersAsync(IncidentActor actor, string activationId, DateTime atUtc, CancellationToken cancellationToken = default)
		{
			var all = await _assignments.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Order("StartOn").Page(0, 5000), cancellationToken);
			return all.Where(a => a.StartOn <= atUtc && (a.EndOn == null || a.EndOn > atUtc)).ToList();
		}

		/// <summary>Open assignments of the same person on any other activation (shown as a conflict, never silently double-booked).</summary>
		private async Task<List<IncidentPositionAssignment>> ConflictsAsync(IncidentActor actor, IncidentPositionAssignment assignment, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(assignment.UserId) && string.IsNullOrWhiteSpace(assignment.IncidentParticipantId))
				return new List<IncidentPositionAssignment>();
			var query = new IncidentLedgerQuery().WhereNull("EndOn").Page(0, 200);
			if (!string.IsNullOrWhiteSpace(assignment.UserId)) query.Where("UserId", assignment.UserId);
			else query.Where("IncidentParticipantId", assignment.IncidentParticipantId);
			return (await _assignments.ListAsync(actor, query, cancellationToken))
				.Where(a => a.IncidentPositionAssignmentId != assignment.IncidentPositionAssignmentId && a.IncidentActivationId != assignment.IncidentActivationId).ToList();
		}

		private async Task PublishAssignedAsync(IncidentActor actor, IncidentActivation activation, IncidentPositionDefinition position, IncidentPositionAssignment assignment, string action, CancellationToken cancellationToken)
		{
			var name = await SubjectNameAsync(actor, assignment, cancellationToken);
			await _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentPositionAssigned, new Dictionary<string, object>
			{
				["position.code"] = position?.Code,
				["position.name"] = position?.Name,
				["position.section"] = position == null ? null : ((IncidentSections)position.Section).ToString(),
				["assignment.subject_type"] = ((IncidentAssignmentSubjectTypes)assignment.SubjectType).ToString(),
				["assignment.subject_name"] = name,
				["assignment.is_trainee"] = assignment.IsTrainee,
				["assignment.is_acting"] = assignment.AssignmentKind == (int)IncidentAssignmentKinds.Acting,
				["assignment.action"] = action,
				["assignment.effective_on"] = action == "Ended" ? assignment.EndOn : assignment.StartOn,
				["assignment.qualification_state"] = ((IncidentQualificationStates)assignment.QualificationState).ToString()
			}, assignment.TableName, assignment.IncidentPositionAssignmentId, cancellationToken);

			// Notice 35 to the holder (a Resgrid user); exercises reach Resgrid users only, prefixed (plan §13.8).
			if (!string.IsNullOrWhiteSpace(assignment.UserId) && position != null)
				await _routing.NotifyUserAsync(actor, activation, EventTypes.IncidentPositionNotification, assignment.UserId,
					culture => IncidentNoticeText.Format(culture, "Notice.PositionAssigned." + action, position.Name, activation.Name, activation.ShortCode), cancellationToken);
		}

		private async Task<string> SubjectNameAsync(IncidentActor actor, IncidentPositionAssignment assignment, CancellationToken cancellationToken)
		{
			switch ((IncidentAssignmentSubjectTypes)assignment.SubjectType)
			{
				case IncidentAssignmentSubjectTypes.User:
					var profile = await _profiles.GetProfileByUserIdAsync(assignment.UserId, false);
					return profile?.FullName?.AsFirstNameLastName;
				case IncidentAssignmentSubjectTypes.Participant:
					return (await _participants.GetAsync(actor, assignment.IncidentParticipantId, false, cancellationToken))?.DisplayName;
				default:
					return assignment.PlaceholderLabel;
			}
		}

		public async Task<IncidentTeamReadinessResult> GetTeamReadinessAsync(IncidentActor actor, string teamId, DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken = default)
		{
			var team = await _teams.GetAsync(actor, teamId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var result = new IncidentTeamReadinessResult { Team = team, WindowStart = windowStartUtc, WindowEnd = windowEndUtc };
			var positions = (await GetPositionsAsync(actor, null, cancellationToken)).ToDictionary(p => p.IncidentPositionDefinitionId);
			var slots = await _slots.ListAsync(actor, new IncidentLedgerQuery().Where("DepartmentIncidentTeamId", teamId).Order("SlotOrder").Page(0, 500), cancellationToken);
			var candidates = await _candidates.ListAsync(actor, new IncidentLedgerQuery().Where("DepartmentIncidentTeamId", teamId).Order("Priority").Page(0, 2000), cancellationToken);

			foreach (var slot in slots)
			{
				positions.TryGetValue(slot.IncidentPositionDefinitionId, out var position);
				var row = new IncidentSlotReadinessRow { Slot = slot, Position = position, Readiness = IncidentSlotReadiness.NoCandidate };
				foreach (var candidate in candidates.Where(c => c.DepartmentIncidentTeamSlotId == slot.DepartmentIncidentTeamSlotId).OrderBy(c => c.CandidateKind).ThenBy(c => c.Priority))
				{
					var (state, missing) = await CandidateReadinessAsync(actor, position, candidate, windowStartUtc, windowEndUtc, cancellationToken);
					if (row.CandidateId == null || state < row.Readiness)
					{
						row.Readiness = state;
						row.CandidateId = candidate.DepartmentIncidentTeamCandidateId;
						row.CandidateName = await CandidateNameAsync(candidate);
						row.Missing = missing;
					}
					if (state == IncidentSlotReadiness.Qualified)
						break;
				}
				result.Slots.Add(row);
			}

			result.Readiness = result.Slots.Any(s => s.Readiness is IncidentSlotReadiness.MissingRequirement or IncidentSlotReadiness.NoCandidate) ? IncidentTeamReadiness.NotDeployable
				: result.Slots.Any(s => s.Readiness != IncidentSlotReadiness.Qualified) ? IncidentTeamReadiness.Degraded : IncidentTeamReadiness.Ready;
			return result;
		}

		private async Task<(IncidentSlotReadiness, List<string>)> CandidateReadinessAsync(IncidentActor actor, IncidentPositionDefinition position, DepartmentIncidentTeamCandidate candidate,
			DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(candidate.UserId))
			{
				var member = await _departments.GetDepartmentMemberAsync(candidate.UserId, actor.DepartmentId, false);
				if (!Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(member, actor.DepartmentId))
					return (IncidentSlotReadiness.NoCandidate, new List<string>());
				var now = await CheckAsync(actor, position, candidate.UserId, windowStartUtc, cancellationToken);
				if (now.State == IncidentQualificationStates.Exception)
					return (IncidentSlotReadiness.MissingRequirement, now.Missing);
				var atEnd = await CheckAsync(actor, position, candidate.UserId, windowEndUtc, cancellationToken);
				if (atEnd.State == IncidentQualificationStates.Exception)
					return (IncidentSlotReadiness.ExpiringBeforeWindowEnd, atEnd.Missing);
				return (now.State == IncidentQualificationStates.Trainee || candidate.CandidateKind == (int)IncidentTeamCandidateKinds.Trainee
					? IncidentSlotReadiness.QualifiedAsTrainee : IncidentSlotReadiness.Qualified, new List<string>());
			}

			// An external person kept as a Contact: verified at check-in against a keyed snapshot.
			var requirements = IncidentPositionRequirement.Parse(position?.RequirementsJson);
			return (requirements.Count == 0 ? IncidentSlotReadiness.Qualified : IncidentSlotReadiness.MissingRequirement, requirements.Select(r => r.Code).ToList());
		}

		private async Task<string> CandidateNameAsync(DepartmentIncidentTeamCandidate candidate)
		{
			if (!string.IsNullOrWhiteSpace(candidate.UserId))
				return (await _profiles.GetProfileByUserIdAsync(candidate.UserId, false))?.FullName?.AsFirstNameLastName;
			if (!string.IsNullOrWhiteSpace(candidate.ContactId))
				return (await _contacts.GetContactByIdAsync(candidate.ContactId))?.GetName();
			return null;
		}

		public async Task<IReadOnlyList<IncidentTeamActivationProposal>> ProposeTeamActivationAsync(IncidentActor actor, string teamId, string activationId, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var readiness = await GetTeamReadinessAsync(actor, teamId, now, now.AddDays(14), cancellationToken);
			var candidates = await _candidates.ListAsync(actor, new IncidentLedgerQuery().Where("DepartmentIncidentTeamId", teamId).Order("Priority").Page(0, 2000), cancellationToken);
			var openElsewhere = await _assignments.ListAsync(actor, new IncidentLedgerQuery().WhereNull("EndOn").WhereNotNull("UserId").Page(0, 5000), cancellationToken);
			var activations = (await _activations.ListAsync(actor, new IncidentLedgerQuery().Page(0, 1000), cancellationToken)).ToDictionary(a => a.IncidentActivationId);
			var proposals = new List<IncidentTeamActivationProposal>();
			foreach (var slot in readiness.Slots)
			{
				var proposal = new IncidentTeamActivationProposal { Slot = slot.Slot, Position = slot.Position, Readiness = slot.Readiness };
				// The first available qualified candidate in priority order; a person already working another incident is a conflict.
				foreach (var candidate in candidates.Where(c => c.DepartmentIncidentTeamSlotId == slot.Slot.DepartmentIncidentTeamSlotId).OrderBy(c => c.CandidateKind).ThenBy(c => c.Priority))
				{
					var busy = candidate.UserId == null ? null : openElsewhere.FirstOrDefault(a => string.Equals(a.UserId, candidate.UserId, StringComparison.OrdinalIgnoreCase) && a.IncidentActivationId != activationId);
					var (state, _) = await CandidateReadinessAsync(actor, slot.Position, candidate, now, now.AddDays(14), cancellationToken);
					if (state is IncidentSlotReadiness.MissingRequirement or IncidentSlotReadiness.NoCandidate)
						continue;
					if (busy != null)
					{
						proposal.ConflictActivationName ??= activations.TryGetValue(busy.IncidentActivationId, out var other) ? other.Name : busy.IncidentActivationId;
						continue;
					}
					proposal.Candidate = candidate;
					proposal.CandidateName = await CandidateNameAsync(candidate);
					proposal.Readiness = state;
					break;
				}
				proposals.Add(proposal);
			}
			return proposals;
		}

		public async Task<IncidentTeamActivationResult> ActivateTeamAsync(IncidentActor actor, string teamId, string activationId, string periodId, IReadOnlyList<IncidentTeamActivationChoice> choices, CancellationToken cancellationToken = default)
		{
			var team = await _teams.GetAsync(actor, teamId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var slots = (await _slots.ListAsync(actor, new IncidentLedgerQuery().Where("DepartmentIncidentTeamId", teamId).Page(0, 500), cancellationToken)).ToDictionary(s => s.DepartmentIncidentTeamSlotId);
			var candidates = (await _candidates.ListAsync(actor, new IncidentLedgerQuery().Where("DepartmentIncidentTeamId", teamId).Page(0, 2000), cancellationToken)).ToDictionary(c => c.DepartmentIncidentTeamCandidateId);
			var result = new IncidentTeamActivationResult();
			var batch = Guid.NewGuid().ToString("D");
			var start = DateTime.UtcNow;

			foreach (var choice in choices ?? Array.Empty<IncidentTeamActivationChoice>())
			{
				if (!slots.TryGetValue(choice.SlotId ?? string.Empty, out var slot))
					continue;
				if (string.IsNullOrWhiteSpace(choice.CandidateId) || !candidates.TryGetValue(choice.CandidateId, out var candidate) || candidate.DepartmentIncidentTeamSlotId != slot.DepartmentIncidentTeamSlotId)
				{
					result.Gaps++;
					continue;
				}

				var assignment = new IncidentPositionAssignment
				{
					IncidentActivationId = activationId,
					IncidentOperationalPeriodId = string.IsNullOrWhiteSpace(periodId) ? null : periodId,
					IncidentPositionDefinitionId = slot.IncidentPositionDefinitionId,
					AssignmentKind = (int)(candidate.CandidateKind == (int)IncidentTeamCandidateKinds.Trainee ? IncidentAssignmentKinds.Trainee : IncidentAssignmentKinds.Primary),
					Shift = (int)IncidentShiftPatterns.Either,
					StartOn = start,
					TeamActivationBatchId = batch
				};
				if (!string.IsNullOrWhiteSpace(candidate.UserId))
				{
					assignment.SubjectType = (int)IncidentAssignmentSubjectTypes.User;
					assignment.UserId = candidate.UserId;
				}
				else
				{
					// An external Contact becomes an incident participant first, then holds the position.
					var contact = await _contacts.GetContactByIdAsync(candidate.ContactId);
					var participant = await _participants.SaveAsync(actor, new IncidentParticipant
					{
						IncidentActivationId = activationId,
						ContactId = candidate.ContactId,
						DisplayName = contact?.GetName() ?? slot.Label ?? "External",
						PaymentClass = (int)IncidentPaymentClasses.Cooperator,
						SupportStatus = (int)IncidentSupportStatuses.Ordered,
						EnrollmentSource = (int)IncidentEnrollmentSources.TeamActivation,
						CheckInCode = IncidentCodes.NewCheckInCode()
					}, null, cancellationToken);
					assignment.SubjectType = (int)IncidentAssignmentSubjectTypes.Participant;
					assignment.IncidentParticipantId = participant.IncidentParticipantId;
				}

				var assigned = await AssignAsync(actor, assignment, false, cancellationToken);
				if (assigned.Conflicts.Count > 0)
					result.Conflicts.Add(assigned.Conflicts[0].IncidentActivationId);
				result.Assigned++;
			}

			_audit.Record(actor, AuditLogTypes.IncidentTeamActivated, team.TableName, team.DepartmentIncidentTeamId, null, new { activationId, result.Assigned, result.Gaps });
			await _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentActivationChanged, new Dictionary<string, object>
			{
				["activation.id"] = activation.IncidentActivationId,
				["activation.name"] = activation.Name,
				["activation.short_code"] = activation.ShortCode,
				["activation.kind"] = ((IncidentActivationKinds)activation.Kind).ToString(),
				["activation.is_exercise"] = activation.IsExercise,
				["action"] = "TeamActivated",
				["team.name"] = team.Name,
				["assignments.count"] = result.Assigned
			}, team.TableName, team.DepartmentIncidentTeamId, cancellationToken);
			return result;
		}

		private static string AuthorityTrim(string value, int length) => IncidentAuthorityService.Trim(value, length);
	}

	/// <summary>Opaque short codes: participant check-in QR codes and locally issued numbers.</summary>
	public static class IncidentCodes
	{
		private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

		public static string NewCheckInCode(int length = 10)
		{
			var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
			return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
		}
	}

	/// <summary>Notice copy in the recipient's language through the module resx (plan §13.8), English fallback.</summary>
	public static class IncidentNoticeText
	{
		private static readonly System.Resources.ResourceManager Strings =
			new System.Resources.ResourceManager("Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice", typeof(Resgrid.Localization.SupportedLocales).Assembly);
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		public static string Text(string key, CultureInfo culture) =>
			Strings.GetString(key, culture ?? English) ?? Strings.GetString(key, English) ?? key;

		public static string Format(CultureInfo culture, string key, params object[] args) =>
			string.Format(culture ?? English, Text(key, culture), args);
	}

	/// <summary>The standard position catalogs (plan §9.2): NIMS core all-hazards, NWCG wildland and the CICCS set, versioned.</summary>
	public static class IncidentPositionCatalog
	{
		public const string Version = "2026.1";

		public sealed class Entry
		{
			public string Code { get; set; }
			public string Name { get; set; }
			public IncidentSections Section { get; set; }
			public string Supervisor { get; set; }
			public bool Nims { get; set; } = true;
			public bool Wildland { get; set; }
		}

		private static Entry E(string code, string name, IncidentSections section, string supervisor, bool nims = true, bool wildland = true)
			=> new Entry { Code = code, Name = name, Section = section, Supervisor = supervisor, Nims = nims, Wildland = wildland };

		public static readonly IReadOnlyList<Entry> All = new[]
		{
			E("IC", "Incident Commander", IncidentSections.Command, null),
			E("DPIC", "Deputy Incident Commander", IncidentSections.Command, "IC"),
			E("SOFR", "Safety Officer", IncidentSections.Command, "IC"),
			E("PIO", "Public Information Officer", IncidentSections.Command, "IC"),
			E("LOFR", "Liaison Officer", IncidentSections.Command, "IC"),
			E("OSC", "Operations Section Chief", IncidentSections.Operations, "IC"),
			E("OPBD", "Operations Branch Director", IncidentSections.Operations, "OSC"),
			E("DIVS", "Division/Group Supervisor", IncidentSections.Operations, "OPBD"),
			E("STAM", "Staging Area Manager", IncidentSections.Operations, "OSC"),
			E("TFLD", "Task Force Leader", IncidentSections.Operations, "DIVS"),
			E("STL", "Strike Team Leader", IncidentSections.Operations, "DIVS"),
			E("AOBD", "Air Operations Branch Director", IncidentSections.Operations, "OSC"),
			E("ASGS", "Air Support Group Supervisor", IncidentSections.Operations, "AOBD"),
			E("HEBM", "Helibase Manager", IncidentSections.Operations, "ASGS"),
			E("PSC", "Planning Section Chief", IncidentSections.Planning, "IC"),
			E("RESL", "Resources Unit Leader", IncidentSections.Planning, "PSC"),
			E("SCKN", "Status/Check-In Recorder", IncidentSections.Planning, "RESL"),
			E("SITL", "Situation Unit Leader", IncidentSections.Planning, "PSC"),
			E("GISS", "GIS Specialist", IncidentSections.Planning, "SITL"),
			E("FBAN", "Fire Behavior Analyst", IncidentSections.Planning, "SITL", nims: false),
			E("DOCL", "Documentation Unit Leader", IncidentSections.Planning, "PSC"),
			E("DMOB", "Demobilization Unit Leader", IncidentSections.Planning, "PSC"),
			E("TNSP", "Training Specialist", IncidentSections.Planning, "PSC"),
			E("LSC", "Logistics Section Chief", IncidentSections.Logistics, "IC"),
			E("SVBD", "Service Branch Director", IncidentSections.Logistics, "LSC"),
			E("SPBD", "Support Branch Director", IncidentSections.Logistics, "LSC"),
			E("COML", "Communications Unit Leader", IncidentSections.Logistics, "SVBD"),
			E("COMT", "Communications Technician", IncidentSections.Logistics, "COML"),
			E("RADO", "Radio Operator", IncidentSections.Logistics, "COML"),
			E("MEDL", "Medical Unit Leader", IncidentSections.Logistics, "SVBD"),
			E("FDUL", "Food Unit Leader", IncidentSections.Logistics, "SVBD"),
			E("SPUL", "Supply Unit Leader", IncidentSections.Logistics, "SPBD"),
			E("ORDM", "Ordering Manager", IncidentSections.Logistics, "SPUL"),
			E("RCDM", "Receiving/Distribution Manager", IncidentSections.Logistics, "SPUL"),
			E("FACL", "Facilities Unit Leader", IncidentSections.Logistics, "SPBD"),
			E("BCMG", "Base/Camp Manager", IncidentSections.Logistics, "FACL"),
			E("SECM", "Security Manager", IncidentSections.Logistics, "FACL"),
			E("GSUL", "Ground Support Unit Leader", IncidentSections.Logistics, "SPBD"),
			E("EQPM", "Equipment Manager", IncidentSections.Logistics, "GSUL"),
			E("FSC", "Finance/Administration Section Chief", IncidentSections.FinanceAdministration, "IC"),
			E("TIME", "Time Unit Leader", IncidentSections.FinanceAdministration, "FSC"),
			E("PTRC", "Personnel Time Recorder", IncidentSections.FinanceAdministration, "TIME"),
			E("EQTR", "Equipment Time Recorder", IncidentSections.FinanceAdministration, "TIME"),
			E("PROC", "Procurement Unit Leader", IncidentSections.FinanceAdministration, "FSC"),
			E("BUYM", "Buying Team Member", IncidentSections.FinanceAdministration, "PROC"),
			E("COMP", "Compensation/Claims Unit Leader", IncidentSections.FinanceAdministration, "FSC"),
			E("COST", "Cost Unit Leader", IncidentSections.FinanceAdministration, "FSC"),
			E("EOCD", "EOC Director", IncidentSections.Coordination, null, nims: true, wildland: false)
		};

		public static IEnumerable<Entry> For(IncidentPositionSources catalog) => catalog switch
		{
			IncidentPositionSources.Nwcg or IncidentPositionSources.Ciccs => All.Where(e => e.Wildland),
			_ => All.Where(e => e.Nims)
		};

		/// <summary>The chief position of each section — the notice fallback when a position is unfilled (plan §13.8).</summary>
		public static string SectionChief(IncidentSections section) => section switch
		{
			IncidentSections.Operations => "OSC",
			IncidentSections.Planning => "PSC",
			IncidentSections.Logistics => "LSC",
			IncidentSections.FinanceAdministration => "FSC",
			IncidentSections.Coordination => "EOCD",
			_ => "IC"
		};
	}
}
