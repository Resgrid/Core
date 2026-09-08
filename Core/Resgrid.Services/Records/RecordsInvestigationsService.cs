using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Investigation cases (RMS plan section 4.4, RMS-5). Case-level authorization sits on top of RecordRestricted_View:
	/// every read and change requires an active membership, and department administrator status is not a bypass (plan
	/// section 5.7). Every read of a case is audited; every export is audited; custody is append-only; findings never
	/// touch the linked incident revision.
	/// </summary>
	public class RecordsInvestigationsService : IRecordsInvestigationsService
	{
		public const string AuditPurpose = "investigation";

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsInvestigationCasesRepository _cases;
		private readonly IRmsInvestigationCaseIncidentsRepository _incidents;
		private readonly IRmsInvestigationCaseMembersRepository _members;
		private readonly IRmsInvestigationNotesRepository _notes;
		private readonly IRmsInvestigationEvidenceRepository _evidence;
		private readonly IRmsInvestigationCustodyRepository _custody;
		private readonly IRmsInvestigationReferralsRepository _referrals;
		private readonly IRmsPreventionAttachmentsRepository _attachments;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsInvestigationsService(RecordsPreventionGate gate, IRmsInvestigationCasesRepository cases, IRmsInvestigationCaseIncidentsRepository incidents, IRmsInvestigationCaseMembersRepository members,
			IRmsInvestigationNotesRepository notes, IRmsInvestigationEvidenceRepository evidence, IRmsInvestigationCustodyRepository custody, IRmsInvestigationReferralsRepository referrals,
			IRmsPreventionAttachmentsRepository attachments, IRmsIncidentReportsRepository reports, IRmsAccessAuditsRepository audits, IRecordsAuthorizationService authorization,
			IRecordsProtectionService protection, IUnitOfWork unitOfWork)
		{
			_gate = gate; _cases = cases; _incidents = incidents; _members = members; _notes = notes; _evidence = evidence; _custody = custody; _referrals = referrals;
			_attachments = attachments; _reports = reports; _audits = audits; _authorization = authorization; _protection = protection; _unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Investigations);

		#region Authorization

		private async Task<(RmsInvestigationCase Case, RmsInvestigationRole Role)> RequireMemberAsync(int departmentId, string userId, string caseId, params RmsInvestigationRole[] allowedRoles)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Investigations);
			await _gate.RequireRestrictedAsync(departmentId, userId);
			var investigation = await _cases.GetByIdForDepartmentAsync(departmentId, caseId);
			if (investigation == null || investigation.DeletedOn != null) throw new ArgumentException("The case does not exist.");
			var membership = ((await _members.GetForCaseAsync(departmentId, caseId)) ?? Enumerable.Empty<RmsInvestigationCaseMember>()).FirstOrDefault(m => m.UserId == userId && m.IsActive);
			if (membership == null)
			{
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Denied, AuditPurpose, caseId, new { reason = "not a case member" }, successful: false);
				throw new UnauthorizedAccessException("Only members of the case may access it.");
			}
			var role = (RmsInvestigationRole)membership.Role;
			if (allowedRoles != null && allowedRoles.Length > 0 && !allowedRoles.Contains(role))
			{
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Denied, AuditPurpose, caseId, new { reason = $"role {role} may not perform this action" }, successful: false);
				throw new UnauthorizedAccessException($"This action needs the {string.Join(" or ", allowedRoles)} role on the case.");
			}
			return (investigation, role);
		}

		private static void RequireOpen(RmsInvestigationCase investigation)
		{
			if (investigation.IsClosed) throw new InvalidOperationException("The case is closed; reopen it before making changes.");
		}

		#endregion

		#region Cases

		public async Task<List<RmsInvestigationCase>> ListMyCasesAsync(int departmentId, string userId, bool includeClosed)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Investigations);
			await _gate.RequireRestrictedAsync(departmentId, userId);
			var memberships = (await _members.GetActiveForUserAsync(departmentId, userId))?.Select(m => m.RmsInvestigationCaseId).Distinct().ToList() ?? new List<string>();
			if (memberships.Count == 0) return new List<RmsInvestigationCase>();
			var cases = ((await _cases.GetByIdsAsync(departmentId, memberships)) ?? Enumerable.Empty<RmsInvestigationCase>()).Where(c => includeClosed || !c.IsClosed).ToList();
			await _protection.RevealInvestigationCasesAsync(departmentId, cases);
			return cases;
		}

		public async Task<InvestigationCaseAggregate> GetAsync(int departmentId, string userId, string caseId, string ipAddress = null)
		{
			var (investigation, role) = await RequireMemberAsync(departmentId, userId, caseId);
			var aggregate = new InvestigationCaseAggregate
			{
				Case = investigation, CallerRole = role,
				Members = (await _members.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationCaseMember>(),
				Incidents = (await _incidents.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationCaseIncident>(),
				Notes = (await _notes.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationNote>(),
				Evidence = (await _evidence.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationEvidence>(),
				Referrals = (await _referrals.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationReferral>(),
				Attachments = (await _attachments.GetMetadataForParentAsync(departmentId, RmsPreventionParentKind.InvestigationCase, caseId))?.ToList() ?? new List<RmsPreventionAttachment>()
			};
			aggregate.Protection.Merge(await _protection.RevealInvestigationCasesAsync(departmentId, new[] { investigation }));
			aggregate.Protection.Merge(await _protection.RevealInvestigationNotesAsync(departmentId, aggregate.Notes));
			aggregate.Protection.Merge(await _protection.RevealInvestigationEvidenceAsync(departmentId, aggregate.Evidence));
			aggregate.Protection.Merge(await _protection.RevealInvestigationReferralsAsync(departmentId, aggregate.Referrals));
			aggregate.Protection.Merge(await _protection.RevealPreventionAttachmentsAsync(departmentId, aggregate.Attachments, false));
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Read, AuditPurpose, caseId, new { investigation.CaseNumber, role = role.ToString(), redacted = aggregate.Protection.RedactedFields.Count }, ipAddress: ipAddress);
			return aggregate;
		}

		public async Task<RmsInvestigationCase> OpenAsync(int departmentId, string userId, string title, string occupancyId, int? callId, string incidentSummary, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Investigations);
			await _gate.RequireRestrictedAsync(departmentId, userId);
			var now = DateTime.UtcNow;
			var investigation = new RmsInvestigationCase
			{
				RmsInvestigationCaseId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				CaseNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Investigation, now, cancellationToken),
				Title = RecordsPreventionGate.Require(title, 250, "A case needs a title."), State = (int)RmsInvestigationCaseState.Open, OpenedOn = now, OpenedByUserId = userId, LeadInvestigatorUserId = userId,
				RmsOccupancyId = RecordsPreventionGate.Trim(occupancyId, 36), CallId = callId, IncidentSummary = RecordsPreventionGate.Trim(incidentSummary, 8000),
				CauseClassification = (int)RmsFireCauseClassification.UnderInvestigation, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsInvestigationCase>.Take(investigation, RmsProtectedFields.InvestigationCases);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectInvestigationCaseAsync(departmentId, investigation, null, userId, cancellationToken);
				await _cases.InsertAsync(investigation, cancellationToken, true);
				await _members.InsertAsync(new RmsInvestigationCaseMember { RmsInvestigationCaseMemberId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = investigation.RmsInvestigationCaseId, UserId = userId, Role = (int)RmsInvestigationRole.Lead, AddedOn = now, AddedByUserId = userId }, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, AuditPurpose, investigation.RmsInvestigationCaseId, new { action = "opened", investigation.CaseNumber }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			plaintext.Restore();
			return investigation;
		}

		public async Task<RmsInvestigationCase> UpdateAsync(int departmentId, string userId, RmsInvestigationCase input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var (investigation, role) = await RequireMemberAsync(departmentId, userId, input.RmsInvestigationCaseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			if (input.RowVersion != 0 && input.RowVersion != investigation.RowVersion) throw new InvalidOperationException("The case changed since it was loaded. Reload it before saving.");
			var existing = new RmsInvestigationCase { IncidentSummary = investigation.IncidentSummary, CauseDetail = investigation.CauseDetail, OriginDescription = investigation.OriginDescription, Findings = investigation.Findings, ClosureReason = investigation.ClosureReason };
			investigation.Title = RecordsPreventionGate.Require(input.Title, 250, "A case needs a title.");
			investigation.IncidentSummary = RecordsPreventionGate.Trim(input.IncidentSummary, 8000); investigation.RmsOccupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36); investigation.CallId = input.CallId;
			// Only a lead reassigns the lead. An investigator may edit the case body, so the posted value is
			// authoritative for nobody else: a tampered field is denied rather than quietly applied.
			var requestedLead = RecordsPreventionGate.Trim(input.LeadInvestigatorUserId, 128);
			if (requestedLead != null && requestedLead != investigation.LeadInvestigatorUserId)
			{
				if (role != RmsInvestigationRole.Lead)
				{
					await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Denied, AuditPurpose, investigation.RmsInvestigationCaseId,
						new { reason = "only a lead investigator may reassign the lead investigator" }, successful: false, cancellationToken: cancellationToken);
					throw new UnauthorizedAccessException("Only a lead investigator may reassign the lead investigator.");
				}

				if (!await _authorization.IsActiveMemberAsync(requestedLead, departmentId))
					throw new ArgumentException("The lead investigator must be an active member of the department.");

				investigation.LeadInvestigatorUserId = requestedLead;
			}
			investigation.ModifiedOn = DateTime.UtcNow; investigation.RowVersion++;
			await SealAndUpdateAsync(departmentId, userId, investigation, existing, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, investigation.RmsInvestigationCaseId, new { action = "updated" }, cancellationToken: cancellationToken);
			return investigation;
		}

		private async Task SealAndUpdateAsync(int departmentId, string userId, RmsInvestigationCase investigation, RmsInvestigationCase existing, CancellationToken cancellationToken)
		{
			var plaintext = PlaintextSnapshot<RmsInvestigationCase>.Take(investigation, RmsProtectedFields.InvestigationCases);
			await _protection.ProtectInvestigationCaseAsync(departmentId, investigation, existing, userId, cancellationToken);
			await _cases.UpdateAsync(investigation, cancellationToken, true);
			plaintext.Restore();
		}

		#endregion

		#region Members and incidents

		public async Task<RmsInvestigationCaseMember> AddMemberAsync(int departmentId, string userId, string caseId, string memberUserId, RmsInvestigationRole role, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead);
			RequireOpen(investigation);
			if (string.IsNullOrWhiteSpace(memberUserId) || !await _authorization.IsActiveMemberAsync(memberUserId, departmentId)) throw new ArgumentException("The member must be an active member of the department.");
			var members = (await _members.GetForCaseAsync(departmentId, caseId))?.ToList() ?? new List<RmsInvestigationCaseMember>();
			var existing = members.FirstOrDefault(m => m.UserId == memberUserId && m.IsActive);
			var now = DateTime.UtcNow;
			if (existing != null) { existing.Role = (int)role; await _members.UpdateAsync(existing, cancellationToken, true); }
			else
			{
				existing = new RmsInvestigationCaseMember { RmsInvestigationCaseMemberId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = caseId, UserId = memberUserId, Role = (int)role, AddedOn = now, AddedByUserId = userId };
				await _members.InsertAsync(existing, cancellationToken, true);
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, AuditPurpose, caseId, new { action = "member added", memberUserId, role = role.ToString() }, cancellationToken: cancellationToken);
			return existing;
		}

		public async Task RemoveMemberAsync(int departmentId, string userId, string caseId, string memberId, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead);
			RequireOpen(investigation);
			var member = await _members.GetByIdForDepartmentAsync(departmentId, memberId);
			if (member == null || member.RmsInvestigationCaseId != caseId || !member.IsActive) throw new ArgumentException("The member does not exist on this case.");
			var leads = ((await _members.GetForCaseAsync(departmentId, caseId)) ?? Enumerable.Empty<RmsInvestigationCaseMember>()).Count(m => m.IsActive && m.Role == (int)RmsInvestigationRole.Lead);
			if (member.Role == (int)RmsInvestigationRole.Lead && leads <= 1) throw new InvalidOperationException("A case keeps at least one lead investigator.");
			member.RemovedOn = DateTime.UtcNow; member.RemovedByUserId = userId;
			await _members.UpdateAsync(member, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, AuditPurpose, caseId, new { action = "member removed", member.UserId }, cancellationToken: cancellationToken);
		}

		public async Task<RmsInvestigationCaseIncident> LinkIncidentAsync(int departmentId, string userId, string caseId, string recordId, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, recordId);
			if (report == null || report.DeletedOn != null) throw new ArgumentException("The incident report does not exist.");
			if (!await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) throw new UnauthorizedAccessException("You cannot view that incident report.");
			var existing = ((await _incidents.GetForCaseAsync(departmentId, caseId)) ?? Enumerable.Empty<RmsInvestigationCaseIncident>()).FirstOrDefault(i => i.RecordId == recordId);
			if (existing != null) return existing;
			var link = new RmsInvestigationCaseIncident { RmsInvestigationCaseIncidentId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = caseId, RecordId = recordId, PinnedRevisionId = report.CurrentRevisionId, RecordNumber = report.RecordNumber ?? report.IncidentNumber, LinkedOn = DateTime.UtcNow, LinkedByUserId = userId };
			await _incidents.InsertAsync(link, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "incident linked", recordId, link.PinnedRevisionId }, cancellationToken: cancellationToken);
			return link;
		}

		public async Task UnlinkIncidentAsync(int departmentId, string userId, string caseId, string linkId, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var link = ((await _incidents.GetForCaseAsync(departmentId, caseId)) ?? Enumerable.Empty<RmsInvestigationCaseIncident>()).FirstOrDefault(i => i.RmsInvestigationCaseIncidentId == linkId) ?? throw new ArgumentException("The link does not exist.");
			await _incidents.DeleteAsync(link, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "incident unlinked", link.RecordId }, cancellationToken: cancellationToken);
		}

		#endregion

		#region Notes, evidence, referrals

		public async Task<RmsInvestigationNote> AddNoteAsync(int departmentId, string userId, string caseId, RmsInvestigationNoteKind kind, DateTime occurredOn, string subject, string body, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var now = DateTime.UtcNow;
			var note = new RmsInvestigationNote
			{
				RmsInvestigationNoteId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = caseId, Kind = (int)kind,
				OccurredOn = occurredOn == default ? now : occurredOn, AuthorUserId = userId, Subject = RecordsPreventionGate.Trim(subject, 250), Body = RecordsPreventionGate.Require(body, 32000, "A note needs content."), CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsInvestigationNote>.Take(note, RmsProtectedFields.InvestigationNotes);
			await _protection.ProtectInvestigationNoteAsync(departmentId, note, null, userId, cancellationToken);
			await _notes.InsertAsync(note, cancellationToken, true);
			plaintext.Restore();
			await TouchCaseAsync(investigation, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "note added", note.RmsInvestigationNoteId, kind = kind.ToString() }, cancellationToken: cancellationToken);
			return note;
		}

		public async Task<RmsInvestigationNote> UpdateNoteAsync(int departmentId, string userId, string noteId, string subject, string body, CancellationToken cancellationToken = default)
		{
			var note = await _notes.GetByIdForDepartmentAsync(departmentId, noteId);
			if (note == null || note.DeletedOn != null) throw new ArgumentException("The note does not exist.");
			var (investigation, role) = await RequireMemberAsync(departmentId, userId, note.RmsInvestigationCaseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			if (note.IsLocked) throw new InvalidOperationException("The note is locked and cannot be edited.");
			if (note.AuthorUserId != userId && role != RmsInvestigationRole.Lead) throw new UnauthorizedAccessException("Only the author or the lead may edit a note.");
			var existing = new RmsInvestigationNote { Subject = note.Subject, Body = note.Body };
			note.Subject = RecordsPreventionGate.Trim(subject, 250); note.Body = RecordsPreventionGate.Require(body, 32000, "A note needs content."); note.ModifiedOn = DateTime.UtcNow; note.RowVersion++;
			var plaintext = PlaintextSnapshot<RmsInvestigationNote>.Take(note, RmsProtectedFields.InvestigationNotes);
			await _protection.ProtectInvestigationNoteAsync(departmentId, note, existing, userId, cancellationToken);
			await _notes.UpdateAsync(note, cancellationToken, true);
			plaintext.Restore();
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, note.RmsInvestigationCaseId, new { action = "note updated", noteId }, cancellationToken: cancellationToken);
			return note;
		}

		public async Task<RmsInvestigationEvidence> AddEvidenceAsync(int departmentId, string userId, string caseId, RmsInvestigationEvidence input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var now = DateTime.UtcNow;
			var item = new RmsInvestigationEvidence
			{
				RmsInvestigationEvidenceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = caseId,
				EvidenceNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Evidence, now, cancellationToken), Kind = input.Kind == 0 ? (int)RmsInvestigationEvidenceKind.Physical : input.Kind,
				Description = RecordsPreventionGate.Require(input.Description, 4000, "Evidence needs a description."), CollectedOn = input.CollectedOn == default ? now : input.CollectedOn,
				CollectedByUserId = string.IsNullOrWhiteSpace(input.CollectedByUserId) ? userId : input.CollectedByUserId.Trim(), CollectedFrom = RecordsPreventionGate.Trim(input.CollectedFrom, 1000),
				State = (int)RmsEvidenceState.Collected, StorageLocation = RecordsPreventionGate.Trim(input.StorageLocation, 250), CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			item.CurrentCustodianUserId = item.CollectedByUserId;
			var custody = new RmsInvestigationCustody { RmsInvestigationCustodyId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationEvidenceId = item.RmsInvestigationEvidenceId, Sequence = 1, TransferredOn = item.CollectedOn, ToUserId = item.CollectedByUserId, Reason = "Collected", ResultingState = (int)RmsEvidenceState.Collected, RecordedByUserId = userId, CreatedOn = now };
			var plaintext = PlaintextSnapshot<RmsInvestigationEvidence>.Take(item, RmsProtectedFields.InvestigationEvidence);
			var custodyPlain = PlaintextSnapshot<RmsInvestigationCustody>.Take(custody, RmsProtectedFields.InvestigationCustody);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectInvestigationEvidenceAsync(departmentId, item, null, userId, cancellationToken);
				await _evidence.InsertAsync(item, cancellationToken, true);
				await _protection.ProtectInvestigationCustodyAsync(departmentId, custody, userId, cancellationToken);
				await _custody.InsertAsync(custody, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "evidence added", item.RmsInvestigationEvidenceId, item.EvidenceNumber }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			plaintext.Restore(); custodyPlain.Restore();
			await TouchCaseAsync(investigation, cancellationToken);
			return item;
		}

		public async Task<RmsInvestigationCustody> TransferCustodyAsync(int departmentId, string userId, string evidenceId, string toUserId, string toExternal, string reason, RmsEvidenceState resultingState, CancellationToken cancellationToken = default)
		{
			var item = await _evidence.GetByIdForDepartmentAsync(departmentId, evidenceId);
			if (item == null || item.DeletedOn != null) throw new ArgumentException("The evidence item does not exist.");
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, item.RmsInvestigationCaseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			if (string.IsNullOrWhiteSpace(toUserId) && string.IsNullOrWhiteSpace(toExternal) && resultingState != RmsEvidenceState.Destroyed) throw new ArgumentException("Name who receives the evidence.");
			if (item.State == (int)RmsEvidenceState.Destroyed) throw new InvalidOperationException("Destroyed evidence has no further custody.");
			reason = RecordsPreventionGate.Require(reason, 1000, "A custody transfer needs a reason.");
			var now = DateTime.UtcNow;
			var sequence = ((await _custody.GetForEvidenceAsync(departmentId, evidenceId))?.Count() ?? 0) + 1;
			var transfer = new RmsInvestigationCustody
			{
				RmsInvestigationCustodyId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationEvidenceId = evidenceId, Sequence = sequence, TransferredOn = now,
				FromUserId = item.CurrentCustodianUserId, FromExternal = item.CurrentCustodianExternal, ToUserId = RecordsPreventionGate.Trim(toUserId, 128), ToExternal = RecordsPreventionGate.Trim(toExternal, 250), Reason = reason,
				ResultingState = (int)resultingState, RecordedByUserId = userId, CreatedOn = now
			};
			var existing = new RmsInvestigationEvidence { Description = item.Description, CollectedFrom = item.CollectedFrom, CurrentCustodianExternal = item.CurrentCustodianExternal };
			item.CurrentCustodianUserId = transfer.ToUserId; item.CurrentCustodianExternal = transfer.ToExternal; item.State = (int)resultingState; item.ModifiedOn = now; item.RowVersion++;
			var transferPlain = PlaintextSnapshot<RmsInvestigationCustody>.Take(transfer, RmsProtectedFields.InvestigationCustody);
			var itemPlain = PlaintextSnapshot<RmsInvestigationEvidence>.Take(item, RmsProtectedFields.InvestigationEvidence);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectInvestigationCustodyAsync(departmentId, transfer, userId, cancellationToken);
				await _custody.InsertAsync(transfer, cancellationToken, true);
				await _protection.ProtectInvestigationEvidenceAsync(departmentId, item, existing, userId, cancellationToken);
				await _evidence.UpdateAsync(item, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, item.RmsInvestigationCaseId, new { action = "custody transferred", evidenceId, sequence, state = resultingState.ToString() }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			transferPlain.Restore(); itemPlain.Restore();
			return transfer;
		}

		public async Task<List<RmsInvestigationCustody>> GetCustodyChainAsync(int departmentId, string userId, string evidenceId)
		{
			var item = await _evidence.GetByIdForDepartmentAsync(departmentId, evidenceId);
			if (item == null || item.DeletedOn != null) throw new ArgumentException("The evidence item does not exist.");
			await RequireMemberAsync(departmentId, userId, item.RmsInvestigationCaseId);
			var chain = (await _custody.GetForEvidenceAsync(departmentId, evidenceId))?.ToList() ?? new List<RmsInvestigationCustody>();
			await _protection.RevealInvestigationCustodyAsync(departmentId, chain);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Read, AuditPurpose, item.RmsInvestigationCaseId, new { action = "custody chain read", evidenceId });
			return chain;
		}

		public async Task<RmsInvestigationReferral> AddReferralAsync(int departmentId, string userId, string caseId, string agency, string reason, string referenceNumber, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var now = DateTime.UtcNow;
			var referral = new RmsInvestigationReferral
			{
				RmsInvestigationReferralId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInvestigationCaseId = caseId,
				Agency = RecordsPreventionGate.Require(agency, 250, "A referral names the receiving agency."), ReferredOn = now, ReferredByUserId = userId, Reason = RecordsPreventionGate.Trim(reason, 4000),
				ReferenceNumber = RecordsPreventionGate.Trim(referenceNumber, 100), State = (int)RmsReferralState.Sent, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsInvestigationReferral>.Take(referral, RmsProtectedFields.InvestigationReferrals);
			await _protection.ProtectInvestigationReferralAsync(departmentId, referral, null, userId, cancellationToken);
			await _referrals.InsertAsync(referral, cancellationToken, true);
			plaintext.Restore();
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Share, AuditPurpose, caseId, new { action = "referral sent", referral.Agency, referral.ReferenceNumber }, cancellationToken: cancellationToken);
			return referral;
		}

		public async Task<RmsInvestigationReferral> UpdateReferralStateAsync(int departmentId, string userId, string referralId, RmsReferralState state, CancellationToken cancellationToken = default)
		{
			var referral = await _referrals.GetByIdForDepartmentAsync(departmentId, referralId) ?? throw new ArgumentException("The referral does not exist.");
			await RequireMemberAsync(departmentId, userId, referral.RmsInvestigationCaseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			referral.State = (int)state; referral.ModifiedOn = DateTime.UtcNow; referral.RowVersion++;
			await _referrals.UpdateAsync(referral, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, referral.RmsInvestigationCaseId, new { action = "referral state", referralId, state = state.ToString() }, cancellationToken: cancellationToken);
			return referral;
		}

		#endregion

		#region Findings, closure

		public async Task<RmsInvestigationCase> RecordFindingsAsync(int departmentId, string userId, string caseId, RmsFireCauseClassification classification, string causeDetail, string originDescription, string findings, bool recommendsIncidentAmendment, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Investigator);
			RequireOpen(investigation);
			var existing = Snapshot(investigation);
			var now = DateTime.UtcNow;
			investigation.CauseClassification = (int)classification; investigation.CauseDetail = RecordsPreventionGate.Trim(causeDetail, 8000); investigation.OriginDescription = RecordsPreventionGate.Trim(originDescription, 8000);
			investigation.Findings = RecordsPreventionGate.Require(findings, 32000, "Findings are required."); investigation.FindingsAuthorUserId = userId; investigation.FindingsRecordedOn = now;
			investigation.FindingsApprovedOn = null; investigation.FindingsApprovedByUserId = null; investigation.RecommendsIncidentAmendment = recommendsIncidentAmendment;
			investigation.State = (int)RmsInvestigationCaseState.PendingReview; investigation.ModifiedOn = now; investigation.RowVersion++;
			await SealAndUpdateAsync(departmentId, userId, investigation, existing, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "findings recorded", classification = classification.ToString(), recommendsIncidentAmendment }, cancellationToken: cancellationToken);
			return investigation;
		}

		public async Task<RmsInvestigationCase> ApproveFindingsAsync(int departmentId, string userId, string caseId, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Reviewer);
			RequireOpen(investigation);
			if (investigation.State != (int)RmsInvestigationCaseState.PendingReview) throw new InvalidOperationException("There are no findings awaiting approval.");
			if (string.Equals(investigation.FindingsAuthorUserId, userId, StringComparison.Ordinal)) throw new InvalidOperationException("Findings must be approved by someone other than their author.");
			var now = DateTime.UtcNow;
			investigation.FindingsApprovedOn = now; investigation.FindingsApprovedByUserId = userId; investigation.State = (int)RmsInvestigationCaseState.Active; investigation.ModifiedOn = now; investigation.RowVersion++;
			await _cases.UpdateAsync(investigation, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Sign, AuditPurpose, caseId, new { action = "findings approved", author = investigation.FindingsAuthorUserId }, cancellationToken: cancellationToken);
			return investigation;
		}

		public async Task<RmsInvestigationCase> ReturnFindingsAsync(int departmentId, string userId, string caseId, string reason, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Reviewer);
			RequireOpen(investigation);
			if (investigation.State != (int)RmsInvestigationCaseState.PendingReview) throw new InvalidOperationException("There are no findings awaiting approval.");
			reason = RecordsPreventionGate.Require(reason, 2000, "Say what needs to change.");
			investigation.State = (int)RmsInvestigationCaseState.Active; investigation.ModifiedOn = DateTime.UtcNow; investigation.RowVersion++;
			await _cases.UpdateAsync(investigation, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "findings returned", reason }, cancellationToken: cancellationToken);
			return investigation;
		}

		public async Task<RmsInvestigationCase> CloseAsync(int departmentId, string userId, string caseId, string closureReason, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead);
			RequireOpen(investigation);
			if (!investigation.FindingsApprovedOn.HasValue) throw new InvalidOperationException("A case closes only after its findings are approved.");
			var existing = Snapshot(investigation);
			var now = DateTime.UtcNow;
			investigation.State = (int)RmsInvestigationCaseState.Closed; investigation.ClosedOn = now; investigation.ClosedByUserId = userId; investigation.ClosureReason = RecordsPreventionGate.Require(closureReason, 4000, "Record why the case is closing.");
			investigation.ModifiedOn = now; investigation.RowVersion++;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await SealAndUpdateAsync(departmentId, userId, investigation, existing, cancellationToken);
				await _notes.LockForCaseAsync(departmentId, caseId, now, cancellationToken);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "closed" }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			return investigation;
		}

		public async Task<RmsInvestigationCase> ReopenAsync(int departmentId, string userId, string caseId, string reason, CancellationToken cancellationToken = default)
		{
			var (investigation, _) = await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead);
			if (!investigation.IsClosed) throw new InvalidOperationException("The case is not closed.");
			reason = RecordsPreventionGate.Require(reason, 2000, "Record why the case is reopening.");
			investigation.State = (int)RmsInvestigationCaseState.Active; investigation.ClosedOn = null; investigation.ClosedByUserId = null; investigation.ModifiedOn = DateTime.UtcNow; investigation.RowVersion++;
			await _cases.UpdateAsync(investigation, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, AuditPurpose, caseId, new { action = "reopened", reason }, cancellationToken: cancellationToken);
			return investigation;
		}

		private static RmsInvestigationCase Snapshot(RmsInvestigationCase c) => new RmsInvestigationCase { IncidentSummary = c.IncidentSummary, CauseDetail = c.CauseDetail, OriginDescription = c.OriginDescription, Findings = c.Findings, ClosureReason = c.ClosureReason };

		private async Task TouchCaseAsync(RmsInvestigationCase investigation, CancellationToken cancellationToken)
		{
			if (investigation.State == (int)RmsInvestigationCaseState.Open) investigation.State = (int)RmsInvestigationCaseState.Active;
			investigation.ModifiedOn = DateTime.UtcNow; investigation.RowVersion++;
			await _cases.UpdateAsync(investigation, cancellationToken, true);
		}

		#endregion

		#region Export and audit

		public async Task<byte[]> ExportAsync(int departmentId, string userId, string caseId, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			var aggregate = await GetAsync(departmentId, userId, caseId, ipAddress);
			aggregate.Protection.RequireRevealed("investigation export");
			var custody = new Dictionary<string, List<RmsInvestigationCustody>>();
			foreach (var item in aggregate.Evidence)
			{
				var chain = (await _custody.GetForEvidenceAsync(departmentId, item.RmsInvestigationEvidenceId))?.ToList() ?? new List<RmsInvestigationCustody>();
				(await _protection.RevealInvestigationCustodyAsync(departmentId, chain)).RequireRevealed("investigation export");
				custody[item.EvidenceNumber ?? item.RmsInvestigationEvidenceId] = chain;
			}
			var packet = new
			{
				contract = "resgrid.investigation-packet.v1", exported_on = DateTime.UtcNow, exported_by = userId, department_id = departmentId,
				aggregate.Case, aggregate.Members, aggregate.Incidents, aggregate.Notes, aggregate.Evidence, custody, aggregate.Referrals,
				attachments = aggregate.Attachments.Select(a => new { a.RmsPreventionAttachmentId, a.FileName, a.ContentType, a.ByteSize, a.Checksum, a.UploadedOn })
			};
			var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet, Formatting.Indented));
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Export, AuditPurpose, caseId, new { action = "packet exported", bytes = bytes.Length, checksum = RecordSnapshotSerializer.Checksum(Encoding.UTF8.GetString(bytes)) }, ipAddress: ipAddress, cancellationToken: cancellationToken);
			return bytes;
		}

		public async Task<List<RmsAccessAudit>> GetAccessAuditAsync(int departmentId, string userId, string caseId, int take)
		{
			await RequireMemberAsync(departmentId, userId, caseId, RmsInvestigationRole.Lead, RmsInvestigationRole.Reviewer);
			return (await _audits.GetForAggregateAsync(departmentId, caseId, take))?.ToList() ?? new List<RmsAccessAudit>();
		}

		#endregion
	}
}
