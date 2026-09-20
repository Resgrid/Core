using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Services.CostRecovery
{
	/// <summary>
	/// Cal OES MARS / CFAA cost recovery (Workforce &amp; Business Operations plan, Phase C-M3; C4, C11; decisions
	/// 36-38). Prepares agency / resource / rate / agreement readiness, projects F-42 and expense-claim work items from
	/// the immutable deployment, order, fill, roster, DTR and expense facts, validates them against the pinned
	/// authority profile, calculates expected reimbursement through the pure calculator, renders the no-store handoff
	/// view, and reconciles what a MARS manager observed in the portal. It never writes to MARS (the P0 gateway is
	/// manual), never stores a portal credential, and never marks a record submitted from a handoff or a download.
	/// MARS records are never Phase B invoices (decision 36). Callers authorize.
	/// </summary>
	public partial class CalOesMarsService : ICalOesMarsService
	{
		private static readonly HashSet<int> RemindedToday = new HashSet<int>();

		private readonly ICalOesMarsAgencyProfileRepository _agencies;
		private readonly ICalOesMarsResourceProfileRepository _resources;
		private readonly ICalOesMarsRateProfileRepository _rateProfiles;
		private readonly ICalOesMarsRateLineRepository _rateLines;
		private readonly ICalOesMarsAdministrativeRateInputRepository _adminInputs;
		private readonly ICalOesMarsAgreementSnapshotRepository _agreements;
		private readonly ICalOesMarsWorkItemRepository _workItems;
		private readonly ICalOesMarsReimbursementLineRepository _lines;
		private readonly IDeploymentService _deploymentService;
		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentTimeEntryRepository _timeEntries;
		private readonly IUnitsService _unitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly ICalOesMarsReimbursementCalculator _calculator;
		private readonly ICalOesMarsExternalGateway _gateway;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IDepartmentSettingsService> _departmentSettings;

		public CalOesMarsService(ICalOesMarsAgencyProfileRepository agencies, ICalOesMarsResourceProfileRepository resources, ICalOesMarsRateProfileRepository rateProfiles,
			ICalOesMarsRateLineRepository rateLines, ICalOesMarsAdministrativeRateInputRepository adminInputs, ICalOesMarsAgreementSnapshotRepository agreements,
			ICalOesMarsWorkItemRepository workItems, ICalOesMarsReimbursementLineRepository lines, IDeploymentService deploymentService, ITimeTrackingService timeTracking,
			IDeploymentTimeEntryRepository timeEntries, IUnitsService unitsService, IUserProfileService userProfileService, IDepartmentsService departmentsService,
			IEventAggregator eventAggregator, IUnitOfWork unitOfWork, ICalOesMarsReimbursementCalculator calculator, ICalOesMarsExternalGateway gateway,
			Lazy<ICommunicationService> communication = null, Lazy<IDepartmentSettingsService> departmentSettings = null)
		{
			_agencies = agencies;
			_resources = resources;
			_rateProfiles = rateProfiles;
			_rateLines = rateLines;
			_adminInputs = adminInputs;
			_agreements = agreements;
			_workItems = workItems;
			_lines = lines;
			_deploymentService = deploymentService;
			_timeTracking = timeTracking;
			_timeEntries = timeEntries;
			_unitsService = unitsService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_calculator = calculator;
			_gateway = gateway;
			_communication = communication;
			_departmentSettings = departmentSettings;
		}

		#region Readiness and agency

		public async Task<CalOesMarsReadiness> GetAgencyReadinessAsync(int departmentId, DateTime? dispatchOn = null)
		{
			var asOf = (dispatchOn ?? DateTime.UtcNow).Date;
			var profile = CalOesMarsAuthorityProfile.ForDispatch(asOf);
			var readiness = new CalOesMarsReadiness { AsOf = asOf, AuthorityProfileCode = profile?.Code, AuthorityProfileCurrent = profile != null && profile.IsReviewed };
			if (profile == null) readiness.Items.Add(Item("authority", CalOesMarsReadinessSeverities.Blocker, "ReadinessAuthorityMissing", asOf.ToString("yyyy-MM-dd"), "Agency"));

			var agency = await GetAgencyProfileAsync(departmentId);
			readiness.Agency = agency;
			if (agency == null) readiness.Items.Add(Item("agency", CalOesMarsReadinessSeverities.Blocker, "ReadinessAgencyMissing", null, "Agency"));
			else
			{
				if (string.IsNullOrWhiteSpace(agency.MacsDesignator)) readiness.Items.Add(Item("macs", CalOesMarsReadinessSeverities.Blocker, "ReadinessMacsMissing", null, "Agency"));
				if (string.IsNullOrWhiteSpace(agency.FeinReference)) readiness.Items.Add(Item("fein", CalOesMarsReadinessSeverities.Warning, "ReadinessFeinMissing", null, "Agency"));
				if (string.IsNullOrWhiteSpace(agency.UeiReference)) readiness.Items.Add(Item("uei", CalOesMarsReadinessSeverities.Warning, "ReadinessUeiMissing", null, "Agency"));
				if (string.IsNullOrWhiteSpace(agency.FiscalSupplierReference)) readiness.Items.Add(Item("fiscal", CalOesMarsReadinessSeverities.Warning, "ReadinessFiscalMissing", null, "Agency"));
				if (!agency.VerifiedOn.HasValue || agency.VerifiedOn.Value < asOf.AddDays(-365)) readiness.Items.Add(Item("verified", CalOesMarsReadinessSeverities.Warning, "ReadinessAgencyUnverified", agency.VerifiedOn?.ToString("yyyy-MM-dd"), "Agency"));
			}

			var resources = (await _resources.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<CalOesMarsResourceProfile>();
			readiness.ResourceProfiles = resources.Count;
			readiness.ResourceMismatches = resources.Count(r => r.ReviewState == (int)CalOesMarsReviewStates.Mismatch);
			if (resources.Count == 0) readiness.Items.Add(Item("resources", CalOesMarsReadinessSeverities.Warning, "ReadinessNoResources", null, "Resources"));
			if (readiness.ResourceMismatches > 0) readiness.Items.Add(Item("resource-mismatch", CalOesMarsReadinessSeverities.Warning, "ReadinessResourceMismatch", readiness.ResourceMismatches.ToString(), "Resources"));

			var effective = (await _rateProfiles.GetEffectiveAsync(departmentId, asOf))?.ToList() ?? new List<CalOesMarsRateProfile>();
			readiness.CurrentRateProfiles = effective;
			var hasSalary = effective.Any(p => p.SubmissionType == (int)CalOesMarsSubmissionTypes.SalarySurvey && (p.BaseRateAccepted || p.Status >= (int)CalOesMarsRateProfileStatuses.Reviewed));
			if (!hasSalary) readiness.Items.Add(Item("salary", CalOesMarsReadinessSeverities.Blocker, "ReadinessSalaryMissing", asOf.Year.ToString(), "Rates"));
			if (!effective.Any(p => p.SubmissionType == (int)CalOesMarsSubmissionTypes.AdministrativeRate || p.AdministrativeRateMethod != (int)CalOesMarsAdministrativeRateMethods.None))
				readiness.Items.Add(Item("administrative", CalOesMarsReadinessSeverities.Warning, "ReadinessAdministrativeMissing", null, "Rates"));
			if (!effective.Any(p => p.SubmissionType == (int)CalOesMarsSubmissionTypes.RateLetter)) readiness.Items.Add(Item("rate-letter", CalOesMarsReadinessSeverities.Warning, "ReadinessRateLetterMissing", null, "Rates"));
			foreach (var expiring in effective.Where(p => p.ExpiresOn.HasValue && p.ExpiresOn.Value.Date <= asOf.AddDays(Config.CostRecoveryConfig.AnnualDeadlineLeadDays)))
				readiness.Items.Add(Item("rate-expiring:" + expiring.CalOesMarsRateProfileId, CalOesMarsReadinessSeverities.Warning, "ReadinessRateExpiring", $"{(CalOesMarsSubmissionTypes)expiring.SubmissionType} {expiring.SubmissionYear} → {expiring.ExpiresOn:yyyy-MM-dd}", "Rates"));
			foreach (var unsigned in effective.Where(p => p.Status < (int)CalOesMarsRateProfileStatuses.SignedLocally && !p.BaseRateAccepted && p.SubmissionType != (int)CalOesMarsSubmissionTypes.RateLetter))
				readiness.Items.Add(Item("rate-unsigned:" + unsigned.CalOesMarsRateProfileId, CalOesMarsReadinessSeverities.Warning, "ReadinessRateUnsigned", $"{(CalOesMarsSubmissionTypes)unsigned.SubmissionType} {unsigned.SubmissionYear}", "Rates"));

			var agreements = (await _agreements.GetForDepartmentAsync(departmentId))?.Where(a => a.CoversDate(asOf)).ToList() ?? new List<CalOesMarsAgreementSnapshot>();
			readiness.CurrentAgreements = agreements;
			if (agreements.Count == 0) readiness.Items.Add(Item("agreement", CalOesMarsReadinessSeverities.Blocker, "ReadinessAgreementMissing", asOf.ToString("yyyy-MM-dd"), "Agreements"));
			foreach (var expiring in agreements.Where(a => a.EndOn.HasValue && a.EndOn.Value.Date <= asOf.AddDays(Config.CostRecoveryConfig.AgreementExpiryLeadDays)))
				readiness.Items.Add(Item("agreement-expiring:" + expiring.CalOesMarsAgreementSnapshotId, CalOesMarsReadinessSeverities.Warning, "ReadinessAgreementExpiring", $"{expiring.ClassificationTitle ?? expiring.ClassificationCode ?? "*"} → {expiring.EndOn:yyyy-MM-dd}", "Agreements"));

			var queue = (await _workItems.GetActionQueueAsync(departmentId))?.ToList() ?? new List<CalOesMarsWorkItem>();
			readiness.OpenWorkItems = queue.Count;
			readiness.ReturnedWorkItems = queue.Count(w => w.LocalState == (int)CalOesMarsLocalStates.ReturnedForAgencyReview);
			readiness.InvoicesAwaitingLocalApproval = queue.Count(w => w.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice && w.LocalState == (int)CalOesMarsLocalStates.PendingLocalAgencyApproval);
			if (readiness.ReturnedWorkItems > 0) readiness.Items.Add(Item("returned", CalOesMarsReadinessSeverities.Warning, "ReadinessReturnedItems", readiness.ReturnedWorkItems.ToString(), "Queue"));
			if (readiness.InvoicesAwaitingLocalApproval > 0) readiness.Items.Add(Item("invoices", CalOesMarsReadinessSeverities.Warning, "ReadinessInvoicesAwaiting", readiness.InvoicesAwaitingLocalApproval.ToString(), "Reconciliation"));
			return readiness;
		}

		private static CalOesMarsReadinessItem Item(string key, CalOesMarsReadinessSeverities severity, string messageKey, string detail, string area) =>
			new CalOesMarsReadinessItem { Key = key, Severity = (int)severity, MessageKey = messageKey, Detail = detail, Area = area };

		public async Task<CalOesMarsAgencyProfile> GetAgencyProfileAsync(int departmentId)
		{
			var agency = await _agencies.GetByDepartmentAsync(departmentId);
			return agency == null || agency.IsDeleted ? null : agency;
		}

		public async Task<CalOesMarsAgencyProfile> SaveAgencyProfileAsync(CalOesMarsAgencyProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (string.IsNullOrWhiteSpace(profile.AgencyName)) throw new InvalidOperationException("calmars_agency_name_required");
			var now = DateTime.UtcNow;
			var existing = await _agencies.GetByDepartmentAsync(profile.DepartmentId);
			var before = existing == null ? null : Snapshot(existing);
			var target = existing ?? new CalOesMarsAgencyProfile { DepartmentId = profile.DepartmentId, AddedOn = now, AddedByUserId = userId, AuthorityProfileCode = CalOesMarsAuthorityProfile.Current.Code };
			var changedIdentity = existing != null && (Trim(existing.MacsDesignator) != Trim(profile.MacsDesignator) || Trim(existing.FeinReference) != Trim(profile.FeinReference) || Trim(existing.UeiReference) != Trim(profile.UeiReference) || Trim(existing.FiscalSupplierReference) != Trim(profile.FiscalSupplierReference));
			target.AuthorityProfileCode = string.IsNullOrWhiteSpace(profile.AuthorityProfileCode) ? target.AuthorityProfileCode ?? CalOesMarsAuthorityProfile.Current.Code : profile.AuthorityProfileCode;
			target.MacsDesignator = Trim(profile.MacsDesignator)?.ToUpperInvariant();
			target.AgencyName = profile.AgencyName.Trim();
			target.AgencyCategory = Trim(profile.AgencyCategory);
			target.ContactName = Trim(profile.ContactName);
			target.ContactPhone = Trim(profile.ContactPhone);
			target.ContactEmail = Trim(profile.ContactEmail);
			target.Address = Trim(profile.Address);
			target.FeinReference = Trim(profile.FeinReference);
			target.UeiReference = Trim(profile.UeiReference);
			target.SamReference = Trim(profile.SamReference);
			target.FiscalSupplierReference = Trim(profile.FiscalSupplierReference);
			target.PortalAccountRole = Trim(profile.PortalAccountRole);
			target.PortalAccountReference = Trim(profile.PortalAccountReference);
			target.SourceArtifact = Trim(profile.SourceArtifact);
			target.SourceChecksum = Trim(profile.SourceChecksum);
			target.IsActive = profile.IsActive;
			target.IsDeleted = false;
			// An identifier change invalidates the last verification; the readiness dashboard asks for a fresh check.
			if (changedIdentity) { target.VerifiedOn = null; target.VerifiedByUserId = null; }
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _agencies.SaveOrUpdateAsync(target, cancellationToken);
			Audit(profile.DepartmentId, userId, AuditLogTypes.CalOesMarsAgencyProfileChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<CalOesMarsAgencyProfile> MarkAgencyVerifiedAsync(int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var agency = await GetAgencyProfileAsync(departmentId) ?? throw new InvalidOperationException("calmars_agency_not_found");
			var before = Snapshot(agency);
			agency.VerifiedOn = DateTime.UtcNow;
			agency.VerifiedByUserId = userId;
			agency.EditedOn = agency.VerifiedOn;
			agency.EditedByUserId = userId;
			var saved = await _agencies.SaveOrUpdateAsync(agency, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsAgencyProfileChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		#endregion

		#region F-5 resource inventory crosswalk

		public async Task<List<CalOesMarsResourceProfile>> GetResourceProfilesAsync(int departmentId)
		{
			var rows = (await _resources.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<CalOesMarsResourceProfile>();
			await NameResourcesAsync(rows, departmentId);
			return rows;
		}

		public async Task<CalOesMarsResourceProfile> GetResourceProfileAsync(string resourceProfileId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(resourceProfileId)) return null;
			var row = await _resources.GetByIdForDepartmentAsync(resourceProfileId, departmentId);
			if (row == null || row.IsDeleted) return null;
			await NameResourcesAsync(new[] { row }, departmentId);
			return row;
		}

		public async Task<List<CalOesMarsResourceProfile>> BuildResourceInventoryF5DraftAsync(int departmentId, IEnumerable<int> unitIds, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var ids = (unitIds ?? Enumerable.Empty<int>()).Distinct().ToList();
			var result = new List<CalOesMarsResourceProfile>();
			if (ids.Count == 0) return result;
			var existing = (await _resources.GetByUnitIdsAsync(departmentId, ids))?.ToDictionary(r => r.UnitId ?? 0) ?? new Dictionary<int, CalOesMarsResourceProfile>();
			var units = (await _unitsService.GetUnitsForDepartmentAsync(departmentId))?.Where(u => ids.Contains(u.UnitId)).ToList() ?? new List<Unit>();
			var now = DateTime.UtcNow;
			foreach (var unit in units)
			{
				if (existing.TryGetValue(unit.UnitId, out var current)) { result.Add(current); continue; }
				// A draft crosswalk row: identity copied from the Unit at this moment (the Unit itself is never written).
				var draft = new CalOesMarsResourceProfile
				{
					DepartmentId = departmentId, SubjectType = (int)CalOesMarsSubjectTypes.Unit, UnitId = unit.UnitId, UnitDesignator = unit.Name, ResourceType = GuessResourceType(unit.Type),
					ResourceKind = "Apparatus", CodeScheme = "MARS-F5", LicensePlate = Trim(unit.PlateNumber), Vin = Trim(unit.VIN), Ownership = (int)CalOesMarsOwnerships.LocalAgency,
					ReviewState = (int)CalOesMarsReviewStates.Draft, EffectiveOn = now.Date, AddedOn = now, AddedByUserId = userId
				};
				var saved = await _resources.SaveOrUpdateAsync(draft, cancellationToken);
				Audit(departmentId, userId, AuditLogTypes.CalOesMarsResourceProfileChanged, ipAddress, userAgent, null, saved);
				result.Add(saved);
			}
			await NameResourcesAsync(result, departmentId);
			return result;
		}

		private static string GuessResourceType(string unitType)
		{
			var type = (unitType ?? string.Empty).Trim();
			if (type.Length == 0) return null;
			return CalOesMarsAuthorityProfile.Current.ResourceTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
				?? CalOesMarsAuthorityProfile.Current.ResourceTypes.FirstOrDefault(t => t.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		public async Task<CalOesMarsResourceProfile> SaveResourceProfileAsync(CalOesMarsResourceProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (!Enum.IsDefined(typeof(CalOesMarsSubjectTypes), profile.SubjectType)) throw new InvalidOperationException("calmars_resource_subject_invalid");
			if (profile.SubjectType == (int)CalOesMarsSubjectTypes.Unit && !profile.UnitId.HasValue) throw new InvalidOperationException("calmars_resource_unit_required");
			if (profile.SubjectType == (int)CalOesMarsSubjectTypes.External && string.IsNullOrWhiteSpace(profile.ExternalResourceName)) throw new InvalidOperationException("calmars_resource_name_required");
			if (profile.ExpiresOn.HasValue && profile.EffectiveOn.HasValue && profile.ExpiresOn < profile.EffectiveOn) throw new InvalidOperationException("calmars_dates_invalid");
			if (profile.UnitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(profile.UnitId.Value);
				if (unit == null || unit.DepartmentId != profile.DepartmentId) throw new InvalidOperationException("calmars_resource_unit_not_found");
			}
			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(profile.CalOesMarsResourceProfileId) ? null : await _resources.GetByIdForDepartmentAsync(profile.CalOesMarsResourceProfileId, profile.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("calmars_resource_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var target = existing ?? new CalOesMarsResourceProfile { DepartmentId = profile.DepartmentId, AddedOn = now, AddedByUserId = userId, ReviewState = (int)CalOesMarsReviewStates.Draft };
			target.SubjectType = profile.SubjectType;
			target.UnitId = profile.SubjectType == (int)CalOesMarsSubjectTypes.Unit ? profile.UnitId : null;
			target.InventoryAssetId = profile.SubjectType == (int)CalOesMarsSubjectTypes.InventoryAsset ? Trim(profile.InventoryAssetId) : null;
			target.ExternalResourceName = Trim(profile.ExternalResourceName);
			target.MarsResourceId = Trim(profile.MarsResourceId);
			target.ResourceType = Trim(profile.ResourceType);
			target.ResourceKind = Trim(profile.ResourceKind) ?? "Apparatus";
			target.CodeScheme = Trim(profile.CodeScheme) ?? "MARS-F5";
			target.UnitDesignator = Trim(profile.UnitDesignator);
			target.LicensePlate = Trim(profile.LicensePlate);
			target.Vin = Trim(profile.Vin);
			target.SerialNumber = Trim(profile.SerialNumber);
			target.Ownership = Enum.IsDefined(typeof(CalOesMarsOwnerships), profile.Ownership) ? profile.Ownership : (int)CalOesMarsOwnerships.LocalAgency;
			target.EffectiveOn = profile.EffectiveOn;
			target.ExpiresOn = profile.ExpiresOn;
			target.SourceArtifact = Trim(profile.SourceArtifact);
			target.SourceChecksum = Trim(profile.SourceChecksum);
			if (existing != null)
			{
				// An edit after an observation needs a fresh look against MARS.
				if (existing.ReviewState == (int)CalOesMarsReviewStates.Observed) target.ReviewState = (int)CalOesMarsReviewStates.Reviewed;
				else if (profile.ReviewState == (int)CalOesMarsReviewStates.Reviewed || profile.ReviewState == (int)CalOesMarsReviewStates.Draft) target.ReviewState = profile.ReviewState;
				target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId;
			}
			else if (profile.ReviewState == (int)CalOesMarsReviewStates.Reviewed) target.ReviewState = profile.ReviewState;
			var saved = await _resources.SaveOrUpdateAsync(target, cancellationToken);
			Audit(profile.DepartmentId, userId, AuditLogTypes.CalOesMarsResourceProfileChanged, ipAddress, userAgent, before, saved);
			await NameResourcesAsync(new[] { saved }, profile.DepartmentId);
			return saved;
		}

		public async Task<CalOesMarsResourceProfile> RecordResourceObservationAsync(string resourceProfileId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			var row = await GetResourceProfileAsync(resourceProfileId, departmentId) ?? throw new InvalidOperationException("calmars_resource_not_found");
			var before = Snapshot(row);
			row.MarsResourceId = Trim(observation.ExternalId) ?? row.MarsResourceId;
			row.ObservedExternalStatus = Trim(observation.ExternalStatus);
			row.ObservedOn = observation.ObservedOn ?? DateTime.UtcNow;
			row.ReviewState = string.Equals(observation.ExternalStatus, "mismatch", StringComparison.OrdinalIgnoreCase) ? (int)CalOesMarsReviewStates.Mismatch : (int)CalOesMarsReviewStates.Observed;
			row.SourceChecksum = Trim(observation.ArtifactChecksum) ?? row.SourceChecksum;
			row.RowVersion++; row.EditedOn = DateTime.UtcNow; row.EditedByUserId = userId;
			var saved = await _resources.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsResourceProfileChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<bool> DeleteResourceProfileAsync(string resourceProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await GetResourceProfileAsync(resourceProfileId, departmentId);
			if (row == null) return false;
			var before = Snapshot(row);
			row.IsDeleted = true; row.EditedOn = DateTime.UtcNow; row.EditedByUserId = userId;
			await _resources.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsResourceProfileChanged, ipAddress, userAgent, before, row);
			return true;
		}

		private async Task NameResourcesAsync(IEnumerable<CalOesMarsResourceProfile> rows, int departmentId)
		{
			var list = rows?.ToList() ?? new List<CalOesMarsResourceProfile>();
			if (list.Count == 0) return;
			Dictionary<int, string> units = null;
			foreach (var row in list)
			{
				if (row.UnitId.HasValue)
				{
					units ??= (await _unitsService.GetUnitsForDepartmentAsync(departmentId))?.ToDictionary(u => u.UnitId, u => u.Name) ?? new Dictionary<int, string>();
					row.SubjectName = units.TryGetValue(row.UnitId.Value, out var name) ? name : row.UnitDesignator;
				}
				else row.SubjectName = row.ExternalResourceName ?? row.UnitDesignator ?? row.InventoryAssetId;
			}
		}

		#endregion

		#region Annual rate profiles

		public async Task<List<CalOesMarsRateProfile>> GetRateProfilesAsync(int departmentId, int? submissionYear = null) =>
			(await _rateProfiles.GetForDepartmentAsync(departmentId, submissionYear))?.ToList() ?? new List<CalOesMarsRateProfile>();

		public async Task<CalOesMarsRateProfile> GetRateProfileAsync(string rateProfileId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(rateProfileId)) return null;
			var profile = await _rateProfiles.GetByIdForDepartmentAsync(rateProfileId, departmentId);
			if (profile == null || profile.IsDeleted) return null;
			profile.Lines = (await _rateLines.GetByProfileAsync(rateProfileId))?.ToList() ?? new List<CalOesMarsRateLine>();
			profile.AdministrativeInputs = (await _adminInputs.GetByProfileAsync(rateProfileId))?.ToList() ?? new List<CalOesMarsAdministrativeRateInput>();
			return profile;
		}

		public async Task<CalOesMarsRateProfile> SaveRateProfileAsync(CalOesMarsRateProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (profile.SubmissionYear < 2000 || profile.SubmissionYear > 2100) throw new InvalidOperationException("calmars_rate_year_invalid");
			if (!Enum.IsDefined(typeof(CalOesMarsSubmissionTypes), profile.SubmissionType)) throw new InvalidOperationException("calmars_rate_type_invalid");
			if (profile.ExpiresOn.HasValue && profile.EffectiveOn.HasValue && profile.ExpiresOn < profile.EffectiveOn) throw new InvalidOperationException("calmars_dates_invalid");
			if (profile.AdministrativeRateValue.HasValue && (profile.AdministrativeRateValue < 0 || profile.AdministrativeRateValue > 100)) throw new InvalidOperationException("calmars_rate_percent_invalid");
			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(profile.CalOesMarsRateProfileId) ? null : await _rateProfiles.GetByIdForDepartmentAsync(profile.CalOesMarsRateProfileId, profile.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("calmars_rate_not_found");
			if (existing != null && !existing.IsEditable) throw new InvalidOperationException("calmars_rate_locked");
			var before = existing == null ? null : Snapshot(existing);
			var target = existing ?? new CalOesMarsRateProfile { DepartmentId = profile.DepartmentId, Status = (int)CalOesMarsRateProfileStatuses.Draft, AddedOn = now, AddedByUserId = userId, AuthorityProfileCode = CalOesMarsAuthorityProfile.Current.Code };
			target.SubmissionYear = profile.SubmissionYear;
			target.SubmissionType = profile.SubmissionType;
			target.EffectiveOn = profile.EffectiveOn ?? new DateTime(profile.SubmissionYear, 1, 1);
			target.ExpiresOn = profile.ExpiresOn;
			target.BaseRateAccepted = profile.BaseRateAccepted;
			target.AdministrativeRateMethod = Enum.IsDefined(typeof(CalOesMarsAdministrativeRateMethods), profile.AdministrativeRateMethod) ? profile.AdministrativeRateMethod : (int)CalOesMarsAdministrativeRateMethods.None;
			target.AdministrativeRateValue = target.AdministrativeRateMethod == (int)CalOesMarsAdministrativeRateMethods.None ? null : profile.AdministrativeRateValue;
			target.AuthorityProfileCode = Trim(profile.AuthorityProfileCode) ?? target.AuthorityProfileCode;
			target.SourceUrl = Trim(profile.SourceUrl);
			target.SourceDate = profile.SourceDate;
			target.SourceArtifact = Trim(profile.SourceArtifact);
			target.SourceChecksum = Trim(profile.SourceChecksum);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _rateProfiles.SaveOrUpdateAsync(target, cancellationToken);
			Audit(profile.DepartmentId, userId, AuditLogTypes.CalOesMarsRateProfileChanged, ipAddress, userAgent, before, saved);
			return await GetRateProfileAsync(saved.CalOesMarsRateProfileId, profile.DepartmentId);
		}

		public async Task<CalOesMarsRateProfile> SaveRateLinesAsync(string rateProfileId, int departmentId, List<CalOesMarsRateLine> lines, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await GetRateProfileAsync(rateProfileId, departmentId) ?? throw new InvalidOperationException("calmars_rate_not_found");
			if (!profile.IsEditable) throw new InvalidOperationException("calmars_rate_locked");
			var before = Snapshot(profile);
			var incoming = (lines ?? new List<CalOesMarsRateLine>()).Where(l => l != null).ToList();
			foreach (var line in incoming)
			{
				if (!Enum.IsDefined(typeof(CalOesMarsRateLineKinds), line.LineKind)) throw new InvalidOperationException("calmars_rate_line_kind_invalid");
				if (!Enum.IsDefined(typeof(CalOesMarsRateBases), line.Basis)) throw new InvalidOperationException("calmars_rate_line_basis_invalid");
				if ((line.StraightRate ?? 0) < 0 || (line.OvertimeRate ?? 0) < 0) throw new InvalidOperationException("calmars_rate_line_rate_invalid");
				var salary = line.LineKind is (int)CalOesMarsRateLineKinds.SalarySurvey or (int)CalOesMarsRateLineKinds.AttachmentANonSuppression;
				if (salary && string.IsNullOrWhiteSpace(line.ClassificationCode)) throw new InvalidOperationException("calmars_rate_line_classification_required");
				if (!salary && line.LineKind != (int)CalOesMarsRateLineKinds.AdministrativeRate && line.LineKind != (int)CalOesMarsRateLineKinds.PrivatelyOwnedVehicle && line.LineKind != (int)CalOesMarsRateLineKinds.MealLodgingIncidentals && string.IsNullOrWhiteSpace(line.ResourceCode) && string.IsNullOrWhiteSpace(line.FemaCode))
					throw new InvalidOperationException("calmars_rate_line_resource_required");
			}
			await TransactionAsync(async () =>
			{
				var current = profile.Lines.ToDictionary(l => l.CalOesMarsRateLineId, StringComparer.OrdinalIgnoreCase);
				var kept = new HashSet<string>(incoming.Where(l => !string.IsNullOrWhiteSpace(l.CalOesMarsRateLineId) && current.ContainsKey(l.CalOesMarsRateLineId)).Select(l => l.CalOesMarsRateLineId), StringComparer.OrdinalIgnoreCase);
				var now = DateTime.UtcNow;
				foreach (var stale in current.Values.Where(l => !kept.Contains(l.CalOesMarsRateLineId)))
				{
					stale.IsDeleted = true; stale.EditedOn = now; stale.EditedByUserId = userId;
					await _rateLines.SaveOrUpdateAsync(stale, cancellationToken);
				}
				var sort = 0;
				foreach (var line in incoming)
				{
					var existing = !string.IsNullOrWhiteSpace(line.CalOesMarsRateLineId) && current.TryGetValue(line.CalOesMarsRateLineId, out var found) ? found : null;
					var target = existing ?? new CalOesMarsRateLine { CalOesMarsRateProfileId = rateProfileId, DepartmentId = departmentId, AddedOn = now, AddedByUserId = userId };
					target.LineKind = line.LineKind;
					target.ClassificationCode = Trim(line.ClassificationCode);
					target.ResourceCode = Trim(line.ResourceCode);
					target.FemaCode = Trim(line.FemaCode);
					target.Description = Trim(line.Description);
					target.Basis = line.Basis;
					target.StraightRate = line.StraightRate;
					target.OvertimeRate = line.OvertimeRate;
					target.IncludesWorkersComp = line.IncludesWorkersComp;
					target.IncludesUnemploymentInsurance = line.IncludesUnemploymentInsurance;
					target.PortalToPortalEligible = line.PortalToPortalEligible;
					target.OvertimeEligible = line.OvertimeEligible;
					target.Authority = Enum.IsDefined(typeof(CalOesMarsRateAuthorities), line.Authority) ? line.Authority : (int)CalOesMarsRateAuthorities.AgencySubmitted;
					target.SourceInputVersions = Trim(line.SourceInputVersions);
					target.SourceArtifact = Trim(line.SourceArtifact);
					target.SortOrder = sort++;
					if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
					await _rateLines.SaveOrUpdateAsync(target, cancellationToken);
				}
				profile.RowVersion++; profile.EditedOn = now; profile.EditedByUserId = userId;
				await _rateProfiles.SaveOrUpdateAsync(profile, cancellationToken);
				return true;
			}, cancellationToken);
			var reloaded = await GetRateProfileAsync(rateProfileId, departmentId);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsRateProfileChanged, ipAddress, userAgent, before, reloaded);
			return reloaded;
		}

		public async Task<CalOesMarsRateProfile> SaveAdministrativeInputsAsync(string rateProfileId, int departmentId, List<CalOesMarsAdministrativeRateInput> inputs, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await GetRateProfileAsync(rateProfileId, departmentId) ?? throw new InvalidOperationException("calmars_rate_not_found");
			if (!profile.IsEditable) throw new InvalidOperationException("calmars_rate_locked");
			var before = Snapshot(profile);
			var incoming = (inputs ?? new List<CalOesMarsAdministrativeRateInput>()).Where(i => i != null).ToList();
			foreach (var input in incoming)
			{
				if (!Enum.IsDefined(typeof(CalOesMarsCostClassifications), input.Classification)) throw new InvalidOperationException("calmars_admin_input_classification_invalid");
				if (input.FiscalYear < 2000 || input.FiscalYear > profile.SubmissionYear) throw new InvalidOperationException("calmars_admin_input_year_invalid");
				if (input.Amount < 0) throw new InvalidOperationException("calmars_admin_input_amount_invalid");
			}
			await TransactionAsync(async () =>
			{
				var current = profile.AdministrativeInputs.ToDictionary(i => i.CalOesMarsAdministrativeRateInputId, StringComparer.OrdinalIgnoreCase);
				var kept = new HashSet<string>(incoming.Where(i => !string.IsNullOrWhiteSpace(i.CalOesMarsAdministrativeRateInputId) && current.ContainsKey(i.CalOesMarsAdministrativeRateInputId)).Select(i => i.CalOesMarsAdministrativeRateInputId), StringComparer.OrdinalIgnoreCase);
				var now = DateTime.UtcNow;
				foreach (var stale in current.Values.Where(i => !kept.Contains(i.CalOesMarsAdministrativeRateInputId)))
				{
					stale.IsDeleted = true; stale.EditedOn = now; stale.EditedByUserId = userId;
					await _adminInputs.SaveOrUpdateAsync(stale, cancellationToken);
				}
				foreach (var input in incoming)
				{
					var existing = !string.IsNullOrWhiteSpace(input.CalOesMarsAdministrativeRateInputId) && current.TryGetValue(input.CalOesMarsAdministrativeRateInputId, out var found) ? found : null;
					var target = existing ?? new CalOesMarsAdministrativeRateInput { CalOesMarsRateProfileId = rateProfileId, DepartmentId = departmentId, AddedOn = now, AddedByUserId = userId };
					target.FiscalYear = input.FiscalYear;
					target.FunctionCode = Trim(input.FunctionCode);
					target.CategoryCode = Trim(input.CategoryCode);
					target.CategoryProfileVersion = Trim(input.CategoryProfileVersion) ?? CalOesMarsAuthorityProfile.Current.Code;
					target.Classification = input.Classification;
					target.ActualAmount = input.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
					target.SourceSystem = Trim(input.SourceSystem);
					target.SourceLine = Trim(input.SourceLine);
					target.IncidentDirectExclusion = input.IncidentDirectExclusion;
					target.DoubleCountMarker = input.DoubleCountMarker;
					target.ReviewStatus = Enum.IsDefined(typeof(CalOesMarsInputReviewStatuses), input.ReviewStatus) ? input.ReviewStatus : (int)CalOesMarsInputReviewStatuses.Pending;
					target.ReviewReason = Trim(input.ReviewReason);
					target.SourceArtifact = Trim(input.SourceArtifact);
					if (existing != null) { target.InputVersion = existing.InputVersion + 1; target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
					await _adminInputs.SaveOrUpdateAsync(target, cancellationToken);
				}
				return true;
			}, cancellationToken);
			var reloaded = await GetRateProfileAsync(rateProfileId, departmentId);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsRateProfileChanged, ipAddress, userAgent, before, reloaded);
			return reloaded;
		}

		public async Task<CalOesMarsAdministrativeRateDraft> BuildAdministrativeRateDraftAsync(string rateProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await GetRateProfileAsync(rateProfileId, departmentId) ?? throw new InvalidOperationException("calmars_rate_not_found");
			var authority = CalOesMarsAuthorityProfile.Get(profile.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current;
			var draft = BuildAdministrativeRateDraft(profile, authority);
			if (profile.IsEditable && draft.IsReady)
			{
				var before = Snapshot(profile);
				profile.AdministrativeRateMethod = draft.MethodChosen;
				profile.AdministrativeRateValue = draft.ChosenPercent;
				profile.RowVersion++; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
				await _rateProfiles.SaveOrUpdateAsync(profile, cancellationToken);
				Audit(departmentId, userId, AuditLogTypes.CalOesMarsRateDraftBuilt, ipAddress, userAgent, before, profile);
			}
			return draft;
		}

		/// <summary>Allowable indirect ÷ allowable direct from the reviewed inputs (pure; the de-minimis option comes from the authority profile).</summary>
		public static CalOesMarsAdministrativeRateDraft BuildAdministrativeRateDraft(CalOesMarsRateProfile profile, CalOesMarsAuthorityProfile authority)
		{
			var draft = new CalOesMarsAdministrativeRateDraft { RateProfileId = profile.CalOesMarsRateProfileId, DeMinimisPercent = authority.DeMinimisAdministrativePercent };
			var inputs = (profile.AdministrativeInputs ?? new List<CalOesMarsAdministrativeRateInput>()).Where(i => !i.IsDeleted).ToList();
			if (inputs.Count == 0) draft.Blockers.Add("no_inputs");
			if (inputs.Any(i => i.DoubleCountMarker && i.ReviewStatus == (int)CalOesMarsInputReviewStatuses.Pending)) draft.Blockers.Add("double_count_unresolved");
			if (inputs.Any(i => i.ReviewStatus == (int)CalOesMarsInputReviewStatuses.Pending && !i.DoubleCountMarker)) draft.Blockers.Add("inputs_pending_review");
			foreach (var input in inputs.Where(i => i.ReviewStatus == (int)CalOesMarsInputReviewStatuses.Accepted))
			{
				if (input.IncidentDirectExclusion) { draft.ExcludedIncidentDirect += input.Amount; continue; }
				switch ((CalOesMarsCostClassifications)input.Classification)
				{
					case CalOesMarsCostClassifications.Direct: draft.AllowableDirect += input.Amount; break;
					case CalOesMarsCostClassifications.Indirect: draft.AllowableIndirect += input.Amount; break;
					default: draft.ExcludedUnallowable += input.Amount; break;
				}
			}
			if (draft.AllowableDirect > 0) draft.CalculatedPercent = Math.Round(draft.AllowableIndirect / draft.AllowableDirect * 100m, 4, MidpointRounding.AwayFromZero);
			else if (inputs.Count > 0) draft.Blockers.Add("no_direct_base");
			if (draft.IsReady)
			{
				// The larger allowable option is recorded; a department may still override the method on the profile.
				var useCalculated = draft.CalculatedPercent.HasValue && draft.CalculatedPercent.Value > draft.DeMinimisPercent;
				draft.MethodChosen = (int)(useCalculated ? CalOesMarsAdministrativeRateMethods.Calculated : CalOesMarsAdministrativeRateMethods.DeMinimis);
				draft.ChosenPercent = useCalculated ? draft.CalculatedPercent : draft.DeMinimisPercent;
			}
			else if (inputs.Count == 0)
			{
				draft.MethodChosen = (int)CalOesMarsAdministrativeRateMethods.DeMinimis;
				draft.ChosenPercent = draft.DeMinimisPercent;
			}
			return draft;
		}

		public async Task<CalOesMarsRateProfile> SetRateProfileStatusAsync(string rateProfileId, int departmentId, CalOesMarsRateProfileStatuses status, string signedByName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await GetRateProfileAsync(rateProfileId, departmentId) ?? throw new InvalidOperationException("calmars_rate_not_found");
			if (!IsValidRateTransition((CalOesMarsRateProfileStatuses)profile.Status, status)) throw new InvalidOperationException("calmars_rate_transition_invalid");
			if (status == CalOesMarsRateProfileStatuses.SignedLocally && string.IsNullOrWhiteSpace(signedByName)) throw new InvalidOperationException("calmars_rate_signer_required");
			if (status == CalOesMarsRateProfileStatuses.Reviewed && profile.SubmissionType != (int)CalOesMarsSubmissionTypes.RateLetter && !profile.BaseRateAccepted && profile.Lines.Count == 0 && profile.AdministrativeRateMethod == (int)CalOesMarsAdministrativeRateMethods.None)
				throw new InvalidOperationException("calmars_rate_empty");
			var before = Snapshot(profile);
			profile.Status = (int)status;
			if (status == CalOesMarsRateProfileStatuses.SignedLocally) { profile.SignedOn = DateTime.UtcNow; profile.SignedByName = signedByName.Trim(); }
			profile.RowVersion++; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
			await _rateProfiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsRateReviewed, ipAddress, userAgent, before, profile);
			return profile;
		}

		public static bool IsValidRateTransition(CalOesMarsRateProfileStatuses from, CalOesMarsRateProfileStatuses to) => (from, to) switch
		{
			(CalOesMarsRateProfileStatuses.Draft, CalOesMarsRateProfileStatuses.Reviewed) => true,
			(CalOesMarsRateProfileStatuses.Reviewed, CalOesMarsRateProfileStatuses.Draft) => true,
			(CalOesMarsRateProfileStatuses.Reviewed, CalOesMarsRateProfileStatuses.SignedLocally) => true,
			(CalOesMarsRateProfileStatuses.SignedLocally, CalOesMarsRateProfileStatuses.Reviewed) => true,
			(CalOesMarsRateProfileStatuses.SignedLocally, CalOesMarsRateProfileStatuses.SubmittedExternal) => true,
			(CalOesMarsRateProfileStatuses.SubmittedExternal, CalOesMarsRateProfileStatuses.Accepted) => true,
			(CalOesMarsRateProfileStatuses.SubmittedExternal, CalOesMarsRateProfileStatuses.Reviewed) => true,
			(_, CalOesMarsRateProfileStatuses.Superseded) => from != CalOesMarsRateProfileStatuses.Superseded,
			_ => false
		};

		public async Task<CalOesMarsRateProfile> RecordRateProfileObservationAsync(string rateProfileId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			var profile = await GetRateProfileAsync(rateProfileId, departmentId) ?? throw new InvalidOperationException("calmars_rate_not_found");
			if (profile.Status < (int)CalOesMarsRateProfileStatuses.SignedLocally) throw new InvalidOperationException("calmars_rate_not_signed");
			var before = Snapshot(profile);
			profile.ObservedExternalStatus = Trim(observation.ExternalStatus);
			profile.ObservedOn = observation.ObservedOn ?? DateTime.UtcNow;
			profile.SourceChecksum = Trim(observation.ArtifactChecksum) ?? profile.SourceChecksum;
			if (string.Equals(observation.ExternalStatus, "Accepted", StringComparison.OrdinalIgnoreCase) || string.Equals(observation.ExternalStatus, "Approved", StringComparison.OrdinalIgnoreCase)) profile.Status = (int)CalOesMarsRateProfileStatuses.Accepted;
			else if (profile.Status == (int)CalOesMarsRateProfileStatuses.SignedLocally) profile.Status = (int)CalOesMarsRateProfileStatuses.SubmittedExternal;
			profile.RowVersion++; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
			await _rateProfiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsExternalStatusObserved, ipAddress, userAgent, before, profile);
			return profile;
		}

		public async Task<bool> DeleteRateProfileAsync(string rateProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await GetRateProfileAsync(rateProfileId, departmentId);
			if (profile == null) return false;
			if (!profile.IsEditable) throw new InvalidOperationException("calmars_rate_locked");
			var before = Snapshot(profile);
			profile.IsDeleted = true; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
			await _rateProfiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsRateProfileChanged, ipAddress, userAgent, before, profile);
			return true;
		}

		#endregion

		#region Agreements

		public async Task<List<CalOesMarsAgreementSnapshot>> GetAgreementsAsync(int departmentId) => (await _agreements.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<CalOesMarsAgreementSnapshot>();

		public async Task<CalOesMarsAgreementSnapshot> GetAgreementAsync(string agreementSnapshotId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(agreementSnapshotId)) return null;
			var row = await _agreements.GetByIdForDepartmentAsync(agreementSnapshotId, departmentId);
			return row == null || row.IsDeleted ? null : row;
		}

		public async Task<CalOesMarsAgreementSnapshot> SaveAgreementAsync(CalOesMarsAgreementSnapshot agreement, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (agreement == null) throw new ArgumentNullException(nameof(agreement));
			if (!Enum.IsDefined(typeof(CalOesMarsDocumentKinds), agreement.DocumentKind)) throw new InvalidOperationException("calmars_agreement_kind_invalid");
			if (!Enum.IsDefined(typeof(CalOesMarsCompensationMethods), agreement.CompensationMethod)) throw new InvalidOperationException("calmars_agreement_method_invalid");
			if (!Enum.IsDefined(typeof(CalOesMarsOvertimeMethods), agreement.OvertimeMethod)) throw new InvalidOperationException("calmars_agreement_overtime_invalid");
			if (agreement.EndOn.HasValue && agreement.StartOn.HasValue && agreement.EndOn < agreement.StartOn) throw new InvalidOperationException("calmars_dates_invalid");
			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(agreement.CalOesMarsAgreementSnapshotId) ? null : await _agreements.GetByIdForDepartmentAsync(agreement.CalOesMarsAgreementSnapshotId, agreement.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("calmars_agreement_not_found");
			var before = existing == null ? null : Snapshot(existing);
			// A snapshot referenced by an external work item is immutable: the edit becomes a new version.
			var referenced = existing != null && (await _workItems.GetActionQueueAsync(agreement.DepartmentId))?.Any(w => w.AgreementSnapshotId == existing.CalOesMarsAgreementSnapshotId && w.IsExternal) == true;
			var target = existing == null || referenced ? new CalOesMarsAgreementSnapshot { DepartmentId = agreement.DepartmentId, AddedOn = now, AddedByUserId = userId, RowVersion = referenced ? existing.RowVersion + 1 : 1 } : existing;
			target.ClassificationCode = Trim(agreement.ClassificationCode);
			target.ClassificationTitle = Trim(agreement.ClassificationTitle);
			target.DocumentKind = agreement.DocumentKind;
			target.CompensationMethod = agreement.CompensationMethod;
			target.OvertimeMethod = agreement.OvertimeMethod;
			target.StartOn = agreement.StartOn;
			target.EndOn = agreement.EndOn;
			target.AttachmentId = agreement.AttachmentId;
			target.AttachmentChecksum = Trim(agreement.AttachmentChecksum);
			target.SourceArtifact = Trim(agreement.SourceArtifact);
			target.SourceChecksum = Trim(agreement.SourceChecksum);
			if (ReferenceEquals(target, existing)) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _agreements.SaveOrUpdateAsync(target, cancellationToken);
			if (referenced)
			{
				existing.EndOn = existing.EndOn ?? now.Date; existing.EditedOn = now; existing.EditedByUserId = userId;
				await _agreements.SaveOrUpdateAsync(existing, cancellationToken);
			}
			Audit(agreement.DepartmentId, userId, AuditLogTypes.CalOesMarsAgreementChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<CalOesMarsAgreementSnapshot> RecordAgreementObservationAsync(string agreementSnapshotId, int departmentId, CalOesMarsExternalObservation observation, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (observation == null) throw new ArgumentNullException(nameof(observation));
			var row = await GetAgreementAsync(agreementSnapshotId, departmentId) ?? throw new InvalidOperationException("calmars_agreement_not_found");
			var before = Snapshot(row);
			row.ExternalApprovalStatus = Trim(observation.ExternalStatus);
			row.ObservedOn = observation.ObservedOn ?? DateTime.UtcNow;
			row.AttachmentChecksum = Trim(observation.ArtifactChecksum) ?? row.AttachmentChecksum;
			row.RowVersion++; row.EditedOn = DateTime.UtcNow; row.EditedByUserId = userId;
			var saved = await _agreements.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsAgreementObserved, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<bool> DeleteAgreementAsync(string agreementSnapshotId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await GetAgreementAsync(agreementSnapshotId, departmentId);
			if (row == null) return false;
			if ((await _workItems.GetActionQueueAsync(departmentId))?.Any(w => w.AgreementSnapshotId == row.CalOesMarsAgreementSnapshotId) == true) throw new InvalidOperationException("calmars_agreement_in_use");
			var before = Snapshot(row);
			row.IsDeleted = true; row.EditedOn = DateTime.UtcNow; row.EditedByUserId = userId;
			await _agreements.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CalOesMarsAgreementChanged, ipAddress, userAgent, before, row);
			return true;
		}

		public async Task<CalOesMarsAgreementSnapshot> SelectAgreementAsync(int departmentId, string classificationCode, DateTime dispatchOn)
		{
			var candidates = (await _agreements.GetForDepartmentAsync(departmentId))?.Where(a => a.CoversDate(dispatchOn)).ToList() ?? new List<CalOesMarsAgreementSnapshot>();
			return candidates.Where(a => !string.IsNullOrWhiteSpace(classificationCode) && string.Equals(a.ClassificationCode, classificationCode, StringComparison.OrdinalIgnoreCase)).OrderByDescending(a => a.StartOn ?? DateTime.MinValue).FirstOrDefault()
				?? candidates.Where(a => string.IsNullOrWhiteSpace(a.ClassificationCode)).OrderByDescending(a => a.StartOn ?? DateTime.MinValue).FirstOrDefault();
		}

		#endregion

		#region Helpers

		/// <summary>Runs the action in the scope's transaction, joining one the caller already opened (tests pass no unit of work and run unwrapped).</summary>
		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null) return await action();
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

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		internal static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			switch (clone)
			{
				case CalOesMarsRateProfile profile: profile.Lines = null; profile.AdministrativeInputs = null; break;
				case CalOesMarsWorkItem item: item.Lines = null; item.SnapshotJson = item.SnapshotJson == null ? null : "sha256:" + Sha256(item.SnapshotJson); break;
			}
			return clone.CloneJsonToString();
		}

		internal static string Sha256(string value)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
		}

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
