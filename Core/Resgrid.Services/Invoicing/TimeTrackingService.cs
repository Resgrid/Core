using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Phase C daily time reports (Workforce &amp; Business Operations plan, C4). Numbers come from the atomic
	/// per-department sequence, entries are validated and replaced as a batch, status transitions happen only here,
	/// and Submitted/Approved publish through the deployment outbox producer. The ADP catalog-28 seam of
	/// <see cref="DeploymentService"/> protects the customer signer name, expense descriptions and receipts.
	/// </summary>
	public class TimeTrackingService : ITimeTrackingService
	{
		private readonly IDeploymentRepository _deployments;
		private readonly IDeploymentPersonnelRepository _personnel;
		private readonly IDeploymentUnitRepository _units;
		private readonly IDeploymentEquipmentRepository _equipment;
		private readonly IDeploymentTimeReportRepository _reports;
		private readonly IDeploymentTimeEntryRepository _entries;
		private readonly IDeploymentExpenseRepository _expenses;
		private readonly IDeploymentAttachmentRepository _attachments;
		private readonly ITimeReportNumberSequenceRepository _sequence;
		private readonly IDeploymentService _deploymentService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IPdfProvider _pdfProvider;
		private readonly IUnitOfWork _unitOfWork;

		/// <summary>A span longer than this without any break is flagged (plan C4 "breaks per 5-h rule").</summary>
		public const int BreakRuleHours = 5;
		/// <summary>Travel beyond this many hours in a day needs approval (plan C4).</summary>
		public const int LongTravelHours = 12;
		private const int DefaultStartHour = 8;
		private const int DefaultEndHour = 16;

		public TimeTrackingService(IDeploymentRepository deployments, IDeploymentPersonnelRepository personnel, IDeploymentUnitRepository units, IDeploymentEquipmentRepository equipment,
			IDeploymentTimeReportRepository reports, IDeploymentTimeEntryRepository entries, IDeploymentExpenseRepository expenses, IDeploymentAttachmentRepository attachments,
			ITimeReportNumberSequenceRepository sequence, IDeploymentService deploymentService, IDepartmentsService departmentsService, IUserProfileService userProfileService,
			IUnitsService unitsService, IEventAggregator eventAggregator, IPdfProvider pdfProvider, IUnitOfWork unitOfWork)
		{
			_deployments = deployments;
			_personnel = personnel;
			_units = units;
			_equipment = equipment;
			_reports = reports;
			_entries = entries;
			_expenses = expenses;
			_attachments = attachments;
			_sequence = sequence;
			_deploymentService = deploymentService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
			_pdfProvider = pdfProvider;
			_unitOfWork = unitOfWork;
		}

		private DeploymentService Core => _deploymentService as DeploymentService;

		#region Reads

		public async Task<List<DeploymentTimeReport>> GetTimeReportsAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null) return new List<DeploymentTimeReport>();
			var reports = (await _reports.GetByDeploymentAsync(deploymentId))?.Where(r => r.DepartmentId == departmentId).ToList() ?? new List<DeploymentTimeReport>();
			return reports;
		}

		public async Task<decimal> GetPersonnelHoursAsync(string deploymentId, int departmentId)
		{
			// Two reads for the whole deployment (the CSV export's shape) instead of a report + entries pair per report.
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null) return 0m;
			var counted = (await _reports.GetByDeploymentAsync(deploymentId))?.Where(r => r.DepartmentId == departmentId && !r.IsDeleted && r.Status != (int)DeploymentTimeReportStatuses.Void)
				.Select(r => r.DeploymentTimeReportId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var entries = await _entries.GetByDeploymentAsync(deploymentId);
			return entries?.Where(e => e.DepartmentId == departmentId && e.DeploymentTimeReportId != null && counted.Contains(e.DeploymentTimeReportId) && e.SubjectType == (int)DeploymentTimeSubjectTypes.Personnel).Sum(e => e.Hours) ?? 0m;
		}

		public async Task<DeploymentTimeReport> GetTimeReportByIdAsync(string deploymentTimeReportId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(deploymentTimeReportId)) return null;
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) return null;
			report.Entries = (await _entries.GetByReportAsync(deploymentTimeReportId))?.ToList() ?? new List<DeploymentTimeEntry>();
			return report;
		}

		public async Task<List<DeploymentTimeReport>> GetTimeReportsWithEntriesAsync(string deploymentId, int departmentId)
		{
			var reports = (await GetTimeReportsAsync(deploymentId, departmentId)).Where(r => !r.IsDeleted).ToList();
			if (reports.Count == 0) return reports;
			// Same order as GetByReportAsync (SortOrder, StartTime); the deployment read orders by StartTime only.
			var entries = ((await _entries.GetByDeploymentAsync(deploymentId)) ?? Enumerable.Empty<DeploymentTimeEntry>())
				.Where(e => e.DepartmentId == departmentId && e.DeploymentTimeReportId != null)
				.GroupBy(e => e.DeploymentTimeReportId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderBy(e => e.SortOrder).ThenBy(e => e.StartTime).ToList(), StringComparer.OrdinalIgnoreCase);
			foreach (var report in reports)
				report.Entries = entries.TryGetValue(report.DeploymentTimeReportId, out var rows) ? rows : new List<DeploymentTimeEntry>();
			return reports;
		}

		public async Task<List<DeploymentTimeReport>> GetUnbilledApprovedReportsAsync(int departmentId, string deploymentId = null)
		{
			var reports = (await _reports.GetUnbilledApprovedAsync(departmentId, deploymentId))?.ToList() ?? new List<DeploymentTimeReport>();
			return reports;
		}

		#endregion

		#region Reports

		public Task<DeploymentTimeReport> CreateTimeReportAsync(string deploymentId, int departmentId, DateTime reportDate, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			CreateTimeReportAsync(deploymentId, departmentId, reportDate, null, null, userId, ipAddress, userAgent, cancellationToken);

		public async Task<DeploymentTimeReport> CreateTimeReportAsync(string deploymentId, int departmentId, DateTime reportDate, string deploymentUnitId, string deploymentPersonnelId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deploymentService.GetDeploymentByIdAsync(deploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");
			if (!deployment.IsOpen) throw new InvalidOperationException("deployments_closed");
			deploymentUnitId = Trim(deploymentUnitId);
			deploymentPersonnelId = Trim(deploymentPersonnelId);
			if (deploymentUnitId != null && deploymentPersonnelId != null) throw new InvalidOperationException("timereports_scope_invalid");
			if (deploymentUnitId != null && !deployment.Units.Any(u => u.IsActive && Same(u.DeploymentUnitId, deploymentUnitId))) throw new InvalidOperationException("timereports_scope_invalid");
			if (deploymentPersonnelId != null && !deployment.Personnel.Any(p => p.IsActive && Same(p.DeploymentPersonnelId, deploymentPersonnelId))) throw new InvalidOperationException("timereports_scope_invalid");

			var day = reportDate.Date;
			var sameDay = await LiveReportsOnAsync(deploymentId, departmentId, day);
			if (sameDay.Any(r => Same(r.DeploymentUnitId, deploymentUnitId) && Same(r.DeploymentPersonnelId, deploymentPersonnelId))) throw new InvalidOperationException("timereports_date_exists");
			// A subject bills once a day: whoever is already on another live report for this day is not prefilled again, and a crew
			// or person whose own time is already on one cannot open a second report for it.
			var covered = await CoveredSubjectsAsync(deploymentId, departmentId, sameDay, null);
			if ((deploymentUnitId != null && covered.Contains(deploymentUnitId)) || (deploymentPersonnelId != null && covered.Contains(deploymentPersonnelId)))
				throw new InvalidOperationException("timereports_subject_covered");
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var timeZone = string.IsNullOrWhiteSpace(deployment.LocalTimeZoneId) ? department?.TimeZone : deployment.LocalTimeZoneId;

			return await TransactionAsync(async () =>
			{
				var report = new DeploymentTimeReport
				{
					DeploymentId = deploymentId, DepartmentId = departmentId, ReportNumber = await _sequence.GetNextNumberAsync(departmentId, cancellationToken), ReportDate = day,
					DeploymentUnitId = deploymentUnitId, DeploymentPersonnelId = deploymentPersonnelId,
					Status = (int)DeploymentTimeReportStatuses.Draft, IncidentNumber = deployment.IncidentNumber, ResourceOrderNumber = deployment.ResourceOrderNumber,
					RequestNumber = deployment.RequestNumber, CostCode = deployment.CostCode, PointOfHire = deployment.PointOfHire,
					AddedOn = DateTime.UtcNow, AddedByUserId = userId
				};
				var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);

				// Prefill: one Deployment entry per active subject in the report's scope, copying the most recent earlier span for the same subject when there is one.
				var earlier = (await _reports.GetByDeploymentAsync(deploymentId))?.Where(r => r.ReportDate < day && r.Status != (int)DeploymentTimeReportStatuses.Void && !r.IsDeleted).ToDictionary(r => r.DeploymentTimeReportId, StringComparer.OrdinalIgnoreCase)
					?? new Dictionary<string, DeploymentTimeReport>(StringComparer.OrdinalIgnoreCase);
				var previousEntries = earlier.Count == 0 ? new List<DeploymentTimeEntry>()
					: ((await _entries.GetByDeploymentAsync(deploymentId)) ?? Enumerable.Empty<DeploymentTimeEntry>()).Where(e => e.DeploymentTimeReportId != null && earlier.ContainsKey(e.DeploymentTimeReportId))
						.OrderByDescending(e => earlier[e.DeploymentTimeReportId].ReportDate).ToList();
				var defaultStart = ToUtc(day.AddHours(DefaultStartHour), timeZone);
				var defaultEnd = ToUtc(day.AddHours(DefaultEndHour), timeZone);
				var sort = 0;
				var subjects = ScopeSubjects(deployment, deploymentUnitId, deploymentPersonnelId).Where(s => !covered.Contains(s.Id));
				foreach (var subject in subjects)
				{
					// Latest earlier day that has this subject, then its first Deployment span of that day.
					var priorDay = previousEntries.FirstOrDefault(e => e.SubjectId == subject.Id && e.EntryType == (int)DeploymentTimeEntryTypes.Deployment)?.DeploymentTimeReportId;
					var prior = priorDay == null ? null : previousEntries.Where(e => e.DeploymentTimeReportId == priorDay && e.SubjectId == subject.Id && e.EntryType == (int)DeploymentTimeEntryTypes.Deployment).OrderBy(e => e.StartTime).FirstOrDefault();
					var start = prior == null ? defaultStart : day.Add(ToLocal(prior.StartTime, timeZone).TimeOfDay);
					var end = prior == null ? defaultEnd : day.Add(ToLocal(prior.EndTime, timeZone).TimeOfDay);
					if (prior != null) { start = ToUtc(start, timeZone); end = ToUtc(end, timeZone); if (end <= start) end = end.AddDays(1); }
					var crew = subject.Type == DeploymentTimeSubjectTypes.Unit ? deployment.Personnel.Count(p => p.IsActive && p.DeploymentUnitId == subject.Id) : (int?)null;
					await _entries.SaveOrUpdateAsync(new DeploymentTimeEntry
					{
						DeploymentTimeReportId = saved.DeploymentTimeReportId, DeploymentId = deploymentId, DepartmentId = departmentId, SubjectType = (int)subject.Type,
						DeploymentPersonnelId = subject.Type == DeploymentTimeSubjectTypes.Personnel ? subject.Id : null,
						DeploymentUnitId = subject.Type == DeploymentTimeSubjectTypes.Unit ? subject.Id : null,
						DeploymentEquipmentId = subject.Type == DeploymentTimeSubjectTypes.Equipment ? subject.Id : null,
						EntryType = (int)DeploymentTimeEntryTypes.Deployment, StartTime = start, EndTime = end,
						PaidBreakMinutes = prior?.PaidBreakMinutes ?? 0, UnpaidBreakMinutes = prior?.UnpaidBreakMinutes ?? 0, CrewSizeSnapshot = crew, CertificationCode = subject.Cert,
						AgencySuppliedMeals = prior?.AgencySuppliedMeals ?? false, AgencySuppliedAccommodation = prior?.AgencySuppliedAccommodation ?? false, SortOrder = sort++
					}, cancellationToken);
				}

				Audit(departmentId, userId, AuditLogTypes.TimeReportCreated, ipAddress, userAgent, null, saved);
				if (Core != null) await Core.PublishTimeReportAsync(deployment, WorkflowTriggerEventType.TimeReportCreated, saved, cancellationToken);
				return await GetTimeReportByIdAsync(saved.DeploymentTimeReportId, departmentId);
			}, cancellationToken);
		}

		public async Task<DeploymentTimeReport> UpdateTimeReportAsync(DeploymentTimeReport report, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (report == null) throw new ArgumentNullException(nameof(report));
			var existing = await _reports.GetByIdForDepartmentAsync(report.DeploymentTimeReportId, report.DepartmentId);
			if (existing == null || existing.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (!existing.IsEditable) throw new InvalidOperationException("timereports_locked");

			var before = DeploymentService.Snapshot(existing);
			existing.IncidentNumber = Trim(report.IncidentNumber);
			existing.ResourceOrderNumber = Trim(report.ResourceOrderNumber);
			existing.RequestNumber = Trim(report.RequestNumber);
			existing.CostCode = Trim(report.CostCode);
			existing.PointOfHire = Trim(report.PointOfHire);
			existing.NoClear8 = report.NoClear8;
			existing.UnsafeConditionsStandDown = report.UnsafeConditionsStandDown;
			existing.Notes = Trim(report.Notes);
			existing.RmsExternalOrderFillId = Trim(report.RmsExternalOrderFillId);
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;
			var saved = await _reports.SaveOrUpdateAsync(existing, cancellationToken);
			Audit(report.DepartmentId, userId, AuditLogTypes.TimeReportUpdated, ipAddress, userAgent, before, saved);
			return await GetTimeReportByIdAsync(saved.DeploymentTimeReportId, report.DepartmentId);
		}

		public Task<TimeReportSaveResult> SaveTimeEntriesAsync(string deploymentTimeReportId, int departmentId, List<DeploymentTimeEntry> entries, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SaveTimeEntriesAsync(deploymentTimeReportId, departmentId, entries, null, userId, ipAddress, userAgent, cancellationToken);

		public async Task<TimeReportSaveResult> SaveTimeEntriesAsync(string deploymentTimeReportId, int departmentId, List<DeploymentTimeEntry> entries, DeploymentTimeAccess access, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (!report.IsEditable) throw new InvalidOperationException("timereports_locked");
			var deployment = await _deploymentService.GetDeploymentByIdAsync(report.DeploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");

			var incoming = (entries ?? new List<DeploymentTimeEntry>()).Where(e => e != null).ToList();
			var roster = RosterSubjects(deployment);
			foreach (var entry in incoming)
			{
				// The subject id decides the type; the caller's SubjectType is not trusted.
				if (!string.IsNullOrWhiteSpace(entry.DeploymentPersonnelId)) { entry.SubjectType = (int)DeploymentTimeSubjectTypes.Personnel; entry.DeploymentUnitId = null; entry.DeploymentEquipmentId = null; }
				else if (!string.IsNullOrWhiteSpace(entry.DeploymentUnitId)) { entry.SubjectType = (int)DeploymentTimeSubjectTypes.Unit; entry.DeploymentEquipmentId = null; }
				else if (!string.IsNullOrWhiteSpace(entry.DeploymentEquipmentId)) entry.SubjectType = (int)DeploymentTimeSubjectTypes.Equipment;
				if (!Enum.IsDefined(typeof(DeploymentTimeEntryTypes), entry.EntryType)) entry.EntryType = (int)DeploymentTimeEntryTypes.Deployment;
				entry.PaidBreakMinutes = Math.Max(0, entry.PaidBreakMinutes);
				entry.UnpaidBreakMinutes = Math.Max(0, entry.UnpaidBreakMinutes);
				entry.Notes = Trim(entry.Notes);
				entry.CertificationCode = Trim(entry.CertificationCode);
			}

			// A scoped writer (a crew member, the unit tablet, one person) replaces only the subjects they may write; every other
			// subject's entries stay exactly as stored, so two crews saving the same deployment-wide report never erase each other.
			var current = (await _entries.GetByReportAsync(deploymentTimeReportId))?.ToList() ?? new List<DeploymentTimeEntry>();
			var restricted = access != null && !access.CanManage;
			var preserved = new List<DeploymentTimeEntry>();
			if (restricted)
			{
				incoming = incoming.Where(e => access.CanWriteSubject(e.SubjectId)).ToList();
				preserved = current.Where(e => !access.CanWriteSubject(e.SubjectId)).ToList();
			}

			var validation = Validate(report, incoming.Concat(preserved).ToList(), roster.Keys);
			await ValidateScopeAsync(validation, report, deployment, incoming);
			var result = new TimeReportSaveResult { Validation = validation };
			if (!validation.IsValid)
			{
				result.Report = await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
				return result;
			}

			var before = DeploymentService.Snapshot(report);
			await TransactionAsync(async () =>
			{
				// Entries keep their ids across a save: the catalog-28 envelope on an entry's notes is bound to the entry's row key,
				// so an untouched entry (REDACTED posted back) is updated in place and a stale one deleted, never re-inserted.
				var byId = current.Where(e => !string.IsNullOrWhiteSpace(e.DeploymentTimeEntryId)).ToDictionary(e => e.DeploymentTimeEntryId, StringComparer.OrdinalIgnoreCase);
				var preservedIds = new HashSet<string>(preserved.Select(e => e.DeploymentTimeEntryId), StringComparer.OrdinalIgnoreCase);
				var kept = new HashSet<string>(incoming.Where(e => !string.IsNullOrWhiteSpace(e.DeploymentTimeEntryId) && byId.ContainsKey(e.DeploymentTimeEntryId) && !preservedIds.Contains(e.DeploymentTimeEntryId)).Select(e => e.DeploymentTimeEntryId), StringComparer.OrdinalIgnoreCase);
				foreach (var stale in current.Where(e => !kept.Contains(e.DeploymentTimeEntryId) && !preservedIds.Contains(e.DeploymentTimeEntryId)))
					await _entries.DeleteAsync(stale, cancellationToken);
				// A scoped save appends after the entries it did not touch; an unrestricted save renumbers the whole report.
				var sort = preserved.Count == 0 ? 0 : preserved.Max(e => e.SortOrder) + 1;
				foreach (var entry in incoming.OrderBy(e => e.SortOrder).ThenBy(e => e.StartTime))
				{
					var existingEntry = !string.IsNullOrWhiteSpace(entry.DeploymentTimeEntryId) && byId.TryGetValue(entry.DeploymentTimeEntryId, out var found) && !preservedIds.Contains(entry.DeploymentTimeEntryId) ? found : null;
					if (existingEntry == null)
					{
						entry.DeploymentTimeEntryId = null;
					}
					entry.DeploymentTimeReportId = deploymentTimeReportId;
					entry.DeploymentId = report.DeploymentId;
					entry.DepartmentId = departmentId;
					entry.SortOrder = sort++;
					if (entry.SubjectType == (int)DeploymentTimeSubjectTypes.Unit && !entry.CrewSizeSnapshot.HasValue)
						entry.CrewSizeSnapshot = deployment.Personnel.Count(p => p.IsActive && p.DeploymentUnitId == entry.DeploymentUnitId);
					await _entries.SaveOrUpdateAsync(entry, cancellationToken);
				}
				report.EditedOn = DateTime.UtcNow;
				report.EditedByUserId = userId;
				await _reports.SaveOrUpdateAsync(report, cancellationToken);
				return true;
			}, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TimeReportUpdated, ipAddress, userAgent, before, report);
			result.Report = await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
			return result;
		}

		/// <summary>
		/// M0227 rules on top of <see cref="Validate"/>: a crew or individual report carries only its own subjects, and a subject
		/// already on another live report for the same day is refused (it would bill twice).
		/// </summary>
		private async Task ValidateScopeAsync(TimeReportValidation validation, DeploymentTimeReport report, Deployment deployment, IReadOnlyList<DeploymentTimeEntry> entries)
		{
			var scope = ScopeSubjectIds(deployment, report);
			var sameDay = await LiveReportsOnAsync(report.DeploymentId, report.DepartmentId, report.ReportDate.Date);
			var covered = await CoveredSubjectsAsync(report.DeploymentId, report.DepartmentId, sameDay, report.DeploymentTimeReportId);
			foreach (var entry in entries)
			{
				var subject = entry.SubjectId;
				if (string.IsNullOrWhiteSpace(subject)) continue;
				if (scope != null && !scope.Contains(subject))
					validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.SubjectOutsideScope, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId });
				else if (covered.Contains(subject))
					validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.SubjectOnOtherReport, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId });
			}
		}

		/// <summary>Live (not deleted, not void) reports of the deployment on one calendar day.</summary>
		private async Task<List<DeploymentTimeReport>> LiveReportsOnAsync(string deploymentId, int departmentId, DateTime day) =>
			(await _reports.GetByDeploymentAsync(deploymentId))?.Where(r => r.DepartmentId == departmentId && r.IsLive && r.ReportDate.Date == day.Date).ToList() ?? new List<DeploymentTimeReport>();

		/// <summary>Subjects with time on any of <paramref name="reports"/> other than <paramref name="excludingReportId"/>.</summary>
		private async Task<HashSet<string>> CoveredSubjectsAsync(string deploymentId, int departmentId, IReadOnlyCollection<DeploymentTimeReport> reports, string excludingReportId)
		{
			var others = new HashSet<string>(reports.Where(r => !Same(r.DeploymentTimeReportId, excludingReportId)).Select(r => r.DeploymentTimeReportId), StringComparer.OrdinalIgnoreCase);
			if (others.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var entries = (await _entries.GetByDeploymentAsync(deploymentId)) ?? Enumerable.Empty<DeploymentTimeEntry>();
			return new HashSet<string>(entries.Where(e => e.DepartmentId == departmentId && e.DeploymentTimeReportId != null && others.Contains(e.DeploymentTimeReportId) && !string.IsNullOrWhiteSpace(e.SubjectId)).Select(e => e.SubjectId), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Active subjects a new report prefills: the whole roster, one unit with its crew and equipment, or one person.</summary>
		private static List<(DeploymentTimeSubjectTypes Type, string Id, string Cert)> ScopeSubjects(Deployment deployment, string deploymentUnitId, string deploymentPersonnelId)
		{
			if (deploymentPersonnelId != null)
				return deployment.Personnel.Where(p => p.IsActive && Same(p.DeploymentPersonnelId, deploymentPersonnelId)).Select(p => (DeploymentTimeSubjectTypes.Personnel, p.DeploymentPersonnelId, p.CertificationCode)).ToList();
			bool InScope(string unitId) => deploymentUnitId == null || Same(unitId, deploymentUnitId);
			return deployment.Personnel.Where(p => p.IsActive && InScope(p.DeploymentUnitId)).Select(p => (DeploymentTimeSubjectTypes.Personnel, p.DeploymentPersonnelId, p.CertificationCode))
				.Concat(deployment.Units.Where(u => u.IsActive && InScope(u.DeploymentUnitId)).Select(u => (DeploymentTimeSubjectTypes.Unit, u.DeploymentUnitId, (string)null)))
				.Concat(deployment.Equipment.Where(e => e.IsActive && InScope(e.DeploymentUnitId)).Select(e => (DeploymentTimeSubjectTypes.Equipment, e.DeploymentEquipmentId, (string)null)))
				.ToList();
		}

		/// <summary>Every subject (active or since removed) a scoped report may carry; null for a deployment-wide report (the roster check covers it).</summary>
		private static HashSet<string> ScopeSubjectIds(Deployment deployment, DeploymentTimeReport report)
		{
			switch (report.Scope)
			{
				case DeploymentTimeReportScopes.Individual:
					return new HashSet<string>(new[] { report.DeploymentPersonnelId }, StringComparer.OrdinalIgnoreCase);
				case DeploymentTimeReportScopes.Crew:
					var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { report.DeploymentUnitId };
					foreach (var p in deployment.Personnel.Where(p => Same(p.DeploymentUnitId, report.DeploymentUnitId))) ids.Add(p.DeploymentPersonnelId);
					foreach (var e in deployment.Equipment.Where(e => Same(e.DeploymentUnitId, report.DeploymentUnitId))) ids.Add(e.DeploymentEquipmentId);
					return ids;
				default:
					return null;
			}
		}

		private static bool Same(string a, string b) => string.Equals(string.IsNullOrWhiteSpace(a) ? null : a, string.IsNullOrWhiteSpace(b) ? null : b, StringComparison.OrdinalIgnoreCase);

		public TimeReportValidation Validate(DeploymentTimeReport report, IReadOnlyList<DeploymentTimeEntry> entries, IReadOnlyCollection<string> rosterSubjectIds)
		{
			var validation = new TimeReportValidation();
			if (entries == null || entries.Count == 0)
			{
				validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.NoEntries });
				return validation;
			}
			var dayStart = report.ReportDate.Date.AddDays(-1);
			var dayEnd = report.ReportDate.Date.AddDays(2);
			foreach (var entry in entries)
			{
				var subject = entry.SubjectId;
				if (string.IsNullOrWhiteSpace(subject) || (rosterSubjectIds != null && !rosterSubjectIds.Contains(subject)))
					validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.SubjectNotOnRoster, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId });
				if (entry.EndTime <= entry.StartTime)
					validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.EndBeforeStart, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId });
				if (entry.StartTime < dayStart || entry.EndTime > dayEnd)
					validation.Warnings.Add(new TimeReportIssue { Code = TimeReportValidation.OutsideReportDate, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId });
				if ((entry.EndTime - entry.StartTime).TotalHours > BreakRuleHours && entry.PaidBreakMinutes + entry.UnpaidBreakMinutes == 0 && entry.EntryType != (int)DeploymentTimeEntryTypes.Travel)
					validation.Warnings.Add(new TimeReportIssue { Code = TimeReportValidation.BreakRule, SubjectId = subject, EntryId = entry.DeploymentTimeEntryId, Detail = BreakRuleHours.ToString(CultureInfo.InvariantCulture) });
			}
			foreach (var group in entries.Where(e => !string.IsNullOrWhiteSpace(e.SubjectId)).GroupBy(e => e.SubjectId))
			{
				var ordered = group.OrderBy(e => e.StartTime).ToList();
				for (var i = 1; i < ordered.Count; i++)
					if (ordered[i].StartTime < ordered[i - 1].EndTime)
						validation.Errors.Add(new TimeReportIssue { Code = TimeReportValidation.Overlap, SubjectId = group.Key, EntryId = ordered[i].DeploymentTimeEntryId });
				var travel = group.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Travel).Sum(e => e.Hours);
				if (travel > LongTravelHours)
					validation.Warnings.Add(new TimeReportIssue { Code = TimeReportValidation.LongTravel, SubjectId = group.Key, Detail = travel.ToString("0.##", CultureInfo.InvariantCulture) });
			}
			return validation;
		}

		public async Task<TimeReportSaveResult> SubmitTimeReportAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (report.Status != (int)DeploymentTimeReportStatuses.Draft) throw new InvalidOperationException("timereports_status_transition_invalid");
			var deployment = await _deploymentService.GetDeploymentByIdAsync(report.DeploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");
			var entries = (await _entries.GetByReportAsync(deploymentTimeReportId))?.ToList() ?? new List<DeploymentTimeEntry>();
			var validation = Validate(report, entries, RosterSubjects(deployment).Keys);
			await ValidateScopeAsync(validation, report, deployment, entries);
			var result = new TimeReportSaveResult { Validation = validation };
			if (!validation.IsValid) { result.Report = await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId); return result; }

			var before = DeploymentService.Snapshot(report);
			report.Status = (int)DeploymentTimeReportStatuses.Submitted;
			report.SubmittedByUserId = userId;
			report.SubmittedOn = DateTime.UtcNow;
			report.EditedOn = DateTime.UtcNow;
			report.EditedByUserId = userId;
			var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TimeReportSubmitted, ipAddress, userAgent, before, saved);
			if (Core != null) await Core.PublishTimeReportAsync(deployment, WorkflowTriggerEventType.TimeReportSubmitted, saved, cancellationToken);
			result.Report = await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
			return result;
		}

		public async Task<DeploymentTimeReport> ApproveTimeReportAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (report.Status != (int)DeploymentTimeReportStatuses.Submitted) throw new InvalidOperationException("timereports_status_transition_invalid");
			var deployment = await _deployments.GetByIdForDepartmentAsync(report.DeploymentId, departmentId);

			var before = DeploymentService.Snapshot(report);
			report.Status = (int)DeploymentTimeReportStatuses.Approved;
			report.ApprovedByUserId = userId;
			report.ApprovedOn = DateTime.UtcNow;
			report.EditedOn = DateTime.UtcNow;
			report.EditedByUserId = userId;
			var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TimeReportApproved, ipAddress, userAgent, before, saved);
			if (Core != null && deployment != null) await Core.PublishTimeReportAsync(deployment, WorkflowTriggerEventType.TimeReportApproved, saved, cancellationToken);
			return await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
		}

		public async Task<int> MarkTimeReportsBilledAsync(IEnumerable<string> deploymentTimeReportIds, int departmentId, string invoiceId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(invoiceId)) throw new ArgumentNullException(nameof(invoiceId));
			var count = 0;
			foreach (var id in (deploymentTimeReportIds ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct())
			{
				var report = await _reports.GetByIdForDepartmentAsync(id, departmentId);
				if (report == null || report.IsDeleted || report.Status != (int)DeploymentTimeReportStatuses.Approved || !string.IsNullOrWhiteSpace(report.InvoiceId)) continue;
				var before = DeploymentService.Snapshot(report);
				report.Status = (int)DeploymentTimeReportStatuses.Billed;
				report.InvoiceId = invoiceId;
				report.EditedOn = DateTime.UtcNow;
				report.EditedByUserId = userId;
				var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);
				Audit(departmentId, userId, AuditLogTypes.TimeReportBilled, ipAddress, userAgent, before, saved);
				count++;
			}
			return count;
		}

		public async Task<DeploymentTimeReport> VoidTimeReportAsync(string deploymentTimeReportId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (report.Status is (int)DeploymentTimeReportStatuses.Billed or (int)DeploymentTimeReportStatuses.Void || !string.IsNullOrWhiteSpace(report.InvoiceId))
				throw new InvalidOperationException("timereports_status_transition_invalid");

			var before = DeploymentService.Snapshot(report);
			report.Status = (int)DeploymentTimeReportStatuses.Void;
			if (!string.IsNullOrWhiteSpace(reason))
				report.Notes = string.IsNullOrWhiteSpace(report.Notes) ? $"Void: {reason.Trim()}" : $"{report.Notes}\nVoid: {reason.Trim()}";
			report.EditedOn = DateTime.UtcNow;
			report.EditedByUserId = userId;
			var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TimeReportVoided, ipAddress, userAgent, before, saved);
			var deployment = await _deployments.GetByIdForDepartmentAsync(report.DeploymentId, departmentId);
			if (Core != null && deployment != null) await Core.PublishTimeReportAsync(deployment, WorkflowTriggerEventType.TimeReportVoided, saved, cancellationToken);
			return await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
		}

		public async Task<DeploymentTimeReport> SignTimeReportAsync(string deploymentTimeReportId, int departmentId, bool contractorSigned, string customerSignerName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			if (report.Status is (int)DeploymentTimeReportStatuses.Void or (int)DeploymentTimeReportStatuses.Billed) throw new InvalidOperationException("timereports_locked");
			var before = DeploymentService.Snapshot(report);
			if (contractorSigned) { report.ContractorSignedByUserId = userId; report.ContractorSignedOn = DateTime.UtcNow; }
			var signer = Trim(customerSignerName);
			if (signer != null) { report.CustomerSignerName = signer; report.CustomerSignedOn = DateTime.UtcNow; }
			report.EditedOn = DateTime.UtcNow;
			report.EditedByUserId = userId;
			var saved = await _reports.SaveOrUpdateAsync(report, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TimeReportUpdated, ipAddress, userAgent, before, saved);
			return await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
		}

		private static Dictionary<string, string> RosterSubjects(Deployment deployment)
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var p in deployment.Personnel) map[p.DeploymentPersonnelId] = p.DisplayName ?? p.UserId;
			foreach (var u in deployment.Units) map[u.DeploymentUnitId] = u.UnitName ?? u.UnitId.ToString();
			foreach (var e in deployment.Equipment) map[e.DeploymentEquipmentId] = e.FreeTextName ?? e.InventoryAssetId ?? e.InventoryItemId;
			return map;
		}

		#endregion

		#region Documents and export

		public async Task<string> RenderTimeReportHtmlAsync(string deploymentTimeReportId, int departmentId)
		{
			var report = await GetTimeReportByIdAsync(deploymentTimeReportId, departmentId);
			if (report == null) throw new InvalidOperationException("timereports_not_found");
			var deployment = await _deploymentService.GetDeploymentByIdAsync(report.DeploymentId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var signer = string.IsNullOrWhiteSpace(report.ContractorSignedByUserId) ? null : await _userProfileService.GetProfileByUserIdAsync(report.ContractorSignedByUserId);
			return RenderTimeReportHtml(report, deployment, department, RosterSubjects(deployment), signer?.FullName.AsFirstNameLastName);
		}

		public async Task<byte[]> GetTimeReportPdfAsync(string deploymentTimeReportId, int departmentId)
		{
			var html = await RenderTimeReportHtmlAsync(deploymentTimeReportId, departmentId);
			return _pdfProvider.ConvertHtmlToPdf(html);
		}

		public async Task<DeploymentAttachment> GenerateTimeReportPdfAsync(string deploymentTimeReportId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(deploymentTimeReportId, departmentId);
			if (report == null || report.IsDeleted) throw new InvalidOperationException("timereports_not_found");
			var pdf = await GetTimeReportPdfAsync(deploymentTimeReportId, departmentId);
			if (pdf == null || pdf.Length == 0) throw new InvalidOperationException("deployments_pdf_failed");
			return await _deploymentService.SaveAttachmentAsync(new DeploymentAttachment
			{
				DeploymentId = report.DeploymentId, DepartmentId = departmentId, AttachmentType = (int)DeploymentAttachmentTypes.TimeReportPdf,
				Name = $"DTR {report.ReportNumber} {report.ReportDate:yyyy-MM-dd}", FileName = $"dtr-{report.ReportNumber}-{report.ReportDate:yyyyMMdd}.pdf", FileType = "application/pdf", Data = pdf
			}, userId, ipAddress, userAgent, cancellationToken);
		}

		public static string RenderTimeReportHtml(DeploymentTimeReport report, Deployment deployment, Department department, IReadOnlyDictionary<string, string> subjectNames, string contractorSignerName)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			string T(DateTime value) => department == null ? value.ToString("HH:mm") : value.TimeConverter(department).ToString("HH:mm");
			string D(DateTime? value) => value.HasValue ? (department == null ? value.Value.ToString("yyyy-MM-dd HH:mm") : value.Value.TimeConverter(department).ToString("yyyy-MM-dd HH:mm")) : "—";
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Resgrid | Daily Time Report</title><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}h1{font-size:18px;margin:0 0 4px}h2{font-size:13px;margin:16px 0 6px;border-bottom:1px solid #999;padding-bottom:2px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}th{background:#eee}.meta td{border:none;padding:2px 12px 2px 0}.muted{color:#666}.num{text-align:right}.sig td{border:none;padding-top:28px;border-top:1px solid #444}</style></head><body>");
			string Named(string id) => id != null && subjectNames != null && subjectNames.TryGetValue(id, out var n) ? n : id;
			var title = report.Scope switch { DeploymentTimeReportScopes.Crew => "Crew Time Report", DeploymentTimeReportScopes.Individual => "Individual Time Report", _ => "Daily Time Report" };
			sb.Append("<h1>").Append(E(department?.Name)).Append(" — ").Append(title).Append(" #").Append(report.ReportNumber).Append("</h1>");
			sb.Append("<div class=\"muted\">").Append(E(deployment?.Name)).Append(" · ").Append(report.ReportDate.ToString("yyyy-MM-dd")).Append(" · ").Append(E(((DeploymentTimeReportStatuses)report.Status).ToString()));
			if (report.Scope == DeploymentTimeReportScopes.Crew) sb.Append(" · Crew: ").Append(E(Named(report.DeploymentUnitId)));
			if (report.Scope == DeploymentTimeReportScopes.Individual) sb.Append(" · Resource: ").Append(E(Named(report.DeploymentPersonnelId)));
			sb.Append("</div>");
			sb.Append("<table class=\"meta\"><tr><td><strong>Incident #</strong> ").Append(E(report.IncidentNumber)).Append("</td><td><strong>Resource order #</strong> ").Append(E(report.ResourceOrderNumber)).Append("</td><td><strong>Request #</strong> ").Append(E(report.RequestNumber)).Append("</td></tr>");
			sb.Append("<tr><td><strong>Cost code</strong> ").Append(E(report.CostCode)).Append("</td><td><strong>Point of hire</strong> ").Append(E(report.PointOfHire)).Append("</td><td>");
			if (report.NoClear8) sb.Append("<strong>No clear 8</strong> ");
			if (report.UnsafeConditionsStandDown) sb.Append("<strong>Unsafe conditions stand-down</strong>");
			sb.Append("</td></tr></table>");

			sb.Append("<h2>Time entries</h2><table><tr><th>Subject</th><th>Type</th><th>Start</th><th>End</th><th class=\"num\">Paid break</th><th class=\"num\">Unpaid break</th><th class=\"num\">Hours</th><th>Crew</th><th>Cert</th><th class=\"num\">Km</th><th>Notes</th></tr>");
			decimal total = 0;
			foreach (var entry in report.Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.StartTime))
			{
				total += entry.Hours;
				sb.Append("<tr><td>").Append(E(subjectNames != null && entry.SubjectId != null && subjectNames.TryGetValue(entry.SubjectId, out var name) ? name : entry.SubjectId)).Append("</td><td>").Append(E(((DeploymentTimeEntryTypes)entry.EntryType).ToString())).Append("</td><td>")
				  .Append(T(entry.StartTime)).Append("</td><td>").Append(T(entry.EndTime)).Append("</td><td class=\"num\">").Append(entry.PaidBreakMinutes).Append("</td><td class=\"num\">").Append(entry.UnpaidBreakMinutes).Append("</td><td class=\"num\">")
				  .Append(entry.Hours.ToString("0.00", CultureInfo.InvariantCulture)).Append("</td><td>").Append(entry.CrewSizeSnapshot?.ToString() ?? string.Empty).Append("</td><td>").Append(E(entry.CertificationCode)).Append("</td><td class=\"num\">")
				  .Append(entry.MileageKm?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty).Append("</td><td>").Append(E(entry.Notes)).Append("</td></tr>");
			}
			sb.Append("<tr><th colspan=\"6\" class=\"num\">Total hours</th><th class=\"num\">").Append(total.ToString("0.00", CultureInfo.InvariantCulture)).Append("</th><th colspan=\"4\"></th></tr></table>");
			if (!string.IsNullOrWhiteSpace(report.Notes)) sb.Append("<h2>Notes</h2><p>").Append(E(report.Notes).Replace("\n", "<br/>")).Append("</p>");

			sb.Append("<h2>Signatures</h2><table class=\"sig\"><tr><td>Contractor: ").Append(E(contractorSignerName)).Append(report.ContractorSignedOn.HasValue ? " · " + D(report.ContractorSignedOn) : string.Empty)
			  .Append("</td><td>Customer: ").Append(E(report.CustomerSignerName)).Append(report.CustomerSignedOn.HasValue ? " · " + D(report.CustomerSignedOn) : string.Empty).Append("</td></tr></table>");
			sb.Append("<p class=\"muted\">Generated ").Append(D(DateTime.UtcNow)).Append("</p></body></html>");
			return sb.ToString();
		}

		public async Task<string> ExportTimeEntriesCsvAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deploymentService.GetDeploymentByIdAsync(deploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");
			var reports = (await _reports.GetByDeploymentAsync(deploymentId))?.Where(r => r.DepartmentId == departmentId).ToDictionary(r => r.DeploymentTimeReportId) ?? new Dictionary<string, DeploymentTimeReport>();
			var entries = (await _entries.GetByDeploymentAsync(deploymentId))?.Where(e => e.DepartmentId == departmentId && reports.ContainsKey(e.DeploymentTimeReportId)).ToList() ?? new List<DeploymentTimeEntry>();
			var names = RosterSubjects(deployment);
			static string C(object value)
			{
				var text = value switch { null => string.Empty, DateTime d => d.ToString("o", CultureInfo.InvariantCulture), decimal m => m.ToString(CultureInfo.InvariantCulture), _ => value.ToString() };
				// A leading =, +, -, @, tab or CR makes Excel/Sheets evaluate the cell as a formula on import (the
				// RecordsExportRenderer guard). Only free text can carry one: numbers and dates are formatted above, so a
				// negative deduction stays numeric.
				if (value is string && (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r')))
					text = "'" + text;
				return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
			}
			var sb = new StringBuilder();
			sb.AppendLine("ReportNumber,ReportDate,ReportStatus,IncidentNumber,ResourceOrderNumber,RequestNumber,CostCode,SubjectType,SubjectId,Subject,EntryType,StartUtc,EndUtc,PaidBreakMinutes,UnpaidBreakMinutes,Hours,CrewSize,CertificationCode,MileageKm,FuelDeductionLitres,AgencyMeals,AgencyAccommodation,Notes,InvoiceId");
			foreach (var entry in entries.OrderBy(e => reports[e.DeploymentTimeReportId].ReportDate).ThenBy(e => reports[e.DeploymentTimeReportId].ReportNumber).ThenBy(e => e.SortOrder))
			{
				var report = reports[entry.DeploymentTimeReportId];
				sb.AppendLine(string.Join(",", new[]
				{
					C(report.ReportNumber), C(report.ReportDate.ToString("yyyy-MM-dd")), C(((DeploymentTimeReportStatuses)report.Status).ToString()), C(report.IncidentNumber), C(report.ResourceOrderNumber), C(report.RequestNumber), C(report.CostCode),
					C(((DeploymentTimeSubjectTypes)entry.SubjectType).ToString()), C(entry.SubjectId), C(entry.SubjectId != null && names.TryGetValue(entry.SubjectId, out var name) ? name : null), C(((DeploymentTimeEntryTypes)entry.EntryType).ToString()),
					C(entry.StartTime), C(entry.EndTime), C(entry.PaidBreakMinutes), C(entry.UnpaidBreakMinutes), C(entry.Hours), C(entry.CrewSizeSnapshot), C(entry.CertificationCode), C(entry.MileageKm), C(entry.FuelDeductionLitres),
					C(entry.AgencySuppliedMeals), C(entry.AgencySuppliedAccommodation), C(entry.Notes), C(report.InvoiceId)
				}));
			}
			return sb.ToString();
		}

		#endregion

		#region Expenses

		public async Task<List<DeploymentExpense>> GetExpensesAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null) return new List<DeploymentExpense>();
			var rows = (await _expenses.GetByDeploymentAsync(deploymentId))?.Where(e => e.DepartmentId == departmentId).ToList() ?? new List<DeploymentExpense>();
			return rows;
		}

		public async Task<DeploymentExpense> GetExpenseByIdAsync(string deploymentExpenseId, int departmentId)
		{
			var row = await _expenses.GetByIdForDepartmentAsync(deploymentExpenseId, departmentId);
			if (row == null || row.IsDeleted) return null;
			return row;
		}

		public async Task<DeploymentExpense> SaveExpenseAsync(DeploymentExpense expense, byte[] receipt, string receiptFileName, string receiptContentType, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (expense == null) throw new ArgumentNullException(nameof(expense));
			if (expense.Amount < 0) throw new InvalidOperationException("expenses_amount_invalid");
			if (!Enum.IsDefined(typeof(DeploymentExpenseTypes), expense.ExpenseType)) throw new InvalidOperationException("expenses_type_invalid");
			var deployment = await _deployments.GetByIdForDepartmentAsync(expense.DeploymentId, expense.DepartmentId);
			if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("deployments_not_found");
			if (!string.IsNullOrWhiteSpace(expense.DeploymentTimeReportId))
			{
				var report = await _reports.GetByIdForDepartmentAsync(expense.DeploymentTimeReportId, expense.DepartmentId);
				if (report == null || report.DeploymentId != expense.DeploymentId) throw new InvalidOperationException("timereports_not_found");
			}

			var isNew = string.IsNullOrWhiteSpace(expense.DeploymentExpenseId);
			DeploymentExpense existing = null;
			if (!isNew)
			{
				existing = await _expenses.GetByIdForDepartmentAsync(expense.DeploymentExpenseId, expense.DepartmentId);
				if (existing == null || existing.IsDeleted) throw new InvalidOperationException("expenses_not_found");
				// The callers authorize the submitted deployment; a row that belongs to another deployment is not found from
				// there, so an expense can neither be rewritten nor re-linked across deployments.
				if (!string.Equals(existing.DeploymentId, expense.DeploymentId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expenses_not_found");
				expense.ReceiptAttachmentId ??= existing.ReceiptAttachmentId;
				expense.AddedOn = existing.AddedOn;
				expense.AddedByUserId = existing.AddedByUserId;
				expense.EditedOn = DateTime.UtcNow;
				expense.EditedByUserId = userId;
			}
			else
			{
				expense.DeploymentExpenseId = null;
				expense.AddedOn = DateTime.UtcNow;
				expense.AddedByUserId = userId;
			}
			expense.IsDeleted = false;
			expense.Currency = string.IsNullOrWhiteSpace(expense.Currency) ? deployment.Currency : expense.Currency.Trim().ToUpperInvariant();
			expense.Description = Trim(expense.Description);
			expense.MealCode = Trim(expense.MealCode);
			expense.City = Trim(expense.City);
			expense.ExpenseDate = expense.ExpenseDate == default ? DateTime.UtcNow.Date : expense.ExpenseDate.Date;

			if (receipt != null && receipt.Length > 0)
			{
				var attachment = await _deploymentService.SaveAttachmentAsync(new DeploymentAttachment
				{
					DeploymentId = expense.DeploymentId, DepartmentId = expense.DepartmentId, AttachmentType = (int)DeploymentAttachmentTypes.Receipt,
					Name = $"Receipt {expense.ExpenseDate:yyyy-MM-dd}", FileName = Trim(receiptFileName) ?? "receipt", FileType = Trim(receiptContentType) ?? "application/octet-stream", Data = receipt
				}, userId, ipAddress, userAgent, cancellationToken);
				expense.ReceiptAttachmentId = attachment.DeploymentAttachmentId;
			}

			var before = existing == null ? null : DeploymentService.Snapshot(existing);
			var saved = await _expenses.SaveOrUpdateAsync(expense, cancellationToken);
			Audit(expense.DepartmentId, userId, isNew ? AuditLogTypes.DeploymentExpenseAdded : AuditLogTypes.DeploymentExpenseUpdated, ipAddress, userAgent, before, saved);
			if (isNew && Core != null) await Core.PublishExpenseAsync(deployment, saved, cancellationToken);
			return await GetExpenseByIdAsync(saved.DeploymentExpenseId, expense.DepartmentId);
		}

		public async Task<bool> DeleteExpenseAsync(string deploymentExpenseId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _expenses.GetByIdForDepartmentAsync(deploymentExpenseId, departmentId);
			if (row == null || row.IsDeleted) return false;
			if (!string.IsNullOrWhiteSpace(row.DeploymentTimeReportId))
			{
				var report = await _reports.GetByIdForDepartmentAsync(row.DeploymentTimeReportId, departmentId);
				if (report != null && report.Status is (int)DeploymentTimeReportStatuses.Approved or (int)DeploymentTimeReportStatuses.Billed) throw new InvalidOperationException("timereports_locked");
			}
			var before = DeploymentService.Snapshot(row);
			row.IsDeleted = true;
			row.EditedOn = DateTime.UtcNow;
			row.EditedByUserId = userId;
			await _expenses.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DeploymentExpenseRemoved, ipAddress, userAgent, before, row);
			return true;
		}

		#endregion

		#region Helpers

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : DeploymentService.Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		private static DateTime ToUtc(DateTime local, string timeZone)
		{
			try { return string.IsNullOrWhiteSpace(timeZone) ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : DateTimeHelpers.ConvertToUtc(local, timeZone, true); }
			catch { return DateTime.SpecifyKind(local, DateTimeKind.Utc); }
		}

		private static DateTime ToLocal(DateTime utc, string timeZone)
		{
			try { return string.IsNullOrWhiteSpace(timeZone) ? utc : DateTimeHelpers.GetLocalDateTime(utc, timeZone); }
			catch { return utc; }
		}

		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null)
				return await action();
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var result = await action();
				_unitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		#endregion
	}
}
