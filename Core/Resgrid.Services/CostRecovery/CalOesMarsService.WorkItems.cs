using System;
using Resgrid.Model.Helpers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.CostRecovery
{
	/// <summary>
	/// Work items: F-42 / expense-claim projection from the deployment facts, checklist validation, expected
	/// reimbursement, the no-store handoff, the evidence packet, and the observation / reconciliation state machine.
	/// </summary>
	public partial class CalOesMarsService
	{
		private static readonly int[] F42AttachmentTypes = { (int)DeploymentAttachmentTypes.SignedF42, (int)DeploymentAttachmentTypes.PaperF42, (int)DeploymentAttachmentTypes.CrewRotationApproval, (int)DeploymentAttachmentTypes.LossDamage, (int)DeploymentAttachmentTypes.ExternalOrder, (int)DeploymentAttachmentTypes.SignedServiceRequest };

		#region Queue and reads

		public async Task<List<CalOesMarsQueueItem>> GetActionQueueAsync(int departmentId, string userId, bool managerScope)
		{
			var items = (await _workItems.GetActionQueueAsync(departmentId))?.ToList() ?? new List<CalOesMarsWorkItem>();
			var deploymentIds = items.Where(i => !string.IsNullOrWhiteSpace(i.DeploymentId)).Select(i => i.DeploymentId).Distinct().ToList();
			var deployments = new Dictionary<string, Deployment>(StringComparer.OrdinalIgnoreCase);
			foreach (var id in deploymentIds)
			{
				var deployment = await _deploymentService.GetDeploymentByIdAsync(id, departmentId);
				if (deployment != null) deployments[id] = deployment;
			}
			var result = new List<CalOesMarsQueueItem>();
			var now = DateTime.UtcNow;
			foreach (var item in items)
			{
				deployments.TryGetValue(item.DeploymentId ?? string.Empty, out var deployment);
				var mine = deployment != null && !string.IsNullOrWhiteSpace(userId) && deployment.Personnel.Any(p => p.IsActive && string.Equals(p.UserId, userId, StringComparison.OrdinalIgnoreCase));
				// Field users see only their own incident-bound F-42 / expense drafts; managers see the department queue.
				if (!managerScope && (!mine || item.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice)) continue;
				var validation = Deserialize<CalOesMarsValidationResult>(item.ValidationSummaryJson);
				var snapshot = item.RecordType == (int)CalOesMarsRecordTypes.F42 ? Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson) : null;
				item.DeploymentName = deployment?.Name;
				result.Add(new CalOesMarsQueueItem
				{
					WorkItem = item, DeploymentName = deployment?.Name, IncidentNumber = snapshot?.IncidentNumber ?? deployment?.IncidentNumber, RequestNumber = snapshot?.RequestNumber ?? deployment?.RequestNumber,
					ErrorCount = validation?.Errors.Count ?? 0, WarningCount = validation?.Warnings.Count ?? 0, AgeDays = (int)Math.Floor((now - item.AddedOn).TotalDays), IsMine = mine
				});
			}
			return result.OrderBy(r => r.WorkItem.LocalState).ThenByDescending(r => r.AgeDays).ToList();
		}

		public async Task<List<CalOesMarsWorkItem>> GetWorkItemsForDeploymentAsync(string deploymentId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(deploymentId)) return new List<CalOesMarsWorkItem>();
			return (await _workItems.GetByDeploymentAsync(deploymentId, departmentId))?.ToList() ?? new List<CalOesMarsWorkItem>();
		}

		public async Task<CalOesMarsWorkItem> GetWorkItemAsync(string workItemId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(workItemId)) return null;
			var item = await _workItems.GetByIdForDepartmentAsync(workItemId, departmentId);
			if (item == null || item.IsDeleted) return null;
			item.Lines = (await _lines.GetByWorkItemAsync(workItemId))?.ToList() ?? new List<CalOesMarsReimbursementLine>();
			if (!string.IsNullOrWhiteSpace(item.DeploymentId)) item.DeploymentName = (await _deploymentService.GetDeploymentByIdAsync(item.DeploymentId, departmentId))?.Name;
			return item;
		}

		public async Task<bool> IsRosteredForWorkItemAsync(string workItemId, int departmentId, string userId)
		{
			var item = await _workItems.GetByIdForDepartmentAsync(workItemId, departmentId);
			if (item == null || item.IsDeleted || string.IsNullOrWhiteSpace(item.DeploymentId) || string.IsNullOrWhiteSpace(userId)) return false;
			if (item.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice) return false;
			return await _deploymentService.CanFieldMemberSeeAsync(item.DeploymentId, departmentId, userId);
		}

		#endregion

		#region F-42 and expense projection

		public async Task<CalOesMarsWorkItem> BuildF42DraftAsync(string deploymentId, int departmentId, string rmsExternalOrderFillId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deploymentService.GetDeploymentByIdAsync(deploymentId, departmentId) ?? throw new InvalidOperationException("calmars_deployment_not_found");
			var context = await _deploymentService.GetExternalContextAsync(deploymentId, departmentId, userId);
			RmsExternalOrderFill fill = null;
			if (!string.IsNullOrWhiteSpace(rmsExternalOrderFillId))
			{
				fill = context?.Fills.FirstOrDefault(f => string.Equals(f.RmsExternalOrderFillId, rmsExternalOrderFillId, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("calmars_fill_not_found");
			}
			else if (context != null && context.Fills.Count > 1) throw new InvalidOperationException("calmars_fill_required");
			else fill = context?.Fills.FirstOrDefault();

			var agency = await GetAgencyProfileAsync(departmentId);
			var dispatchOn = fill?.MobilizedOn ?? fill?.FilledOn ?? deployment.StartOn ?? deployment.AddedOn;
			var authority = CalOesMarsAuthorityProfile.ForDispatch(dispatchOn) ?? CalOesMarsAuthorityProfile.Current;
			var existingItems = (await _workItems.GetByDeploymentAsync(deploymentId, departmentId))?.Where(w => w.RecordType == (int)CalOesMarsRecordTypes.F42 && w.LocalState != (int)CalOesMarsLocalStates.Closed && string.Equals(w.RmsExternalOrderFillId ?? string.Empty, fill?.RmsExternalOrderFillId ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<CalOesMarsWorkItem>();
			var current = existingItems.OrderByDescending(w => w.AddedOn).FirstOrDefault();
			var previous = current == null ? null : Deserialize<CalOesMarsF42Snapshot>(current.SnapshotJson);

			var snapshot = new CalOesMarsF42Snapshot
			{
				AuthorityProfileCode = authority.Code,
				MacsDesignator = agency?.MacsDesignator,
				AgencyName = agency?.AgencyName,
				IncidentName = context?.Order?.IncidentName ?? deployment.Name,
				IncidentNumber = context?.Order?.IncidentNumber ?? deployment.IncidentNumber,
				OrderNumber = context?.Order?.OrderNumber ?? deployment.ResourceOrderNumber,
				RequestNumber = fill?.RequestNumber ?? deployment.RequestNumber,
				ParentRequestNumber = fill?.ParentRequestNumber,
				ResourceKind = fill?.ResourceKind,
				ResourceType = fill?.ResourceType,
				StrikeTeamOrTaskForce = fill?.AgencyUnitId,
				ReportingLocation = fill?.HostAgency ?? context?.Order?.RequestingAgency,
				PointOfHire = fill?.PointOfHire ?? deployment.PointOfHire,
				DispatchedOn = dispatchOn,
				CommittedOn = fill?.CheckedInOn ?? fill?.AssignedOn ?? dispatchOn,
				ReleasedOn = fill?.ReleasedOn ?? fill?.DemobilizedOn,
				ReturnedOn = fill?.ReturnedOn ?? (deployment.Status == (int)DeploymentStatuses.Completed ? deployment.EndOn : null),
				OverheadPosition = fill?.Position,
				IsRedispatch = current != null && current.IsExternal,
				PreviousOrderNumber = previous?.OrderNumber,
				PreviousRequestNumber = previous?.RequestNumber,
				// Locally authored facts survive a rebuild.
				Comments = previous?.Comments, LossDamage = previous?.LossDamage, SupplyNumbers = previous?.SupplyNumbers,
				RespondingSignerName = previous?.RespondingSignerName, RespondingSignedOn = previous?.RespondingSignedOn,
				IncidentAuthorizerName = previous?.IncidentAuthorizerName, IncidentAuthorizedOn = previous?.IncidentAuthorizedOn,
				DocumentationOnly = previous?.DocumentationOnly ?? false,
				Rotations = previous?.Rotations ?? new List<CalOesMarsF42Rotation>()
			};

			// Vehicles: rostered units through the F-5 crosswalk; issued equipment as special equipment.
			var unitIds = deployment.Units.Where(u => u.IsActive).Select(u => u.UnitId).ToList();
			var crosswalk = (await _resources.GetByUnitIdsAsync(departmentId, unitIds))?.ToList() ?? new List<CalOesMarsResourceProfile>();
			var entries = (await _timeEntries.GetByDeploymentAsync(deploymentId))?.ToList() ?? new List<DeploymentTimeEntry>();
			var reports = await _timeTracking.GetTimeReportsAsync(deploymentId, departmentId);
			var usable = reports.Where(r => r.Status is (int)DeploymentTimeReportStatuses.Submitted or (int)DeploymentTimeReportStatuses.Approved or (int)DeploymentTimeReportStatuses.Billed).ToDictionary(r => r.DeploymentTimeReportId, StringComparer.OrdinalIgnoreCase);
			var committedHours = Hours(snapshot.CommittedOn ?? dispatchOn, snapshot.ReturnedOn ?? snapshot.ReleasedOn);
			foreach (var unit in deployment.Units.Where(u => u.IsActive))
			{
				var profile = crosswalk.FirstOrDefault(r => r.UnitId == unit.UnitId && r.IsCurrent(dispatchOn)) ?? crosswalk.FirstOrDefault(r => r.UnitId == unit.UnitId);
				var unitHours = entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Unit && e.DeploymentUnitId == unit.DeploymentUnitId && usable.ContainsKey(e.DeploymentTimeReportId)).Sum(e => Hours(e));
				var hours = unitHours > 0 ? unitHours : committedHours;
				snapshot.Vehicles.Add(new CalOesMarsF42Vehicle
				{
					Kind = string.Equals(profile?.ResourceKind, "Support", StringComparison.OrdinalIgnoreCase) ? "Support" : string.Equals(profile?.ResourceKind, "POV", StringComparison.OrdinalIgnoreCase) ? "POV" : "Apparatus",
					DeploymentUnitId = unit.DeploymentUnitId, ResourceProfileId = profile?.CalOesMarsResourceProfileId, Designator = profile?.UnitDesignator ?? unit.UnitName ?? unit.CallSign,
					ResourceCode = profile?.ResourceType, LicensePlate = profile?.LicensePlate, Vin = profile?.Vin, SerialNumber = profile?.SerialNumber,
					CommittedHours = Math.Round(hours, 2), CommittedDays = hours <= 0 ? 0 : Math.Ceiling(hours / 24m),
					Miles = entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Unit && e.DeploymentUnitId == unit.DeploymentUnitId && e.MileageKm.HasValue).Sum(e => e.MileageKm) is decimal km && km > 0 ? Math.Round(km * 0.621371m, 1) : null
				});
			}
			foreach (var equipment in deployment.Equipment.Where(e => !e.ReturnedOn.HasValue || e.ReturnedOn > dispatchOn))
			{
				var hours = entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Equipment && e.DeploymentEquipmentId == equipment.DeploymentEquipmentId && usable.ContainsKey(e.DeploymentTimeReportId)).Sum(e => Hours(e));
				snapshot.Vehicles.Add(new CalOesMarsF42Vehicle { Kind = "Equipment", DeploymentEquipmentId = equipment.DeploymentEquipmentId, Designator = equipment.FreeTextName ?? equipment.InventoryAssetId ?? equipment.InventoryItemId, CommittedHours = Math.Round(hours > 0 ? hours : committedHours, 2), CommittedDays = Math.Ceiling((hours > 0 ? hours : committedHours) / 24m) });
			}

			// Personnel: the roster (filtered to the fill when the roster is tagged), names from profiles, actual hours from usable DTRs.
			var roster = deployment.Personnel.Where(p => p.IsActive).ToList();
			if (fill != null && roster.Any(p => !string.IsNullOrWhiteSpace(p.RmsExternalOrderFillId))) roster = roster.Where(p => string.Equals(p.RmsExternalOrderFillId, fill.RmsExternalOrderFillId, StringComparison.OrdinalIgnoreCase)).ToList();
			var profiles = roster.Count == 0 ? new List<UserProfile>() : (await _userProfileService.GetSelectedUserProfilesAsync(roster.Select(p => p.UserId).Distinct().ToList()))?.ToList() ?? new List<UserProfile>();
			foreach (var member in roster)
			{
				var profile = profiles.FirstOrDefault(p => string.Equals(p.UserId, member.UserId, StringComparison.OrdinalIgnoreCase));
				var kept = previous?.Personnel.FirstOrDefault(p => string.Equals(p.DeploymentPersonnelId, member.DeploymentPersonnelId, StringComparison.OrdinalIgnoreCase));
				var person = new CalOesMarsF42Person
				{
					DeploymentPersonnelId = member.DeploymentPersonnelId, UserId = member.UserId, Name = member.DisplayName ?? profile?.FullName.AsFirstNameLastName ?? member.UserId,
					Rank = kept?.Rank ?? member.CertificationCode, ClassificationCode = kept?.ClassificationCode ?? member.CertificationCode,
					CommittedOn = kept?.CommittedOn ?? snapshot.CommittedOn, ReleasedOn = kept?.ReleasedOn ?? snapshot.ReturnedOn ?? snapshot.ReleasedOn
				};
				person.CommittedHours = Math.Round(Hours(person.CommittedOn, person.ReleasedOn), 2);
				person.ActualHours = entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Personnel && e.DeploymentPersonnelId == member.DeploymentPersonnelId && usable.ContainsKey(e.DeploymentTimeReportId))
					.GroupBy(e => new { e.StartTime.Date, e.DeploymentTimeReportId }).Select(g => new CalOesMarsDailyHours { Date = g.Key.Date, TimeReportId = g.Key.DeploymentTimeReportId, Hours = Math.Round(g.Sum(e => Hours(e)), 2) }).OrderBy(h => h.Date).ToList();
				snapshot.Personnel.Add(person);
			}
			snapshot.SourceTimeReportIds = usable.Keys.ToList();
			snapshot.AttachmentIds = (await _deploymentService.GetAttachmentsAsync(deploymentId, departmentId))?.Where(a => F42AttachmentTypes.Contains(a.AttachmentType)).Select(a => a.DeploymentAttachmentId).ToList() ?? new List<int>();

			var agreement = await SelectAgreementAsync(departmentId, null, dispatchOn);
			var effective = (await _rateProfiles.GetEffectiveAsync(departmentId, dispatchOn))?.ToList() ?? new List<CalOesMarsRateProfile>();
			var now = DateTime.UtcNow;
			CalOesMarsWorkItem target;
			if (current != null && !current.IsExternal) target = current;
			else
			{
				target = new CalOesMarsWorkItem { DepartmentId = departmentId, DeploymentId = deploymentId, RecordType = (int)CalOesMarsRecordTypes.F42, LocalState = (int)CalOesMarsLocalStates.Draft, AddedOn = now, AddedByUserId = userId, SupersedesWorkItemId = current?.CalOesMarsWorkItemId };
				// A redispatch closes the first resource/request interval on the earlier item; release is not return.
				if (current != null && previous != null && !previous.ReturnedOn.HasValue) { previous.ReturnedOn = previous.ReleasedOn; current.SnapshotJson = JsonConvert.SerializeObject(previous); current.EditedOn = now; current.EditedByUserId = userId; await _workItems.SaveOrUpdateAsync(current, cancellationToken); }
			}
			var before = ReferenceEquals(target, current) ? Snapshot(current) : null;
			target.RmsExternalOrderId = context?.Order?.RmsExternalOrderId ?? deployment.RmsExternalOrderId;
			target.RmsExternalOrderFillId = fill?.RmsExternalOrderFillId;
			target.AuthorityProfileCode = authority.Code;
			target.RateProfileVersion = RateVersion(effective);
			target.AgreementSnapshotId = agreement?.CalOesMarsAgreementSnapshotId;
			target.SnapshotJson = JsonConvert.SerializeObject(snapshot);
			target.SourceChecksum = Sha256(target.SnapshotJson);
			if (target.LocalState == (int)CalOesMarsLocalStates.ReadyForPortal) target.LocalState = (int)CalOesMarsLocalStates.NeedsReview;
			if (ReferenceEquals(target, current)) { target.RowVersion++; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _workItems.SaveOrUpdateAsync(target, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemPrepared, ipAddress, userAgent, before, saved);
			return await GetWorkItemAsync(saved.CalOesMarsWorkItemId, departmentId);
		}

		public async Task<CalOesMarsWorkItem> BuildExpenseClaimDraftAsync(string deploymentId, int departmentId, string f42WorkItemId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deploymentService.GetDeploymentByIdAsync(deploymentId, departmentId) ?? throw new InvalidOperationException("calmars_deployment_not_found");
			CalOesMarsWorkItem f42 = null;
			CalOesMarsF42Snapshot f42Snapshot = null;
			if (!string.IsNullOrWhiteSpace(f42WorkItemId))
			{
				f42 = await GetWorkItemAsync(f42WorkItemId, departmentId);
				if (f42 == null || f42.RecordType != (int)CalOesMarsRecordTypes.F42 || !string.Equals(f42.DeploymentId, deploymentId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("calmars_f42_not_found");
				f42Snapshot = Deserialize<CalOesMarsF42Snapshot>(f42.SnapshotJson);
			}
			var expenses = await _timeTracking.GetExpensesAsync(deploymentId, departmentId);
			if (f42 != null && !string.IsNullOrWhiteSpace(f42.RmsExternalOrderFillId) && expenses.Any(e => !string.IsNullOrWhiteSpace(e.RmsExternalOrderFillId)))
				expenses = expenses.Where(e => string.IsNullOrWhiteSpace(e.RmsExternalOrderFillId) || string.Equals(e.RmsExternalOrderFillId, f42.RmsExternalOrderFillId, StringComparison.OrdinalIgnoreCase)).ToList();
			var existing = (await _workItems.GetByDeploymentAsync(deploymentId, departmentId))?.Where(w => w.RecordType == (int)CalOesMarsRecordTypes.ExpenseClaim && !w.IsExternal && w.LocalState != (int)CalOesMarsLocalStates.Closed)
				.Select(w => new { Item = w, Snapshot = Deserialize<CalOesMarsExpenseClaimSnapshot>(w.SnapshotJson) })
				.FirstOrDefault(x => string.Equals(x.Snapshot?.F42WorkItemId ?? string.Empty, f42?.CalOesMarsWorkItemId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
			var dispatchOn = f42Snapshot?.DispatchedOn ?? deployment.StartOn ?? deployment.AddedOn;
			var authority = CalOesMarsAuthorityProfile.ForDispatch(dispatchOn) ?? CalOesMarsAuthorityProfile.Current;
			var snapshot = new CalOesMarsExpenseClaimSnapshot
			{
				AuthorityProfileCode = authority.Code, F42WorkItemId = f42?.CalOesMarsWorkItemId, RequestNumber = f42Snapshot?.RequestNumber ?? deployment.RequestNumber, IncidentNumber = f42Snapshot?.IncidentNumber ?? deployment.IncidentNumber,
				TravelOnly = f42 == null, SignerName = existing?.Snapshot?.SignerName, SignedOn = existing?.Snapshot?.SignedOn, ApproverName = existing?.Snapshot?.ApproverName, ApprovedOn = existing?.Snapshot?.ApprovedOn
			};
			foreach (var expense in expenses.OrderBy(e => e.ExpenseDate))
			{
				snapshot.Lines.Add(new CalOesMarsExpenseLine
				{
					DeploymentExpenseId = expense.DeploymentExpenseId, Date = expense.ExpenseDate, City = expense.City, Category = ExpenseCategory(expense.ExpenseType), Amount = expense.Amount,
					Description = expense.Description, ReceiptAttachmentId = expense.ReceiptAttachmentId, PreApproved = expense.PreApproved
				});
			}
			var now = DateTime.UtcNow;
			var target = existing?.Item ?? new CalOesMarsWorkItem { DepartmentId = departmentId, DeploymentId = deploymentId, RecordType = (int)CalOesMarsRecordTypes.ExpenseClaim, LocalState = (int)CalOesMarsLocalStates.Draft, AddedOn = now, AddedByUserId = userId };
			var before = existing == null ? null : Snapshot(existing.Item);
			target.RmsExternalOrderId = f42?.RmsExternalOrderId ?? deployment.RmsExternalOrderId;
			target.RmsExternalOrderFillId = f42?.RmsExternalOrderFillId;
			target.AuthorityProfileCode = authority.Code;
			target.RateProfileVersion = f42?.RateProfileVersion ?? RateVersion((await _rateProfiles.GetEffectiveAsync(departmentId, dispatchOn))?.ToList());
			target.AgreementSnapshotId = f42?.AgreementSnapshotId;
			target.SnapshotJson = JsonConvert.SerializeObject(snapshot);
			target.SourceChecksum = Sha256(target.SnapshotJson);
			if (target.LocalState == (int)CalOesMarsLocalStates.ReadyForPortal) target.LocalState = (int)CalOesMarsLocalStates.NeedsReview;
			if (existing != null) { target.RowVersion++; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _workItems.SaveOrUpdateAsync(target, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemPrepared, ipAddress, userAgent, before, saved);
			return await GetWorkItemAsync(saved.CalOesMarsWorkItemId, departmentId);
		}

		public static string ExpenseCategory(int expenseType) => (DeploymentExpenseTypes)expenseType switch
		{
			DeploymentExpenseTypes.PerDiemMeal => "Meal",
			DeploymentExpenseTypes.Accommodation => "Lodging",
			DeploymentExpenseTypes.PrivateAccommodation => "Lodging",
			_ => "Miscellaneous"
		};

		public async Task<CalOesMarsWorkItem> SaveF42SnapshotAsync(string workItemId, int departmentId, CalOesMarsF42Snapshot snapshot, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			var item = await RequireLocalAsync(workItemId, departmentId, CalOesMarsRecordTypes.F42);
			var current = Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson) ?? new CalOesMarsF42Snapshot();
			var before = Snapshot(item);
			// Only the locally authored boxes are editable; the projected facts come from the deployment / order / fill / DTRs.
			current.Comments = Trim(snapshot.Comments);
			current.LossDamage = Trim(snapshot.LossDamage);
			current.SupplyNumbers = Trim(snapshot.SupplyNumbers);
			current.StrikeTeamOrTaskForce = Trim(snapshot.StrikeTeamOrTaskForce) ?? current.StrikeTeamOrTaskForce;
			current.ReportingLocation = Trim(snapshot.ReportingLocation) ?? current.ReportingLocation;
			current.OverheadPosition = Trim(snapshot.OverheadPosition) ?? current.OverheadPosition;
			current.ResourceType = Trim(snapshot.ResourceType) ?? current.ResourceType;
			current.RespondingSignerName = Trim(snapshot.RespondingSignerName);
			current.RespondingSignedOn = string.IsNullOrWhiteSpace(current.RespondingSignerName) ? null : snapshot.RespondingSignedOn ?? current.RespondingSignedOn ?? DateTime.UtcNow;
			current.IncidentAuthorizerName = Trim(snapshot.IncidentAuthorizerName);
			current.IncidentAuthorizedOn = string.IsNullOrWhiteSpace(current.IncidentAuthorizerName) ? null : snapshot.IncidentAuthorizedOn ?? current.IncidentAuthorizedOn ?? DateTime.UtcNow;
			current.DocumentationOnly = snapshot.DocumentationOnly;
			current.Rotations = snapshot.Rotations?.Where(r => r != null).ToList() ?? new List<CalOesMarsF42Rotation>();
			if (snapshot.ReturnedOn.HasValue) current.ReturnedOn = snapshot.ReturnedOn;
			foreach (var edited in snapshot.Personnel ?? new List<CalOesMarsF42Person>())
			{
				var person = current.Personnel.FirstOrDefault(p => string.Equals(p.DeploymentPersonnelId, edited.DeploymentPersonnelId, StringComparison.OrdinalIgnoreCase));
				if (person == null) continue;
				person.Rank = Trim(edited.Rank) ?? person.Rank;
				person.ClassificationCode = Trim(edited.ClassificationCode) ?? person.ClassificationCode;
				if (edited.CommittedOn.HasValue) person.CommittedOn = edited.CommittedOn;
				if (edited.ReleasedOn.HasValue) person.ReleasedOn = edited.ReleasedOn;
				person.CommittedHours = Math.Round(Hours(person.CommittedOn, person.ReleasedOn), 2);
			}
			foreach (var edited in snapshot.Vehicles ?? new List<CalOesMarsF42Vehicle>())
			{
				var vehicle = current.Vehicles.FirstOrDefault(v => (!string.IsNullOrWhiteSpace(edited.DeploymentUnitId) && v.DeploymentUnitId == edited.DeploymentUnitId) || (!string.IsNullOrWhiteSpace(edited.DeploymentEquipmentId) && v.DeploymentEquipmentId == edited.DeploymentEquipmentId));
				if (vehicle == null) continue;
				vehicle.Kind = Trim(edited.Kind) ?? vehicle.Kind;
				vehicle.ResourceCode = Trim(edited.ResourceCode) ?? vehicle.ResourceCode;
				vehicle.FemaCode = Trim(edited.FemaCode);
				vehicle.StartOdometer = edited.StartOdometer; vehicle.EndOdometer = edited.EndOdometer;
				if (edited.Miles.HasValue) vehicle.Miles = edited.Miles;
				if (edited.CommittedHours > 0) { vehicle.CommittedHours = edited.CommittedHours; vehicle.CommittedDays = Math.Ceiling(edited.CommittedHours / 24m); }
			}
			item.SnapshotJson = JsonConvert.SerializeObject(current);
			item.SourceChecksum = Sha256(item.SnapshotJson);
			if (item.LocalState == (int)CalOesMarsLocalStates.ReadyForPortal) item.LocalState = (int)CalOesMarsLocalStates.NeedsReview;
			item.RowVersion++; item.EditedOn = DateTime.UtcNow; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemPrepared, ipAddress, userAgent, before, item);
			return await GetWorkItemAsync(workItemId, departmentId);
		}

		public async Task<CalOesMarsWorkItem> SaveExpenseSnapshotAsync(string workItemId, int departmentId, CalOesMarsExpenseClaimSnapshot snapshot, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			var item = await RequireLocalAsync(workItemId, departmentId, CalOesMarsRecordTypes.ExpenseClaim);
			var current = Deserialize<CalOesMarsExpenseClaimSnapshot>(item.SnapshotJson) ?? new CalOesMarsExpenseClaimSnapshot();
			var before = Snapshot(item);
			current.SignerName = Trim(snapshot.SignerName);
			current.SignedOn = string.IsNullOrWhiteSpace(current.SignerName) ? null : snapshot.SignedOn ?? current.SignedOn ?? DateTime.UtcNow;
			current.ApproverName = Trim(snapshot.ApproverName);
			current.ApprovedOn = string.IsNullOrWhiteSpace(current.ApproverName) ? null : snapshot.ApprovedOn ?? current.ApprovedOn ?? DateTime.UtcNow;
			current.TravelOnly = snapshot.TravelOnly;
			item.SnapshotJson = JsonConvert.SerializeObject(current);
			item.SourceChecksum = Sha256(item.SnapshotJson);
			if (item.LocalState == (int)CalOesMarsLocalStates.ReadyForPortal) item.LocalState = (int)CalOesMarsLocalStates.NeedsReview;
			item.RowVersion++; item.EditedOn = DateTime.UtcNow; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemPrepared, ipAddress, userAgent, before, item);
			return await GetWorkItemAsync(workItemId, departmentId);
		}

		#endregion

		#region Validation, calculation, handoff

		public async Task<CalOesMarsValidationResult> ValidateForPortalAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			var agency = await GetAgencyProfileAsync(departmentId);
			var authority = CalOesMarsAuthorityProfile.Get(item.AuthorityProfileCode);
			var result = new CalOesMarsValidationResult { WorkItemId = workItemId, AuthorityProfileCode = item.AuthorityProfileCode, ValidatedOn = DateTime.UtcNow };
			if (authority == null || !authority.IsReviewed) Error(result, "agency", CalOesMarsValidationCodes.AuthorityProfileMissing, item.AuthorityProfileCode);
			if (agency == null) Error(result, "agency", CalOesMarsValidationCodes.AgencyProfileMissing, null);
			else if (string.IsNullOrWhiteSpace(agency.MacsDesignator)) Error(result, "agency", CalOesMarsValidationCodes.MacsMissing, null);

			switch ((CalOesMarsRecordTypes)item.RecordType)
			{
				case CalOesMarsRecordTypes.F42:
					{
						var s = Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson) ?? new CalOesMarsF42Snapshot();
						var attachments = (await _deploymentService.GetAttachmentsAsync(item.DeploymentId, departmentId)) ?? new List<DeploymentAttachment>();
						ValidateF42(result, s, authority ?? CalOesMarsAuthorityProfile.Current, attachments, item);
						var crosswalk = (await _resources.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<CalOesMarsResourceProfile>();
						foreach (var vehicle in s.Vehicles.Where(v => v.Kind != "Equipment" && (string.IsNullOrWhiteSpace(v.ResourceProfileId) || crosswalk.All(c => c.CalOesMarsResourceProfileId != v.ResourceProfileId))))
							Warn(result, "apparatus", CalOesMarsValidationCodes.VehicleNotInInventory, vehicle.Designator);
						var effectiveLines = await EffectiveRateLinesAsync(departmentId, s.DispatchedOn ?? item.AddedOn);
						if (effectiveLines.Count == 0) Warn(result, "personnel", CalOesMarsValidationCodes.RateProfileMissing, (s.DispatchedOn ?? item.AddedOn).ToString("yyyy-MM-dd"));
						foreach (var person in s.Personnel.Where(p => !string.IsNullOrWhiteSpace(p.ClassificationCode) && !(authority ?? CalOesMarsAuthorityProfile.Current).SalaryClassifications.Contains(p.ClassificationCode, StringComparer.OrdinalIgnoreCase) && effectiveLines.All(l => !string.Equals(l.ClassificationCode, p.ClassificationCode, StringComparison.OrdinalIgnoreCase))))
							Warn(result, "personnel", CalOesMarsValidationCodes.ClassificationUnmapped, $"{person.Name}: {person.ClassificationCode}");
						if (string.IsNullOrWhiteSpace(item.AgreementSnapshotId) || await GetAgreementAsync(item.AgreementSnapshotId, departmentId) == null) Error(result, "personnel", CalOesMarsValidationCodes.AgreementMissing, null);
						break;
					}
				case CalOesMarsRecordTypes.ExpenseClaim:
					{
						var s = Deserialize<CalOesMarsExpenseClaimSnapshot>(item.SnapshotJson) ?? new CalOesMarsExpenseClaimSnapshot();
						if (s.Lines.Count == 0) Error(result, "lines", CalOesMarsValidationCodes.ExpenseNoLines, null);
						foreach (var line in s.Lines.Where(l => !l.ReceiptAttachmentId.HasValue))
						{
							if (string.Equals(line.Category, "Meal", StringComparison.OrdinalIgnoreCase)) Warn(result, "lines", CalOesMarsValidationCodes.ExpenseReceiptMissing, $"{line.Date:yyyy-MM-dd} {line.Category} {line.Amount:N2}");
							else Error(result, "lines", CalOesMarsValidationCodes.ExpenseReceiptMissing, $"{line.Date:yyyy-MM-dd} {line.Category} {line.Amount:N2}");
						}
						if (!s.TravelOnly)
						{
							var f42 = string.IsNullOrWhiteSpace(s.F42WorkItemId) ? null : await _workItems.GetByIdForDepartmentAsync(s.F42WorkItemId, departmentId);
							if (f42 == null || !f42.IsExternal) Error(result, "resource", CalOesMarsValidationCodes.ExpenseF42NotSubmitted, s.RequestNumber);
						}
						if (string.IsNullOrWhiteSpace(s.SignerName)) Error(result, "signature", CalOesMarsValidationCodes.ExpenseSignatureMissing, null);
						if (string.IsNullOrWhiteSpace(s.ApproverName)) Warn(result, "signature", CalOesMarsValidationCodes.ExpenseApprovalMissing, null);
						break;
					}
				default:
					break;
			}

			var before = Snapshot(item);
			item.ValidationSummaryJson = JsonConvert.SerializeObject(result);
			if (item.RecordType is (int)CalOesMarsRecordTypes.F42 or (int)CalOesMarsRecordTypes.ExpenseClaim && item.IsLocallyEditable)
				item.LocalState = result.IsReadyForPortal ? (int)CalOesMarsLocalStates.ReadyForPortal : (item.LocalState == (int)CalOesMarsLocalStates.ReturnedForAgencyReview ? item.LocalState : (int)CalOesMarsLocalStates.NeedsReview);
			item.RowVersion++; item.EditedOn = DateTime.UtcNow; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemValidated, ipAddress, userAgent, before, item);
			return result;
		}

		/// <summary>The F-42 checklist (pure; the authority profile's boxes and prefixes drive it).</summary>
		public static void ValidateF42(CalOesMarsValidationResult result, CalOesMarsF42Snapshot s, CalOesMarsAuthorityProfile authority, IReadOnlyCollection<DeploymentAttachment> attachments, CalOesMarsWorkItem item = null)
		{
			if (string.IsNullOrWhiteSpace(s.IncidentNumber) && string.IsNullOrWhiteSpace(s.IncidentName)) Error(result, "incident", CalOesMarsValidationCodes.IncidentMissing, null);
			if (string.IsNullOrWhiteSpace(s.OrderNumber)) Error(result, "order", CalOesMarsValidationCodes.OrderMissing, null);
			if (string.IsNullOrWhiteSpace(s.RequestNumber)) Error(result, "request", CalOesMarsValidationCodes.RequestMissing, null);
			else if (!IsValidRequestNumber(s.RequestNumber, authority)) Error(result, "request", CalOesMarsValidationCodes.RequestPrefixInvalid, s.RequestNumber);
			if (string.IsNullOrWhiteSpace(s.ResourceType) && string.IsNullOrWhiteSpace(s.ResourceKind) && string.IsNullOrWhiteSpace(s.OverheadPosition)) Error(result, "resource", CalOesMarsValidationCodes.ResourceMissing, null);
			if (!s.DispatchedOn.HasValue) Error(result, "dispatch", CalOesMarsValidationCodes.DispatchMissing, null);
			if (s.DispatchedOn.HasValue && s.ReturnedOn.HasValue && s.ReturnedOn < s.DispatchedOn) Error(result, "return", CalOesMarsValidationCodes.ReturnBeforeDispatch, $"{s.DispatchedOn:g} → {s.ReturnedOn:g}");
			if (!s.ReturnedOn.HasValue && s.ReleasedOn.HasValue) Warn(result, "return", CalOesMarsValidationCodes.ReleaseIsNotReturn, s.ReleasedOn?.ToString("g"));
			if (s.Personnel.Count == 0 && string.IsNullOrWhiteSpace(s.OverheadPosition)) Error(result, "personnel", CalOesMarsValidationCodes.PersonnelMissing, null);
			var windowStart = s.DispatchedOn; var windowEnd = s.ReturnedOn ?? s.ReleasedOn;
			foreach (var person in s.Personnel)
			{
				if ((windowStart.HasValue && person.CommittedOn.HasValue && person.CommittedOn < windowStart.Value.AddHours(-1)) || (windowEnd.HasValue && person.ReleasedOn.HasValue && person.ReleasedOn > windowEnd.Value.AddHours(1)))
					Warn(result, "personnel", CalOesMarsValidationCodes.PersonnelIntervalOutside, person.Name);
			}
			var duplicates = s.Vehicles.GroupBy(v => (v.LicensePlate ?? v.Vin ?? v.Designator ?? string.Empty).Trim().ToUpperInvariant()).Where(g => g.Key.Length > 0 && g.Count() > 1).Select(g => g.Key).ToList();
			foreach (var duplicate in duplicates) Error(result, "apparatus", CalOesMarsValidationCodes.DuplicateVehicle, duplicate);
			foreach (var rotation in s.Rotations.Where(r => !r.ApprovalAttachmentId.HasValue || attachments.All(a => a.DeploymentAttachmentId != r.ApprovalAttachmentId.Value)))
				Error(result, "rotation", CalOesMarsValidationCodes.RotationUndocumented, rotation.On.ToString("yyyy-MM-dd"));
			if (string.IsNullOrWhiteSpace(s.RespondingSignerName)) Error(result, "responding-signature", CalOesMarsValidationCodes.RespondingSignatureMissing, null);
			if (string.IsNullOrWhiteSpace(s.IncidentAuthorizerName))
			{
				if (s.DocumentationOnly) Warn(result, "incident-signature", CalOesMarsValidationCodes.IncidentAuthorizationMissing, null);
				else Error(result, "incident-signature", CalOesMarsValidationCodes.IncidentAuthorizationMissing, null);
			}
			if (!attachments.Any(a => (a.AttachmentType == (int)DeploymentAttachmentTypes.SignedF42 || a.AttachmentType == (int)DeploymentAttachmentTypes.PaperF42) && (s.AttachmentIds.Count == 0 || s.AttachmentIds.Contains(a.DeploymentAttachmentId))))
				Error(result, "attachments", CalOesMarsValidationCodes.PaperFallbackMissing, null);
			if (s.DocumentationOnly) Warn(result, "comments", CalOesMarsValidationCodes.DocumentationOnly, null);
		}

		public static bool IsValidRequestNumber(string requestNumber, CalOesMarsAuthorityProfile authority)
		{
			var value = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
			if (value.Length < 2) return false;
			var prefix = value.Substring(0, 1);
			if (!authority.RequestPrefixes.Contains(prefix)) return false;
			var rest = value.Substring(1).TrimStart('-', ' ');
			return rest.Length > 0 && rest.All(c => char.IsDigit(c) || c == '.' || c == '-');
		}

		public async Task<CalOesMarsReimbursementResult> CalculateExpectedReimbursementAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (item.RecordType is not ((int)CalOesMarsRecordTypes.F42 or (int)CalOesMarsRecordTypes.ExpenseClaim)) throw new InvalidOperationException("calmars_work_item_not_calculable");
			if (item.LocalState is (int)CalOesMarsLocalStates.Paid or (int)CalOesMarsLocalStates.Closed) throw new InvalidOperationException("calmars_work_item_settled");
			var input = new CalOesMarsReimbursementInput { RateProfileVersion = item.RateProfileVersion };
			DateTime dispatchOn;
			if (item.RecordType == (int)CalOesMarsRecordTypes.F42)
			{
				input.F42 = Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson) ?? new CalOesMarsF42Snapshot();
				dispatchOn = input.F42.DispatchedOn ?? item.AddedOn;
			}
			else
			{
				input.Expenses = Deserialize<CalOesMarsExpenseClaimSnapshot>(item.SnapshotJson) ?? new CalOesMarsExpenseClaimSnapshot();
				var f42 = string.IsNullOrWhiteSpace(input.Expenses.F42WorkItemId) ? null : await _workItems.GetByIdForDepartmentAsync(input.Expenses.F42WorkItemId, departmentId);
				dispatchOn = Deserialize<CalOesMarsF42Snapshot>(f42?.SnapshotJson)?.DispatchedOn ?? item.AddedOn;
			}
			// Everything is selected as of initial dispatch (decision 37): a later rate letter never rewrites an earlier claim.
			var effective = (await _rateProfiles.GetEffectiveAsync(departmentId, dispatchOn))?.ToList() ?? new List<CalOesMarsRateProfile>();
			input.RateLines = (await _rateLines.GetByProfilesAsync(effective.Select(p => p.CalOesMarsRateProfileId)))?.ToList() ?? new List<CalOesMarsRateLine>();
			input.Agreement = string.IsNullOrWhiteSpace(item.AgreementSnapshotId) ? await SelectAgreementAsync(departmentId, null, dispatchOn) : await GetAgreementAsync(item.AgreementSnapshotId, departmentId);
			var administrative = effective.Where(p => p.AdministrativeRateMethod != (int)CalOesMarsAdministrativeRateMethods.None && p.AdministrativeRateValue.HasValue).OrderByDescending(p => p.SubmissionType == (int)CalOesMarsSubmissionTypes.AdministrativeRate).ThenByDescending(p => p.EffectiveOn).FirstOrDefault();
			input.AdministrativeRatePercent = administrative?.AdministrativeRateValue;
			if (string.IsNullOrWhiteSpace(input.RateProfileVersion)) input.RateProfileVersion = RateVersion(effective);

			var result = _calculator.Calculate(input);
			var before = Snapshot(item);
			await TransactionAsync(async () =>
			{
				await _lines.DeleteByWorkItemAsync(workItemId, cancellationToken);
				var now = DateTime.UtcNow;
				foreach (var line in result.Lines)
				{
					line.CalOesMarsReimbursementLineId = null;
					line.CalOesMarsWorkItemId = workItemId;
					line.DepartmentId = departmentId;
					line.DeploymentId = item.DeploymentId;
					line.AddedOn = now;
					line.AddedByUserId = userId;
					await _lines.SaveOrUpdateAsync(line, cancellationToken);
				}
				item.ExpectedTotal = result.ExpectedTotal;
				item.RateProfileVersion = input.RateProfileVersion;
				item.AgreementSnapshotId ??= input.Agreement?.CalOesMarsAgreementSnapshotId;
				item.RowVersion++; item.EditedOn = now; item.EditedByUserId = userId;
				await _workItems.SaveOrUpdateAsync(item, cancellationToken);
				return true;
			}, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsReimbursementCalculated, ipAddress, userAgent, before, item);
			return result;
		}

		public async Task<CalOesMarsHandoffManifest> OpenPortalHandoffAsync(string workItemId, int departmentId, bool attested, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (Config.CostRecoveryConfig.HandoffAttestationRequired && !attested) throw new InvalidOperationException("calmars_attestation_required");
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			var validation = Deserialize<CalOesMarsValidationResult>(item.ValidationSummaryJson);
			if (item.RecordType is (int)CalOesMarsRecordTypes.F42 or (int)CalOesMarsRecordTypes.ExpenseClaim && (validation == null || !validation.IsReadyForPortal) && !item.IsExternal) throw new InvalidOperationException("calmars_not_ready");
			var manifest = await BuildManifestAsync(item, departmentId, userId);
			manifest.Validation = validation;
			// Opening the handoff is audited and never changes the mirror state (plan C4: "Prepared for MARS", not "Submitted").
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemOpenedForHandoff, ipAddress, userAgent, null, new { item.CalOesMarsWorkItemId, item.RecordType, item.LocalState, manifest.Checksum, attested });
			return manifest;
		}

		private async Task<CalOesMarsHandoffManifest> BuildManifestAsync(CalOesMarsWorkItem item, int departmentId, string userId)
		{
			var authority = CalOesMarsAuthorityProfile.Get(item.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current;
            var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false) ?? new Department();
            string When(DateTime? value) => value?.TimeConverterToString(department);
			var manifest = new CalOesMarsHandoffManifest
			{
				WorkItemId = item.CalOesMarsWorkItemId, RecordType = item.RecordType, AuthorityProfileCode = item.AuthorityProfileCode, RateProfileVersion = item.RateProfileVersion, AgreementSnapshotId = item.AgreementSnapshotId,
				GeneratedOn = DateTime.UtcNow, GeneratedByUserId = userId, PortalUrl = _gateway.GetPortalUrl(item)
			};
			switch ((CalOesMarsRecordTypes)item.RecordType)
			{
				case CalOesMarsRecordTypes.F42:
					{
						var s = Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson) ?? new CalOesMarsF42Snapshot();
						var values = new Dictionary<string, string>
						{
							["agency"] = Join(s.MacsDesignator, s.AgencyName), ["incident"] = Join(s.IncidentName, s.IncidentNumber), ["order"] = s.OrderNumber, ["request"] = Join(s.RequestNumber, s.ParentRequestNumber),
							["resource"] = Join(s.ResourceType ?? s.ResourceKind, s.StrikeTeamOrTaskForce, s.OverheadPosition), ["dispatch"] = When(s.DispatchedOn),
							["return"] = When(s.ReturnedOn) ?? (s.ReleasedOn.HasValue ? "Released " + When(s.ReleasedOn) : null),
							["apparatus"] = string.Join("; ", s.Vehicles.Select(v => $"{v.Kind} {v.Designator} {v.ResourceCode} {v.LicensePlate} {v.CommittedHours}h".Trim())),
							["personnel"] = string.Join("; ", s.Personnel.Select(p => $"{p.Name} ({p.Rank ?? p.ClassificationCode}) {(p.ActualHours.Count > 0 ? p.ActualHours.Sum(h => h.Hours) + "h actual" : p.CommittedHours + "h committed")}")),
							["rotation"] = s.Rotations.Count == 0 ? null : string.Join("; ", s.Rotations.Select(r => r.On.ToString("yyyy-MM-dd"))),
							["comments"] = Join(s.Comments, s.LossDamage, s.SupplyNumbers), ["responding-signature"] = Join(s.RespondingSignerName, When(s.RespondingSignedOn)),
							["incident-signature"] = Join(s.IncidentAuthorizerName, When(s.IncidentAuthorizedOn)), ["attachments"] = s.AttachmentIds.Count.ToString()
						};
						foreach (var box in authority.F42Boxes) manifest.Fields.Add(new CalOesMarsHandoffField { Box = box.Id, Label = box.Label, Value = values.TryGetValue(box.Id, out var v) ? v : null, Source = box.Source });
						if (!string.IsNullOrWhiteSpace(item.DeploymentId))
							manifest.SupportingAttachmentNames = (await _deploymentService.GetAttachmentsAsync(item.DeploymentId, departmentId))?.Where(a => s.AttachmentIds.Contains(a.DeploymentAttachmentId)).Select(a => a.FileName ?? a.Name).ToList() ?? new List<string>();
						break;
					}
				case CalOesMarsRecordTypes.ExpenseClaim:
					{
						var s = Deserialize<CalOesMarsExpenseClaimSnapshot>(item.SnapshotJson) ?? new CalOesMarsExpenseClaimSnapshot();
						manifest.Fields.Add(new CalOesMarsHandoffField { Box = "resource", Label = "Resource / F-42", Value = s.TravelOnly ? "Travel only (unmatched)" : Join(s.RequestNumber, s.IncidentNumber), Source = "Snapshot" });
						foreach (var line in s.Lines) manifest.Fields.Add(new CalOesMarsHandoffField { Box = "line", Label = $"{line.Date:yyyy-MM-dd} {line.Category}", Value = $"{line.City} {line.Amount:N2} {line.Description}".Trim(), Source = "DeploymentExpense:" + line.DeploymentExpenseId });
						manifest.Fields.Add(new CalOesMarsHandoffField { Box = "signature", Label = "Signature / approval", Value = Join(s.SignerName, s.ApproverName), Source = "Snapshot" });
						break;
					}
				case CalOesMarsRecordTypes.GeneratedInvoice:
					{
						var s = Deserialize<CalOesMarsInvoiceSnapshot>(item.SnapshotJson) ?? new CalOesMarsInvoiceSnapshot();
						manifest.Fields.Add(new CalOesMarsHandoffField { Box = "invoice", Label = "MARS invoice", Value = Join(s.MarsInvoiceId, s.InvoiceDate?.ToString("yyyy-MM-dd"), s.InvoicedTotal?.ToString("N2")), Source = "Observed" });
						break;
					}
			}
			manifest.Checksum = Sha256(JsonConvert.SerializeObject(manifest.Fields) + item.SourceChecksum);
			return manifest;
		}

		public async Task<byte[]> BuildEvidencePacketAsync(string workItemId, int departmentId, string userId)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			var manifest = await BuildManifestAsync(item, departmentId, userId);
			manifest.Validation = Deserialize<CalOesMarsValidationResult>(item.ValidationSummaryJson);
			using var stream = new MemoryStream();
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
			{
				Add(zip, "README.txt", Encoding.UTF8.GetBytes("Resgrid Cal OES MARS evidence packet.\r\nThis is NOT an accepted MARS import file. It carries the prepared record, its checklist result, source and checksum metadata and the supporting documents for manual entry in the MARS portal.\r\n" +
					$"Work item: {item.CalOesMarsWorkItemId}\r\nRecord type: {(CalOesMarsRecordTypes)item.RecordType}\r\nAuthority profile: {item.AuthorityProfileCode}\r\nRate profile version: {item.RateProfileVersion}\r\nAgreement snapshot: {item.AgreementSnapshotId}\r\nSnapshot checksum: {item.SourceChecksum}\r\nManifest checksum: {manifest.Checksum}\r\nGenerated: {manifest.GeneratedOn:u}\r\n"));
				Add(zip, "manifest.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.Indented)));
				Add(zip, "snapshot.json", Encoding.UTF8.GetBytes(item.SnapshotJson ?? "{}"));
				Add(zip, "record.html", Encoding.UTF8.GetBytes(await RenderWorkItemHtmlAsync(workItemId, departmentId)));
				if (!string.IsNullOrWhiteSpace(item.DeploymentId))
				{
					var ids = Deserialize<CalOesMarsF42Snapshot>(item.RecordType == (int)CalOesMarsRecordTypes.F42 ? item.SnapshotJson : null)?.AttachmentIds
						?? Deserialize<CalOesMarsExpenseClaimSnapshot>(item.RecordType == (int)CalOesMarsRecordTypes.ExpenseClaim ? item.SnapshotJson : null)?.Lines.Where(l => l.ReceiptAttachmentId.HasValue).Select(l => l.ReceiptAttachmentId.Value).Distinct().ToList()
						?? new List<int>();
					foreach (var id in ids)
					{
						var attachment = await _deploymentService.GetAttachmentAsync(id, departmentId, true);
						if (attachment?.Data == null || attachment.Data.Length == 0) continue;
						Add(zip, "supporting/" + SafeName(attachment.FileName ?? attachment.Name ?? $"attachment-{id}"), attachment.Data);
					}
				}
			}
			return stream.ToArray();
		}

		private static void Add(ZipArchive zip, string path, byte[] data)
		{
			var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
			using var target = entry.Open();
			target.Write(data, 0, data.Length);
		}

		private static string SafeName(string name) => string.Concat((name ?? "file").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

		public async Task<string> RenderWorkItemHtmlAsync(string workItemId, int departmentId)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			var manifest = await BuildManifestAsync(item, departmentId, null);
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Resgrid | Prepared for MARS</title><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}h1{font-size:18px}h2{font-size:14px;margin-top:18px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:4px 6px;text-align:left;vertical-align:top}.muted{color:#777}.right{text-align:right}</style></head><body>");
			sb.Append("<h1>Prepared for Cal OES MARS — ").Append(WebUtility.HtmlEncode(((CalOesMarsRecordTypes)item.RecordType).ToString())).Append("</h1>");
			sb.Append("<p class=\"muted\">Authority profile ").Append(WebUtility.HtmlEncode(item.AuthorityProfileCode ?? "—")).Append(" · rate profile ").Append(WebUtility.HtmlEncode(item.RateProfileVersion ?? "—")).Append(" · checksum ").Append(WebUtility.HtmlEncode(manifest.Checksum)).Append(" · not an accepted MARS import file</p>");
			sb.Append("<table><thead><tr><th>Box</th><th>Value</th><th>Source</th></tr></thead><tbody>");
			foreach (var field in manifest.Fields) sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(field.Label)).Append("</td><td>").Append(WebUtility.HtmlEncode(field.Value ?? "")).Append("</td><td class=\"muted\">").Append(WebUtility.HtmlEncode(field.Source ?? "")).Append("</td></tr>");
			sb.Append("</tbody></table>");
			if (item.Lines.Count > 0)
			{
				sb.Append("<h2>Expected reimbursement (estimate; Cal OES determines the allowed amount)</h2><table><thead><tr><th>Kind</th><th>Subject</th><th class=\"right\">Qty</th><th>Unit</th><th class=\"right\">Rate</th><th class=\"right\">Expected</th><th>Eligibility</th></tr></thead><tbody>");
				foreach (var line in item.Lines)
					sb.Append("<tr><td>").Append((CalOesMarsLineKinds)line.LineKind).Append("</td><td>").Append(WebUtility.HtmlEncode(line.SubjectName ?? line.SubjectId ?? "")).Append("</td><td class=\"right\">").Append(line.Quantity.ToString("0.##")).Append("</td><td>").Append(WebUtility.HtmlEncode(line.Unit ?? "")).Append("</td><td class=\"right\">").Append(line.Rate.ToString("N2")).Append("</td><td class=\"right\">").Append(line.ExpectedAmount.ToString("N2")).Append("</td><td>").Append((CalOesMarsEligibilityStates)line.EligibilityState).Append(string.IsNullOrWhiteSpace(line.EligibilityReason) ? "" : " · " + WebUtility.HtmlEncode(line.EligibilityReason)).Append("</td></tr>");
				sb.Append("<tr><td colspan=\"5\" class=\"right\"><strong>Expected total</strong></td><td class=\"right\"><strong>").Append((item.ExpectedTotal ?? 0).ToString("N2")).Append("</strong></td><td></td></tr></tbody></table>");
			}
			sb.Append("</body></html>");
			return sb.ToString();
		}

		#endregion

		#region External observation and reconciliation

		public async Task<CalOesMarsWorkItem> RecordExternalSubmissionAsync(string workItemId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (item.RecordType is not ((int)CalOesMarsRecordTypes.F42 or (int)CalOesMarsRecordTypes.ExpenseClaim)) throw new InvalidOperationException("calmars_work_item_not_submittable");
			if (item.LocalState != (int)CalOesMarsLocalStates.ReadyForPortal) throw new InvalidOperationException("calmars_not_ready");
			var before = Snapshot(item);
			var now = DateTime.UtcNow;
			item.LocalState = (int)CalOesMarsLocalStates.SubmittedExternal;
			item.MarsRecordId = Trim(observation.ExternalId);
			item.ObservedExternalStatus = Trim(observation.ExternalStatus) ?? "Cal OES Review";
			item.ObservedOn = observation.ObservedOn ?? now;
			item.ObservedSource = Trim(observation.Source) ?? CalOesMarsObservationSources.Manual;
			item.SubmittedByUserId = userId; item.SubmittedOn = now;
			item.SourceArtifact = observation.ArtifactAttachmentId?.ToString() ?? item.SourceArtifact;
			item.RowVersion++; item.EditedOn = now; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsExternalStatusObserved, ipAddress, userAgent, before, item);
			return item;
		}

		public async Task<CalOesMarsWorkItem> RecordExternalStatusAsync(string workItemId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (!item.IsExternal || item.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice) throw new InvalidOperationException("calmars_work_item_not_external");
			var authority = CalOesMarsAuthorityProfile.Get(item.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current;
			var mapped = authority.MapRecordStatus(observation.ExternalStatus) ?? throw new InvalidOperationException("calmars_status_unknown");
			var before = Snapshot(item);
			var now = DateTime.UtcNow;
			item.ObservedExternalStatus = observation.ExternalStatus.Trim();
			item.ObservedOn = observation.ObservedOn ?? now;
			item.ObservedSource = Trim(observation.Source) ?? CalOesMarsObservationSources.Manual;
			item.MarsRecordId = Trim(observation.ExternalId) ?? item.MarsRecordId;
			item.CorrectionComment = Trim(observation.Comment) ?? item.CorrectionComment;
			CalOesMarsWorkItem revision = null;
			if (mapped == CalOesMarsLocalStates.ReturnedForAgencyReview)
			{
				// A returned record closes this revision and opens a new local one carrying the reviewer's comment.
				item.LocalState = (int)CalOesMarsLocalStates.Closed;
				revision = new CalOesMarsWorkItem
				{
					DepartmentId = departmentId, DeploymentId = item.DeploymentId, RmsExternalOrderId = item.RmsExternalOrderId, RmsExternalOrderFillId = item.RmsExternalOrderFillId, RecordType = item.RecordType,
					LocalState = (int)CalOesMarsLocalStates.ReturnedForAgencyReview, MarsRecordId = item.MarsRecordId, ObservedExternalStatus = item.ObservedExternalStatus, ObservedOn = item.ObservedOn, ObservedSource = item.ObservedSource,
					CorrectionComment = Trim(observation.Comment), AuthorityProfileCode = item.AuthorityProfileCode, RateProfileVersion = item.RateProfileVersion, AgreementSnapshotId = item.AgreementSnapshotId,
					SnapshotJson = item.SnapshotJson, SourceChecksum = item.SourceChecksum, ExpectedTotal = item.ExpectedTotal, SupersedesWorkItemId = item.CalOesMarsWorkItemId, AddedOn = now, AddedByUserId = userId
				};
			}
			else
			{
				item.LocalState = (int)mapped;
				if (mapped == CalOesMarsLocalStates.Approved) { item.ApprovedOn = item.ObservedOn; item.ApprovedByUserId = userId; }
			}
			item.RowVersion++; item.EditedOn = now; item.EditedByUserId = userId;
			await TransactionAsync(async () =>
			{
				await _workItems.SaveOrUpdateAsync(item, cancellationToken);
				if (revision != null)
				{
					revision = await _workItems.SaveOrUpdateAsync(revision, cancellationToken);
					var sort = 0;
					foreach (var line in item.Lines)
					{
						var copy = line.CloneJson(); copy.CalOesMarsReimbursementLineId = null; copy.CalOesMarsWorkItemId = revision.CalOesMarsWorkItemId; copy.SortOrder = sort++; copy.AddedOn = now; copy.AddedByUserId = userId;
						await _lines.SaveOrUpdateAsync(copy, cancellationToken);
					}
				}
				return true;
			}, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsExternalStatusObserved, ipAddress, userAgent, before, item);
			return revision == null ? item : await GetWorkItemAsync(revision.CalOesMarsWorkItemId, departmentId);
		}

		public async Task<CalOesMarsWorkItem> RecordMarsInvoiceAsync(int departmentId, string deploymentId, CalOesMarsInvoiceObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			if (string.IsNullOrWhiteSpace(observation.MarsInvoiceId)) throw new InvalidOperationException("calmars_invoice_id_required");
			if (observation.InvoicedTotal < 0) throw new InvalidOperationException("calmars_invoice_amount_invalid");
			if ((await _workItems.GetByExternalIdAsync(departmentId, observation.MarsInvoiceId.Trim()))?.Any(w => w.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice) == true) throw new InvalidOperationException("calmars_invoice_duplicate");
			var covered = new List<CalOesMarsWorkItem>();
			foreach (var id in (observation.CoveredWorkItemIds ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct())
			{
				var item = await _workItems.GetByIdForDepartmentAsync(id, departmentId);
				if (item == null || item.IsDeleted || item.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice) throw new InvalidOperationException("calmars_work_item_not_found");
				if (!item.IsExternal) throw new InvalidOperationException("calmars_work_item_not_external");
				covered.Add(item);
			}
			var authority = CalOesMarsAuthorityProfile.Current;
			var now = DateTime.UtcNow;
			var snapshot = new CalOesMarsInvoiceSnapshot { MarsInvoiceId = observation.MarsInvoiceId.Trim(), InvoiceDate = observation.InvoiceDate, InvoicedTotal = observation.InvoicedTotal, PayingEntity = Trim(observation.PayingEntity), CoveredWorkItemIds = covered.Select(c => c.CalOesMarsWorkItemId).ToList(), InvoiceAttachmentId = observation.InvoiceAttachmentId };
			// A MARS invoice is a work item, never a Phase B Invoice (decision 36): it takes no invoice number, joins no aging and sends no e-mail.
			var invoice = new CalOesMarsWorkItem
			{
				DepartmentId = departmentId, DeploymentId = Trim(deploymentId) ?? covered.FirstOrDefault()?.DeploymentId, RmsExternalOrderId = covered.FirstOrDefault()?.RmsExternalOrderId, RecordType = (int)CalOesMarsRecordTypes.GeneratedInvoice,
				LocalState = (int)(authority.MapInvoiceStatus(observation.ExternalStatus) ?? CalOesMarsLocalStates.PendingLocalAgencyApproval), MarsInvoiceId = snapshot.MarsInvoiceId,
				ObservedExternalStatus = Trim(observation.ExternalStatus) ?? "Pending Local Agency Approval", ObservedOn = observation.ObservedOn ?? now, ObservedSource = CalOesMarsObservationSources.Manual,
				AuthorityProfileCode = authority.Code, SnapshotJson = JsonConvert.SerializeObject(snapshot), ExpectedTotal = covered.Sum(c => c.ExpectedTotal ?? 0), ApprovedTotal = observation.InvoicedTotal,
				CorrectionComment = Trim(observation.Comment), SourceArtifact = observation.InvoiceAttachmentId?.ToString(), AddedOn = now, AddedByUserId = userId
			};
			invoice.SourceChecksum = Sha256(invoice.SnapshotJson);
			var saved = await _workItems.SaveOrUpdateAsync(invoice, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsExternalStatusObserved, ipAddress, userAgent, null, saved);
			return await GetWorkItemAsync(saved.CalOesMarsWorkItemId, departmentId);
		}

		public async Task<CalOesMarsWorkItem> ApproveOrRejectObservedInvoiceAsync(string invoiceWorkItemId, int departmentId, bool approve, string decisionTitle, string comment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await GetWorkItemAsync(invoiceWorkItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (invoice.RecordType != (int)CalOesMarsRecordTypes.GeneratedInvoice) throw new InvalidOperationException("calmars_work_item_not_invoice");
			if (invoice.LocalState != (int)CalOesMarsLocalStates.PendingLocalAgencyApproval) throw new InvalidOperationException("calmars_invoice_not_pending");
			if (string.IsNullOrWhiteSpace(decisionTitle)) throw new InvalidOperationException("calmars_decision_title_required");
			if (!approve && string.IsNullOrWhiteSpace(comment)) throw new InvalidOperationException("calmars_rejection_comment_required");
			var before = Snapshot(invoice);
			var snapshot = Deserialize<CalOesMarsInvoiceSnapshot>(invoice.SnapshotJson) ?? new CalOesMarsInvoiceSnapshot();
			snapshot.LocalDecisionTitle = decisionTitle.Trim();
			snapshot.LocalDecisionComment = Trim(comment);
			var now = DateTime.UtcNow;
			invoice.SnapshotJson = JsonConvert.SerializeObject(snapshot);
			invoice.CorrectionComment = Trim(comment);
			if (approve) { invoice.LocalState = (int)CalOesMarsLocalStates.PendingPayingEntityApproval; invoice.ApprovedByUserId = userId; invoice.ApprovedOn = now; }
			else { invoice.LocalState = (int)CalOesMarsLocalStates.LocalAgencyRejected; invoice.RejectedByUserId = userId; invoice.RejectedOn = now; }
			invoice.RowVersion++; invoice.EditedOn = now; invoice.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(invoice, cancellationToken);
			Audit(departmentId, userId, approve ? AuditLogTypes.CalOesMarsInvoiceApproved : AuditLogTypes.CalOesMarsInvoiceRejected, ipAddress, userAgent, before, invoice);
			return invoice;
		}

		public async Task<CalOesMarsWorkItem> RecordPaymentAsync(string invoiceWorkItemId, int departmentId, CalOesMarsPaymentObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			if (observation.PaidTotal < 0) throw new InvalidOperationException("calmars_payment_amount_invalid");
			var invoice = await GetWorkItemAsync(invoiceWorkItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (invoice.RecordType != (int)CalOesMarsRecordTypes.GeneratedInvoice) throw new InvalidOperationException("calmars_work_item_not_invoice");
			if (invoice.LocalState is not ((int)CalOesMarsLocalStates.PendingPayingEntityApproval or (int)CalOesMarsLocalStates.PendingLocalAgencyApproval)) throw new InvalidOperationException("calmars_invoice_not_payable");
			if (invoice.LocalState == (int)CalOesMarsLocalStates.PendingLocalAgencyApproval) throw new InvalidOperationException("calmars_invoice_not_approved");
			var before = Snapshot(invoice);
			var now = DateTime.UtcNow;
			// Paid is only ever an observed external fact (plan C11 acceptance 8), never derived from the local expected amount.
			invoice.LocalState = (int)CalOesMarsLocalStates.Paid;
			invoice.PaidTotal = observation.PaidTotal;
			invoice.PaidOn = observation.PaidOn;
			invoice.PaymentReference = Trim(observation.PaymentReference);
			invoice.ObservedExternalStatus = Trim(observation.PayingEntityStatus) ?? "Paid";
			invoice.ObservedOn = observation.ObservedOn ?? now;
			invoice.CorrectionComment = Trim(observation.Comment) ?? invoice.CorrectionComment;
			invoice.RowVersion++; invoice.EditedOn = now; invoice.EditedByUserId = userId;
			var snapshot = Deserialize<CalOesMarsInvoiceSnapshot>(invoice.SnapshotJson) ?? new CalOesMarsInvoiceSnapshot();
			await TransactionAsync(async () =>
			{
				await _workItems.SaveOrUpdateAsync(invoice, cancellationToken);
				foreach (var id in snapshot.CoveredWorkItemIds)
				{
					var covered = await _workItems.GetByIdForDepartmentAsync(id, departmentId);
					if (covered == null || covered.IsDeleted || covered.LocalState is (int)CalOesMarsLocalStates.Paid or (int)CalOesMarsLocalStates.Closed) continue;
					covered.LocalState = (int)CalOesMarsLocalStates.Paid; covered.PaidOn = observation.PaidOn; covered.MarsInvoiceId = invoice.MarsInvoiceId;
					covered.RowVersion++; covered.EditedOn = now; covered.EditedByUserId = userId;
					await _workItems.SaveOrUpdateAsync(covered, cancellationToken);
				}
				return true;
			}, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsPaymentReconciled, ipAddress, userAgent, before, invoice);
			return invoice;
		}

		public async Task<CalOesMarsWorkItem> CloseWorkItemAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (item.LocalState is not ((int)CalOesMarsLocalStates.Paid or (int)CalOesMarsLocalStates.DocumentationOnly or (int)CalOesMarsLocalStates.LocalAgencyRejected or (int)CalOesMarsLocalStates.Approved)) throw new InvalidOperationException("calmars_work_item_not_closable");
			var before = Snapshot(item);
			item.LocalState = (int)CalOesMarsLocalStates.Closed;
			item.RowVersion++; item.EditedOn = DateTime.UtcNow; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsExternalStatusObserved, ipAddress, userAgent, before, item);
			return item;
		}

		public async Task<bool> DeleteWorkItemAsync(string workItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId);
			if (item == null) return false;
			if (item.IsExternal) throw new InvalidOperationException("calmars_work_item_external");
			var before = Snapshot(item);
			item.IsDeleted = true; item.EditedOn = DateTime.UtcNow; item.EditedByUserId = userId;
			await _workItems.SaveOrUpdateAsync(item, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsWorkItemDeleted, ipAddress, userAgent, before, item);
			return true;
		}

		public async Task<CalOesMarsInvoiceReconciliation> GetInvoiceReconciliationAsync(string invoiceWorkItemId, int departmentId)
		{
			var invoice = await GetWorkItemAsync(invoiceWorkItemId, departmentId);
			if (invoice == null || invoice.RecordType != (int)CalOesMarsRecordTypes.GeneratedInvoice) return null;
			var snapshot = Deserialize<CalOesMarsInvoiceSnapshot>(invoice.SnapshotJson) ?? new CalOesMarsInvoiceSnapshot();
			var reconciliation = new CalOesMarsInvoiceReconciliation { Invoice = invoice, Snapshot = snapshot, InvoicedTotal = snapshot.InvoicedTotal ?? invoice.ApprovedTotal, PaidTotal = invoice.PaidTotal };
			foreach (var id in snapshot.CoveredWorkItemIds)
			{
				var covered = await GetWorkItemAsync(id, departmentId);
				if (covered != null) reconciliation.CoveredItems.Add(covered);
			}
			reconciliation.ExpectedTotal = reconciliation.CoveredItems.Sum(c => c.ExpectedTotal ?? 0);
			return reconciliation;
		}

		#endregion

		#region Worker 32

		public async Task<int> RunReminderSweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			if (!Config.CostRecoveryConfig.ReminderEnabled || _communication?.Value == null) return 0;
			var departments = ((await _workItems.GetDepartmentsWithOpenItemsAsync())?.ToList() ?? new List<int>())
				.Concat((await _agencies.GetAllAsync())?.Where(a => !a.IsDeleted && a.IsActive).Select(a => a.DepartmentId) ?? Enumerable.Empty<int>()).Distinct().ToList();
			var notified = 0;
			var dayKey = asOfUtc.Date.GetHashCode();
			foreach (var departmentId in departments)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (departmentEnabled != null && !await departmentEnabled(departmentId)) continue;
				var key = HashCode.Combine(dayKey, departmentId);
				lock (RemindedToday) { if (RemindedToday.Contains(key)) continue; }
				var lines = new List<string>();
				try
				{
					// Value-minimized: counts, names and dates only — never amounts, identifiers or reviewer comments (plan C8).
					var effective = (await _rateProfiles.GetEffectiveAsync(departmentId, asOfUtc.Date))?.ToList() ?? new List<CalOesMarsRateProfile>();
					foreach (var profile in effective.Where(p => p.ExpiresOn.HasValue && p.ExpiresOn.Value.Date <= asOfUtc.Date.AddDays(Config.CostRecoveryConfig.AnnualDeadlineLeadDays) && p.SubmissionType != (int)CalOesMarsSubmissionTypes.RateLetter))
						lines.Add($"{(CalOesMarsSubmissionTypes)profile.SubmissionType} {profile.SubmissionYear} expires {profile.ExpiresOn:yyyy-MM-dd}.");
					if (!effective.Any(p => p.SubmissionType == (int)CalOesMarsSubmissionTypes.SalarySurvey)) lines.Add($"No Salary Survey covers {asOfUtc:yyyy-MM-dd}.");
					foreach (var agreement in (await _agreements.GetForDepartmentAsync(departmentId))?.Where(a => a.CoversDate(asOfUtc.Date) && a.EndOn.HasValue && a.EndOn.Value.Date <= asOfUtc.Date.AddDays(Config.CostRecoveryConfig.AgreementExpiryLeadDays)) ?? Enumerable.Empty<CalOesMarsAgreementSnapshot>())
						lines.Add($"Agreement {(agreement.ClassificationTitle ?? agreement.ClassificationCode ?? "(all classifications)")} ends {agreement.EndOn:yyyy-MM-dd}.");

					var queue = (await _workItems.GetActionQueueAsync(departmentId))?.ToList() ?? new List<CalOesMarsWorkItem>();
					var returned = queue.Count(w => w.LocalState == (int)CalOesMarsLocalStates.ReturnedForAgencyReview);
					if (returned > 0) lines.Add($"{returned} record(s) returned for agency review.");
					var invoices = queue.Count(w => w.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice && w.LocalState == (int)CalOesMarsLocalStates.PendingLocalAgencyApproval);
					if (invoices > 0) lines.Add($"{invoices} MARS invoice(s) awaiting local approval.");

					// Only the released cost-recovery deployments, however old: a newest-first page walk let the overdue ones age past its cap.
					var due = asOfUtc.AddDays(-Config.CostRecoveryConfig.F42DueDaysAfterRelease);
					var deployments = await _deploymentService.GetCostRecoveryDeploymentsReleasedBeforeAsync(departmentId, due) ?? new List<Deployment>();
					foreach (var deployment in deployments.Where(d => d.FinanceMode == (int)DeploymentFinanceModes.CostRecovery && d.Status is (int)DeploymentStatuses.Demobilizing or (int)DeploymentStatuses.Completed && (d.StatusChangedOn ?? d.EndOn ?? d.AddedOn) <= due))
					{
						var items = queue.Where(w => string.Equals(w.DeploymentId, deployment.DeploymentId, StringComparison.OrdinalIgnoreCase) && w.RecordType == (int)CalOesMarsRecordTypes.F42).ToList();
						if (!items.Any(w => w.LocalState >= (int)CalOesMarsLocalStates.ReadyForPortal)) lines.Add($"{deployment.Name}: released {Config.CostRecoveryConfig.F42DueDaysAfterRelease}+ days ago without a ready or submitted F-42.");
						var expenses = queue.Where(w => string.Equals(w.DeploymentId, deployment.DeploymentId, StringComparison.OrdinalIgnoreCase) && w.RecordType == (int)CalOesMarsRecordTypes.ExpenseClaim).ToList();
						foreach (var expense in expenses.Where(e => !e.IsExternal))
						{
							var validation = Deserialize<CalOesMarsValidationResult>(expense.ValidationSummaryJson);
							if (validation != null && validation.Errors.Any(x => x.Code == CalOesMarsValidationCodes.ExpenseReceiptMissing)) lines.Add($"{deployment.Name}: expense claim is missing receipt evidence.");
						}
					}
				}
				catch (Exception ex) { Logging.LogException(ex, $"Cal OES MARS reminder sweep failed for department {departmentId}."); continue; }
				if (lines.Count == 0) continue;
				// Claim the department/day atomically: an overlapping sweep that passed the check above must not send a second digest.
				lock (RemindedToday) { if (!RemindedToday.Add(key)) continue; if (RemindedToday.Count > 50_000) RemindedToday.Clear(); }
				await NotifyManagersAsync(departmentId, "Cal OES MARS: " + string.Join(" ", lines));
				notified++;
			}
			return notified;
		}

		private async Task NotifyManagersAsync(int departmentId, string message)
		{
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				var number = _departmentSettings?.Value == null ? null : await _departmentSettings.Value.GetTextToCallNumberForDepartmentAsync(departmentId);
				// Permission 79 defaults to department administrators; the digest goes to them (a narrower role assignment still includes admins).
				foreach (var admin in await _departmentsService.GetActiveAdminsForDepartmentAsync(departmentId))
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, number, department, "Cal OES MARS");
			}
			catch (Exception ex) { Logging.LogException(ex, $"Cal OES MARS digest could not be sent for department {departmentId}."); }
		}

		#endregion

		#region Helpers

		private async Task<CalOesMarsWorkItem> RequireLocalAsync(string workItemId, int departmentId, CalOesMarsRecordTypes recordType)
		{
			var item = await GetWorkItemAsync(workItemId, departmentId) ?? throw new InvalidOperationException("calmars_work_item_not_found");
			if (item.RecordType != (int)recordType) throw new InvalidOperationException("calmars_work_item_type_mismatch");
			if (!item.IsLocallyEditable) throw new InvalidOperationException("calmars_work_item_external");
			return item;
		}

		private async Task<List<CalOesMarsRateLine>> EffectiveRateLinesAsync(int departmentId, DateTime dispatchOn)
		{
			var effective = (await _rateProfiles.GetEffectiveAsync(departmentId, dispatchOn))?.ToList() ?? new List<CalOesMarsRateProfile>();
			return (await _rateLines.GetByProfilesAsync(effective.Select(p => p.CalOesMarsRateProfileId)))?.ToList() ?? new List<CalOesMarsRateLine>();
		}

		private static string RateVersion(List<CalOesMarsRateProfile> effective) => effective == null || effective.Count == 0 ? null : string.Join(",", effective.Select(p => $"{p.CalOesMarsRateProfileId}:{p.RowVersion}"));

		private static decimal Hours(DateTime? from, DateTime? to) => from.HasValue && to.HasValue && to > from ? (decimal)(to.Value - from.Value).TotalHours : 0m;

		private static decimal Hours(DeploymentTimeEntry entry) => entry.EndTime > entry.StartTime ? Math.Max(0, (decimal)(entry.EndTime - entry.StartTime).TotalHours - entry.UnpaidBreakMinutes / 60m) : 0m;

		private static string Join(params string[] parts) { var value = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))); return value.Length == 0 ? null : value; }

		private static void Error(CalOesMarsValidationResult result, string box, string code, string detail) => result.Errors.Add(new CalOesMarsValidationIssue { Box = box, Code = code, Detail = detail });
		private static void Warn(CalOesMarsValidationResult result, string box, string code, string detail) => result.Warnings.Add(new CalOesMarsValidationIssue { Box = box, Code = code, Detail = detail });

		/// <summary>Reads a typed snapshot; a corrupt payload logs and reads as null rather than failing the page.</summary>
		public static T Deserialize<T>(string json) where T : class
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try { return JsonConvert.DeserializeObject<T>(json); }
			catch (Exception ex) { Logging.LogException(ex, "Cal OES MARS snapshot could not be read."); return null; }
		}

		#endregion
	}
}
