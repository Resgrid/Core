using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Plan §9.2 participants, §9.3 qualification snapshots and §9.5 enrollment and desk check-in. The enrollment queue reads
	/// dispatch and IC data (Call dispatches, the board's assignments and ad-hoc entries) read-only and writes only back-office rows.
	/// </summary>
	public class IncidentParticipantsService : IIncidentParticipantsService
	{
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<IncidentParticipant> _participants;
		private readonly IIncidentLedgerService<IncidentParticipantQualification> _qualifications;
		private readonly IIncidentLedgerService<IncidentResource> _resources;
		private readonly IIncidentLedgerService<IncidentCheckIn> _checkIns;
		private readonly IIncidentEventPublisher _events;
		private readonly ICallsService _calls;
		private readonly IIncidentCommandService _incidentCommand;
		private readonly IIncidentResourcesService _incidentResources;
		private readonly IUserProfileService _profiles;
		private readonly IUnitsService _units;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly ICertificationService _certifications;

		public IncidentParticipantsService(IIncidentLedgerService<IncidentActivation> activations, IIncidentLedgerService<IncidentParticipant> participants,
			IIncidentLedgerService<IncidentParticipantQualification> qualifications, IIncidentLedgerService<IncidentResource> resources,
			IIncidentLedgerService<IncidentCheckIn> checkIns, IIncidentEventPublisher events,
			ICallsService calls, IIncidentCommandService incidentCommand, IIncidentResourcesService incidentResources, IUserProfileService profiles,
			IUnitsService units, IDepartmentsService departments, IDepartmentGroupsService groups, ICertificationService certifications)
		{
			_activations = activations;
			_participants = participants;
			_qualifications = qualifications;
			_resources = resources;
			_checkIns = checkIns;
			_events = events;
			_calls = calls;
			_incidentCommand = incidentCommand;
			_incidentResources = incidentResources;
			_profiles = profiles;
			_units = units;
			_departments = departments;
			_groups = groups;
			_certifications = certifications;
		}

		public async Task<IReadOnlyList<IncidentEnrollmentCandidate>> GetEnrollmentQueueAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var callIds = new List<int>();
			if (activation.CallId.HasValue) callIds.Add(activation.CallId.Value);
			if (activation.Kind == (int)IncidentActivationKinds.Complex)
				callIds.AddRange((await _activations.ListAsync(actor, new IncidentLedgerQuery().Where("ParentActivationId", activationId), cancellationToken)).Where(a => a.CallId.HasValue).Select(a => a.CallId.Value));
			if (callIds.Count == 0)
				return Array.Empty<IncidentEnrollmentCandidate>();

			var participants = await _participants.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Page(0, 5000), cancellationToken);
			var resources = await _resources.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Page(0, 5000), cancellationToken);
			var enrolledUsers = new HashSet<string>(participants.Where(p => p.UserId != null).Select(p => p.UserId), StringComparer.OrdinalIgnoreCase);
			var enrolledAdHocPeople = new HashSet<string>(participants.Where(p => p.IncidentAdHocPersonnelId != null).Select(p => p.IncidentAdHocPersonnelId), StringComparer.OrdinalIgnoreCase);
			var enrolledUnits = new HashSet<int>(resources.Where(r => r.UnitId.HasValue).Select(r => r.UnitId.Value));
			var enrolledAdHocUnits = new HashSet<string>(resources.Where(r => r.IncidentAdHocUnitId != null).Select(r => r.IncidentAdHocUnitId), StringComparer.OrdinalIgnoreCase);

			var profiles = await _profiles.GetAllProfilesForDepartmentAsync(actor.DepartmentId);
			var units = (await _units.GetUnitsForDepartmentAsync(actor.DepartmentId) ?? new List<Unit>()).ToDictionary(u => u.UnitId);
			var queue = new Dictionary<string, IncidentEnrollmentCandidate>(StringComparer.OrdinalIgnoreCase);

			void Person(string userId, string source, DateTime? since)
			{
				if (string.IsNullOrWhiteSpace(userId) || enrolledUsers.Contains(userId)) return;
				var key = "user:" + userId;
				if (queue.ContainsKey(key)) return;
				profiles.TryGetValue(userId, out var profile);
				queue[key] = new IncidentEnrollmentCandidate { Key = key, Name = profile?.FullName?.AsFirstNameLastName ?? userId, Kind = "Person", Source = source, SeenSince = since };
			}

			void UnitRow(int unitId, string source, DateTime? since)
			{
				if (enrolledUnits.Contains(unitId) || !units.TryGetValue(unitId, out var unit)) return;
				var key = "unit:" + unitId.ToString(CultureInfo.InvariantCulture);
				if (!queue.ContainsKey(key))
					queue[key] = new IncidentEnrollmentCandidate { Key = key, Name = unit.Name, Kind = "Unit", Source = source, SeenSince = since };
			}

			foreach (var callId in callIds.Distinct())
			{
				var call = await _calls.GetCallByIdAsync(callId, false);
				if (call == null || call.DepartmentId != actor.DepartmentId)
					continue;
				call = await _calls.PopulateCallData(call, true, false, false, false, true, false, false, false, false);
				foreach (var dispatch in call.Dispatches ?? new List<CallDispatch>())
					Person(dispatch.UserId, "Dispatch", dispatch.DispatchedOn);
				foreach (var dispatch in call.UnitDispatches ?? new List<CallDispatchUnit>())
					UnitRow(dispatch.UnitId, "Dispatch", dispatch.DispatchedOn);

				foreach (var assignment in await _incidentCommand.GetAssignmentsForCallAsync(actor.DepartmentId, callId) ?? new List<ResourceAssignment>())
				{
					if (assignment.ReleasedOn.HasValue) continue;
					if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel)
						Person(assignment.ResourceId, "Board", assignment.AssignedOn);
					else if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit && int.TryParse(assignment.ResourceId, out var unitId))
						UnitRow(unitId, "Board", assignment.AssignedOn);
				}

				foreach (var person in await _incidentResources.GetAdHocPersonnelForCallAsync(actor.DepartmentId, callId) ?? new List<IncidentAdHocPersonnel>())
					if (!enrolledAdHocPeople.Contains(person.IncidentAdHocPersonnelId))
						queue["adhoc-person:" + person.IncidentAdHocPersonnelId] = new IncidentEnrollmentCandidate { Key = "adhoc-person:" + person.IncidentAdHocPersonnelId, Name = person.Name, Kind = "AdHocPerson", Source = person.ExternalAgencyName ?? "IC", SeenSince = person.CreatedOn };

				foreach (var unit in await _incidentResources.GetAdHocUnitsForCallAsync(actor.DepartmentId, callId) ?? new List<IncidentAdHocUnit>())
					if (!enrolledAdHocUnits.Contains(unit.IncidentAdHocUnitId))
						queue["adhoc-unit:" + unit.IncidentAdHocUnitId] = new IncidentEnrollmentCandidate { Key = "adhoc-unit:" + unit.IncidentAdHocUnitId, Name = unit.Name, Kind = "AdHocUnit", Source = unit.ExternalAgencyName ?? "IC", SeenSince = unit.CreatedOn };
			}

			return queue.Values.OrderBy(c => c.SeenSince ?? DateTime.MaxValue).ThenBy(c => c.Name).ToList();
		}

		public async Task<IncidentEnrollmentResult> EnrollAsync(IncidentActor actor, string activationId, IReadOnlyList<string> candidateKeys, bool asDraft, CancellationToken cancellationToken = default)
		{
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var queue = (await GetEnrollmentQueueAsync(actor, activationId, cancellationToken)).ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId);
			var result = new IncidentEnrollmentResult();
			var adHocPeople = activation.CallId.HasValue ? (await _incidentResources.GetAdHocPersonnelForCallAsync(actor.DepartmentId, activation.CallId.Value)).ToDictionary(p => p.IncidentAdHocPersonnelId, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, IncidentAdHocPersonnel>();
			var adHocUnits = activation.CallId.HasValue ? (await _incidentResources.GetAdHocUnitsForCallAsync(actor.DepartmentId, activation.CallId.Value)).ToDictionary(u => u.IncidentAdHocUnitId, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, IncidentAdHocUnit>();

			foreach (var key in (candidateKeys ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (!queue.TryGetValue(key, out var candidate))
				{
					result.Skipped++;
					continue;
				}

				var parts = key.Split(':', 2);
				switch (parts[0])
				{
					case "user":
						var group = await _groups.GetGroupForUserAsync(parts[1], actor.DepartmentId);
						var participant = await _participants.SaveAsync(actor, new IncidentParticipant
						{
							IncidentActivationId = activationId,
							UserId = parts[1],
							DisplayName = candidate.Name,
							Agency = department?.Name,
							HomeUnit = group?.Name,
							PaymentClass = (int)IncidentPaymentClasses.Employee,
							SupportStatus = (int)IncidentSupportStatuses.Arrived,
							EnrollmentSource = (int)(asDraft ? IncidentEnrollmentSources.AutoDraft : IncidentEnrollmentSources.EnrollmentQueue),
							IsDraft = asDraft,
							ArrivedOn = candidate.SeenSince,
							NeedsMeals = true,
							CheckInCode = IncidentCodes.NewCheckInCode()
						}, null, cancellationToken);
						await SnapshotQualificationsAsync(actor, participant.IncidentParticipantId, cancellationToken);
						result.Participants++;
						break;
					case "adhoc-person":
						adHocPeople.TryGetValue(parts[1], out var person);
						await _participants.SaveAsync(actor, new IncidentParticipant
						{
							IncidentActivationId = activationId,
							IncidentAdHocPersonnelId = parts[1],
							DisplayName = candidate.Name,
							Agency = person?.ExternalAgencyName,
							PositionTitle = person?.Role,
							PaymentClass = (int)IncidentPaymentClasses.MutualAid,
							SupportStatus = (int)IncidentSupportStatuses.Arrived,
							EnrollmentSource = (int)IncidentEnrollmentSources.AdHocEnrichment,
							IsDraft = asDraft,
							ArrivedOn = candidate.SeenSince,
							NeedsMeals = true,
							CheckInCode = IncidentCodes.NewCheckInCode()
						}, null, cancellationToken);
						result.Participants++;
						break;
					case "unit":
						await _resources.SaveAsync(actor, new IncidentResource
						{
							IncidentActivationId = activationId,
							UnitId = int.Parse(parts[1], CultureInfo.InvariantCulture),
							Kind = (int)IncidentResourceKinds.Equipment,
							Name = candidate.Name,
							ProviderAgency = department?.Name,
							ApparatusCount = 1,
							PaymentClass = (int)IncidentPaymentClasses.Employee,
							SupportStatus = (int)IncidentSupportStatuses.Arrived,
							ArrivedOn = candidate.SeenSince
						}, null, cancellationToken);
						result.Resources++;
						break;
					case "adhoc-unit":
						adHocUnits.TryGetValue(parts[1], out var adHocUnit);
						await _resources.SaveAsync(actor, new IncidentResource
						{
							IncidentActivationId = activationId,
							IncidentAdHocUnitId = parts[1],
							Kind = (int)IncidentResourceKinds.Equipment,
							Name = candidate.Name,
							ResourceType = adHocUnit?.Type,
							ProviderAgency = adHocUnit?.ExternalAgencyName,
							ApparatusCount = 1,
							PaymentClass = (int)IncidentPaymentClasses.MutualAid,
							SupportStatus = (int)IncidentSupportStatuses.Arrived,
							ArrivedOn = candidate.SeenSince
						}, null, cancellationToken);
						result.Resources++;
						break;
					default:
						result.Skipped++;
						break;
				}
			}
			return result;
		}

		public async Task<int> SnapshotQualificationsAsync(IncidentActor actor, string participantId, CancellationToken cancellationToken = default)
		{
			var participant = await _participants.GetAsync(actor, participantId, false, cancellationToken) ?? throw new IncidentRuleException("ParticipantNotFound");
			if (string.IsNullOrWhiteSpace(participant.UserId))
				return 0;
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(actor.DepartmentId) ?? new List<DepartmentCertificationType>()).ToDictionary(t => t.DepartmentCertificationTypeId);
			var count = 0;
			var now = DateTime.UtcNow;
			foreach (var cert in (await _certifications.GetCertificationsByUserIdAsync(participant.UserId) ?? new List<PersonnelCertification>()).Where(c => c.DepartmentId == actor.DepartmentId && !c.IsDeleted))
			{
				types.TryGetValue(cert.DepartmentCertificationTypeId ?? 0, out var type);
				var state = cert.Status == (int)PersonnelCertificationStatuses.Active && (cert.ExpiresOn == null || cert.ExpiresOn > now) ? IncidentVerificationStates.Verified
					: cert.ExpiresOn.HasValue && cert.ExpiresOn <= now ? IncidentVerificationStates.Expired : IncidentVerificationStates.Unverified;
				await _qualifications.SaveAsync(actor, new IncidentParticipantQualification
				{
					IncidentActivationId = participant.IncidentActivationId,
					IncidentParticipantId = participantId,
					Code = type?.Code ?? cert.Type ?? cert.Name,
					Name = cert.Name,
					Source = (int)IncidentQualificationSources.PersonnelCertification,
					SourceName = cert.IssuedBy,
					VerificationState = (int)state,
					EffectiveOn = cert.RecievedOn,
					ExpiresOn = cert.ExpiresOn,
					SnapshotOn = now,
					CredentialNumber = cert.Number
				}, null, cancellationToken);
				count++;
			}
			return count;
		}

		public async Task<IncidentParticipant> CheckInAsync(IncidentActor actor, string participantId, IncidentCheckInSources source, CancellationToken cancellationToken = default)
		{
			var participant = await _participants.GetAsync(actor, participantId, false, cancellationToken) ?? throw new IncidentRuleException("ParticipantNotFound");
			var activation = await _activations.GetAsync(actor, participant.IncidentActivationId, false, cancellationToken);
			var now = DateTime.UtcNow;
			var from = participant.SupportStatus;
			// The transition's IncidentParticipantStatusRule writes the support-status history row.
			if (from < (int)IncidentSupportStatuses.Arrived)
				participant = await _participants.TransitionAsync(actor, participantId, (int)IncidentSupportStatuses.Arrived, "CheckIn", null, cancellationToken);
			participant.CheckedInOn ??= now;
			participant.ArrivedOn ??= now;
			participant.IsDraft = false;
			participant.AssignmentStartOn ??= now.Date;
			participant = await _participants.SaveAsync(actor, participant, participant.RowVersion, cancellationToken);

			await _checkIns.SaveAsync(actor, new IncidentCheckIn
			{
				IncidentActivationId = participant.IncidentActivationId,
				SubjectType = (int)IncidentSubjectTypes.Participant,
				SubjectId = participantId,
				Source = (int)source,
				CheckedInOn = now
			}, null, cancellationToken);

			await _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentParticipantCheckedIn, new Dictionary<string, object>
			{
				["participant.name"] = participant.DisplayName,
				["participant.agency"] = participant.Agency,
				["participant.kind"] = "Person",
				["participant.request_number"] = participant.RequestNumber,
				["checkin.source"] = source.ToString(),
				["checkin.on"] = now
			}, participant.TableName, participant.IncidentParticipantId, cancellationToken);
			return participant;
		}

		public async Task<IncidentParticipant> FindByCheckInCodeAsync(IncidentActor actor, string code, CancellationToken cancellationToken = default)
		{
			var value = (code ?? string.Empty).Trim().ToUpperInvariant();
			if (value.Length < 6 || value.Length > 32)
				return null;
			return (await _participants.ListAsync(actor, new IncidentLedgerQuery().Where("CheckInCode", value).Page(0, 1), cancellationToken)).FirstOrDefault();
		}

		private static readonly string[] RosterColumns = { "DisplayName", "Agency", "HomeUnit", "PositionTitle", "OrderNumber", "RequestNumber", "PaymentClass", "PersonalPhone", "PersonalEmail", "ExpectedArrival" };

		public async Task<IncidentImportStage> StageRosterImportAsync(IncidentActor actor, string activationId, string csv, CancellationToken cancellationToken = default)
		{
			if (await _activations.GetAsync(actor, activationId, false, cancellationToken) == null)
				throw new IncidentRuleException("ActivationNotFound");
			var stage = new IncidentImportStage();
			var existing = (await _participants.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Page(0, 10000), cancellationToken))
				.Select(p => Key(p.Agency, p.OrderNumber, p.DisplayName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

			var records = IncidentCsv.Parse(csv);
			if (records.Count == 0)
				return stage;
			var header = records[0];
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (var r = 1; r < records.Count && stage.Rows.Count < 10000; r++)
			{
				var fields = records[r];
				if (fields.All(string.IsNullOrWhiteSpace))
					continue;
				var row = new IncidentImportRow { Line = r + 1 };
				foreach (var column in RosterColumns)
				{
					var index = Array.FindIndex(header, h => string.Equals(h?.Replace(" ", string.Empty), column, StringComparison.OrdinalIgnoreCase));
					if (index >= 0 && index < fields.Length)
						row.Values[column] = fields[index]?.Trim();
				}
				row.Values.TryGetValue("DisplayName", out var name);
				if (string.IsNullOrWhiteSpace(name)) row.Errors.Add("DisplayNameRequired");
				else if (name.Length > 200) row.Errors.Add("DisplayNameTooLong");
				if (row.Values.TryGetValue("PaymentClass", out var paymentClass) && !string.IsNullOrWhiteSpace(paymentClass) && !Enum.TryParse<IncidentPaymentClasses>(paymentClass, true, out _))
					row.Errors.Add("PaymentClassInvalid");
				if (row.Values.TryGetValue("ExpectedArrival", out var eta) && !string.IsNullOrWhiteSpace(eta) && !DateTime.TryParse(eta, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
					row.Errors.Add("ExpectedArrivalInvalid");
				var key = Key(Value(row, "Agency"), Value(row, "OrderNumber"), name);
				row.IsDuplicate = existing.Contains(key) || !seen.Add(key);
				stage.Rows.Add(row);
			}
			return stage;
		}

		public async Task<int> CommitRosterImportAsync(IncidentActor actor, string activationId, string csv, CancellationToken cancellationToken = default)
		{
			var stage = await StageRosterImportAsync(actor, activationId, csv, cancellationToken);
			var committed = 0;
			foreach (var row in stage.Rows.Where(r => r.Errors.Count == 0 && !r.IsDuplicate))
			{
				Enum.TryParse<IncidentPaymentClasses>(Value(row, "PaymentClass") ?? "Cooperator", true, out var paymentClass);
				DateTime? eta = DateTime.TryParse(Value(row, "ExpectedArrival"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
				await _participants.SaveAsync(actor, new IncidentParticipant
				{
					IncidentActivationId = activationId,
					DisplayName = Value(row, "DisplayName"),
					Agency = Limit(Value(row, "Agency"), 200),
					HomeUnit = Limit(Value(row, "HomeUnit"), 200),
					PositionTitle = Limit(Value(row, "PositionTitle"), 200),
					OrderNumber = Limit(Value(row, "OrderNumber"), 100),
					RequestNumber = Limit(Value(row, "RequestNumber"), 100),
					PaymentClass = (int)paymentClass,
					SupportStatus = (int)(eta.HasValue ? IncidentSupportStatuses.EnRoute : IncidentSupportStatuses.Ordered),
					EnrollmentSource = (int)IncidentEnrollmentSources.Import,
					ExpectedArrivalOn = eta,
					PersonalPhone = Value(row, "PersonalPhone"),
					PersonalEmail = Value(row, "PersonalEmail"),
					NeedsLodging = true,
					NeedsMeals = true,
					CheckInCode = IncidentCodes.NewCheckInCode()
				}, null, cancellationToken);
				committed++;
			}
			return committed;
		}

		private static string Value(IncidentImportRow row, string column) => row.Values.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
		private static string Limit(string value, int length) => value == null ? null : value.Length <= length ? value : value.Substring(0, length);
		private static string Key(string agency, string order, string name) => $"{agency?.Trim()}|{order?.Trim()}|{name?.Trim()}";
	}

	/// <summary>
	/// Plan §13.8: resolves a position code to its current holders at send time (period-scoped by effective dating), falling back
	/// to the section chief and then department administrators; skips members who are deleted, disabled or hidden; logs every
	/// resolution; and delivers through the shipped notification stack in each recipient's language. Exercise activations reach
	/// Resgrid users only, prefixed as an exercise.
	/// </summary>
	public class IncidentNotificationRoutingService : IIncidentNotificationRoutingService
	{
		private readonly IIncidentLedgerService<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerService<IncidentPositionDefinition> _positions;
		private readonly IIncidentLedgerService<IncidentNotificationLog> _log;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IUserProfileService _profiles;
		private readonly ICommunicationService _communication;

		public IncidentNotificationRoutingService(IIncidentLedgerService<IncidentPositionAssignment> assignments, IIncidentLedgerService<IncidentPositionDefinition> positions,
			IIncidentLedgerService<IncidentNotificationLog> log, IIncidentBackOfficeSettingsService settings, IDepartmentsService departments,
			IDepartmentSettingsService departmentSettings, IUserProfileService profiles, ICommunicationService communication)
		{
			_assignments = assignments;
			_positions = positions;
			_log = log;
			_settings = settings;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_profiles = profiles;
			_communication = communication;
		}

		public async Task<IReadOnlyList<string>> ResolveHoldersAsync(IncidentActor actor, IncidentActivation activation, string positionCode, CancellationToken cancellationToken = default)
		{
			var (holders, _) = await ResolveWithFallbackAsync(actor, activation, positionCode, cancellationToken);
			return holders;
		}

		private async Task<(List<string> Holders, IncidentRoutingFallbacks Fallback)> ResolveWithFallbackAsync(IncidentActor actor, IncidentActivation activation, string positionCode, CancellationToken cancellationToken)
		{
			var positions = (await _positions.ListAsync(actor, new IncidentLedgerQuery { ActivationIds = new[] { activation.IncidentActivationId }, IncludeDepartmentCatalog = true }.Page(0, 2000), cancellationToken))
				.GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			var open = (await _assignments.ListAsync(actor, IncidentLedgerQuery.ForActivation(activation.IncidentActivationId).WhereNotNull("UserId").Page(0, 5000), cancellationToken))
				.Where(a => a.StartOn <= now && (a.EndOn == null || a.EndOn > now)).ToList();

			async Task<List<string>> Current(string code)
			{
				if (code == null || !positions.TryGetValue(code, out var position)) return new List<string>();
				var users = open.Where(a => a.IncidentPositionDefinitionId == position.IncidentPositionDefinitionId).Select(a => a.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				var current = new List<string>();
				foreach (var user in users)
					if (Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(await _departments.GetDepartmentMemberAsync(user, actor.DepartmentId, false), actor.DepartmentId))
						current.Add(user);
				return current;
			}

			var holders = await Current(positionCode);
			if (holders.Count > 0) return (holders, IncidentRoutingFallbacks.None);

			var section = positions.TryGetValue(positionCode ?? string.Empty, out var defined) ? (IncidentSections)defined.Section
				: IncidentPositionCatalog.All.FirstOrDefault(e => string.Equals(e.Code, positionCode, StringComparison.OrdinalIgnoreCase))?.Section ?? IncidentSections.Command;
			holders = await Current(IncidentPositionCatalog.SectionChief(section));
			if (holders.Count > 0) return (holders, IncidentRoutingFallbacks.SectionChief);

			holders = (await _departments.GetActiveAdminsForDepartmentAsync(actor.DepartmentId) ?? new List<Model.Identity.IdentityUser>()).Select(a => a.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
			return (holders, holders.Count > 0 ? IncidentRoutingFallbacks.DepartmentAdministrators : IncidentRoutingFallbacks.NoRecipient);
		}

		public async Task<int> NotifyPositionsAsync(IncidentActor actor, IncidentActivation activation, Model.Events.EventTypes notice, Func<CultureInfo, string> message,
			IReadOnlyList<string> positionOverride = null, CancellationToken cancellationToken = default)
		{
			if (activation == null) return 0;
			var policy = await _settings.GetAsync<IncidentNotificationRoutingPolicy>(actor.DepartmentId);
			var codes = positionOverride ?? policy.For((int)notice);
			var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var code in codes)
			{
				var (holders, fallback) = await ResolveWithFallbackAsync(actor, activation, code, cancellationToken);
				await LogAsync(actor, activation, notice, code, holders, fallback, cancellationToken);
				foreach (var holder in holders) recipients.Add(holder);
			}
			var sent = 0;
			foreach (var recipient in recipients)
				if (await SendAsync(actor, activation, recipient, message, cancellationToken))
					sent++;
			return sent;
		}

		public async Task<bool> NotifyUserAsync(IncidentActor actor, IncidentActivation activation, Model.Events.EventTypes notice, string userId, Func<CultureInfo, string> message, CancellationToken cancellationToken = default)
		{
			if (activation == null || string.IsNullOrWhiteSpace(userId)) return false;
			if (!Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(await _departments.GetDepartmentMemberAsync(userId, actor.DepartmentId, false), actor.DepartmentId))
				return false;
			await LogAsync(actor, activation, notice, null, new List<string> { userId }, IncidentRoutingFallbacks.None, cancellationToken);
			return await SendAsync(actor, activation, userId, message, cancellationToken);
		}

		private async Task LogAsync(IncidentActor actor, IncidentActivation activation, Model.Events.EventTypes notice, string code, List<string> holders, IncidentRoutingFallbacks fallback, CancellationToken cancellationToken)
		{
			try
			{
				await _log.SaveAsync(actor, new IncidentNotificationLog
				{
					IncidentActivationId = activation.IncidentActivationId,
					EventType = (int)notice,
					PositionCode = code,
					HoldersJson = Newtonsoft.Json.JsonConvert.SerializeObject(holders),
					Fallback = (int)fallback,
					RecipientCount = holders.Count
				}, null, cancellationToken);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Incident notification routing could not be logged.");
			}
		}

		private async Task<bool> SendAsync(IncidentActor actor, IncidentActivation activation, string userId, Func<CultureInfo, string> message, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, false);
				var number = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(actor.DepartmentId);
				var profile = await _profiles.GetProfileByUserIdAsync(userId, false);
				var culture = Onboarding.DepartmentImportNotificationService.CultureFor(profile?.Language);
				var text = message(culture);
				if (activation.IsExercise)
					text = IncidentNoticeText.Format(culture, "Notice.ExercisePrefix", text);
				var title = IncidentNoticeText.Format(culture, "Notice.Title", activation.ShortCode);
				return await _communication.SendNotificationAsync(userId, actor.DepartmentId, text, number, department, title, profile);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Incident Back Office notice for department {actor.DepartmentId} could not be sent.");
				return false;
			}
		}
	}

	/// <summary>A small RFC 4180 reader for desk imports (quoted fields, doubled quotes, CRLF or LF). Formula prefixes are not evaluated.</summary>
	public static class IncidentCsv
	{
		public static List<string[]> Parse(string text)
		{
			var rows = new List<string[]>();
			if (string.IsNullOrEmpty(text))
				return rows;
			var row = new List<string>();
			var field = new System.Text.StringBuilder();
			var quoted = false;
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (quoted)
				{
					if (c == '"')
					{
						if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
						else quoted = false;
					}
					else field.Append(c);
					continue;
				}
				if (c == '"') { quoted = true; continue; }
				if (c == ',') { row.Add(field.ToString()); field.Clear(); continue; }
				if (c == (char)13) continue;
				if (c == (char)10) { row.Add(field.ToString()); field.Clear(); rows.Add(row.ToArray()); row = new List<string>(); continue; }
				field.Append(c);
			}
			if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
			if (rows.Count > 0 && rows[0].Length > 0 && rows[0][0].Length > 0 && rows[0][0][0] == (char)0xFEFF)
				rows[0][0] = rows[0][0].Substring(1);
			return rows;
		}

		/// <summary>Neutralizes spreadsheet formula prefixes in an exported cell (plan §13.9 C12).</summary>
		public static string Cell(string value)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			if (value[0] is '=' or '+' or '-' or '@' or (char)9 or (char)13) value = "'" + value;
			return value.IndexOfAny(new[] { ',', '"', (char)10, (char)13 }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
		}
	}
}
