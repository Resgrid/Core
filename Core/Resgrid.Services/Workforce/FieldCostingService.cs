using System;
using System.Collections.Generic;
using System.Linq;
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
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Invoicing;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// Resource cost profiles / components / usage entries and the internal field-cost runs for bids, calls and
	/// deployments (plan E3 <c>IFieldCostingService</c>, E4 run semantics). Resource costing is plain numeric data
	/// gated by ViewInternalCosts (74); personnel lines carry no rate in the clear — the priced detail (rate,
	/// multiplier, components, employment) is an ADP catalog 28 envelope on <c>FieldCostLines.ProtectedDetailJson</c>.
	/// A frozen run is immutable; a recalculation supersedes it. Revenue is only ever an observed number: a bid's
	/// estimated total, customer invoices, or the selected Cal OES MARS recovery snapshot.
	/// </summary>
	public class FieldCostingService : IFieldCostingService
	{
		private readonly IResourceCostProfileRepository _profiles;
		private readonly IResourceCostComponentRepository _components;
		private readonly IResourceUsageEntryRepository _usage;
		private readonly IFieldCostRunRepository _runs;
		private readonly IFieldCostLineRepository _lines;
		private readonly IWorkforceWorkerRepository _workers;
		private readonly IWorkforceEmploymentRepository _employments;
		private readonly IWorkforceWorkEntryRepository _workEntries;
		private readonly ICompensationCostService _compensation;
		private readonly IBidRepository _bids;
		private readonly IBidLineItemRepository _bidLines;
		private readonly IDeploymentRepository _deployments;
		private readonly IDeploymentPersonnelRepository _deploymentPersonnel;
		private readonly IDeploymentUnitRepository _deploymentUnits;
		private readonly IDeploymentTimeReportRepository _reports;
		private readonly IDeploymentTimeEntryRepository _entries;
		private readonly IDeploymentExpenseRepository _expenses;
		private readonly IInvoiceRepository _invoices;
		private readonly Lazy<ICalOesMarsService> _calOesMars;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly WorkforceProtectionSeam _seam;

		public FieldCostingService(IResourceCostProfileRepository profiles, IResourceCostComponentRepository components, IResourceUsageEntryRepository usage, IFieldCostRunRepository runs, IFieldCostLineRepository lines,
			IWorkforceWorkerRepository workers, IWorkforceEmploymentRepository employments, IWorkforceWorkEntryRepository workEntries, ICompensationCostService compensation,
			IBidRepository bids, IBidLineItemRepository bidLines, IDeploymentRepository deployments, IDeploymentPersonnelRepository deploymentPersonnel, IDeploymentUnitRepository deploymentUnits,
			IDeploymentTimeReportRepository reports, IDeploymentTimeEntryRepository entries, IDeploymentExpenseRepository expenses, IInvoiceRepository invoices, Lazy<ICalOesMarsService> calOesMars,
			IUnitsService unitsService, IEventAggregator eventAggregator,
			Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_profiles = profiles;
			_components = components;
			_usage = usage;
			_runs = runs;
			_lines = lines;
			_workers = workers;
			_employments = employments;
			_workEntries = workEntries;
			_compensation = compensation;
			_bids = bids;
			_bidLines = bidLines;
			_deployments = deployments;
			_deploymentPersonnel = deploymentPersonnel;
			_deploymentUnits = deploymentUnits;
			_reports = reports;
			_entries = entries;
			_expenses = expenses;
			_invoices = invoices;
			_calOesMars = calOesMars;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
			_seam = new WorkforceProtectionSeam(protectedWrite, protectedRead, grant);
		}

		#region Resource profiles

		public async Task<List<ResourceCostProfile>> GetResourceProfilesAsync(int departmentId)
		{
			var rows = (await _profiles.GetForDepartmentAsync(departmentId))?.Where(p => !p.IsDeleted).OrderBy(p => p.Name).ToList() ?? new List<ResourceCostProfile>();
			await LoadComponentsAsync(rows);
			await NameSubjectsAsync(rows, departmentId);
			return rows;
		}

		public async Task<ResourceCostProfile> GetResourceProfileAsync(string profileId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(profileId)) return null;
			var row = await _profiles.GetByIdForDepartmentAsync(profileId, departmentId);
			if (row == null || row.IsDeleted) return null;
			await LoadComponentsAsync(new[] { row });
			await NameSubjectsAsync(new[] { row }, departmentId);
			return row;
		}

		private async Task LoadComponentsAsync(IReadOnlyList<ResourceCostProfile> rows)
		{
			if (rows.Count == 0) return;
			var components = (await _components.GetByProfilesAsync(rows.Select(r => r.ResourceCostProfileId)))?.Where(c => !c.IsDeleted).ToList() ?? new List<ResourceCostComponent>();
			foreach (var row in rows) row.Components = components.Where(c => c.ResourceCostProfileId == row.ResourceCostProfileId).OrderBy(c => c.Category).ToList();
		}

		private async Task NameSubjectsAsync(IReadOnlyList<ResourceCostProfile> rows, int departmentId)
		{
			if (rows.All(r => !r.UnitId.HasValue)) { foreach (var r in rows) r.SubjectName = r.Name ?? r.ExternalResourceKey ?? r.InventoryAssetId; return; }
			var units = (await _unitsService.GetUnitsForDepartmentAsync(departmentId))?.ToList() ?? new List<Unit>();
			foreach (var row in rows) row.SubjectName = row.UnitId.HasValue ? units.FirstOrDefault(u => u.UnitId == row.UnitId)?.Name ?? row.Name : row.Name ?? row.ExternalResourceKey ?? row.InventoryAssetId;
		}

		public async Task<ResourceCostProfile> SaveResourceProfileAsync(ResourceCostProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (!Enum.IsDefined(typeof(ResourceSubjectTypes), profile.SubjectType)) throw new InvalidOperationException("workforce_subject_invalid");
			if (profile.SubjectType == (int)ResourceSubjectTypes.Unit && !profile.UnitId.HasValue) throw new InvalidOperationException("workforce_unit_required");
			if (profile.SubjectType == (int)ResourceSubjectTypes.InventoryAsset && string.IsNullOrWhiteSpace(profile.InventoryAssetId)) throw new InvalidOperationException("workforce_asset_required");
			if (profile.SubjectType == (int)ResourceSubjectTypes.External && string.IsNullOrWhiteSpace(profile.ExternalResourceKey)) throw new InvalidOperationException("workforce_external_key_required");
			if (!Enum.IsDefined(typeof(AllocationBases), profile.AllocationBasis)) throw new InvalidOperationException("workforce_allocation_invalid");
			if (profile.ExpiresOn.HasValue && profile.ExpiresOn < profile.EffectiveOn) throw new InvalidOperationException("workforce_dates_invalid");
			if (profile.AcquisitionCost < 0 || profile.SalvageValue < 0 || profile.UsefulLifeQuantity < 0 || profile.ExpectedAnnualUtilization < 0) throw new InvalidOperationException("workforce_amount_invalid");
			if (profile.SubjectType == (int)ResourceSubjectTypes.Unit)
			{
				var unit = await _unitsService.GetUnitByIdAsync(profile.UnitId.Value);
				if (unit == null || unit.DepartmentId != profile.DepartmentId) throw new InvalidOperationException("workforce_unit_not_found");
			}
			var existing = string.IsNullOrWhiteSpace(profile.ResourceCostProfileId) ? null : await _profiles.GetByIdForDepartmentAsync(profile.ResourceCostProfileId, profile.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var siblings = (await _profiles.GetForDepartmentAsync(profile.DepartmentId))?.Where(p => !p.IsDeleted && p.ResourceCostProfileId != profile.ResourceCostProfileId && SameSubject(p, profile) && Overlaps(p, profile)).ToList() ?? new List<ResourceCostProfile>();
			if (siblings.Count > 0) throw new InvalidOperationException("workforce_profile_overlap");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new ResourceCostProfile { DepartmentId = profile.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.SubjectType = profile.SubjectType; target.UnitId = profile.SubjectType == (int)ResourceSubjectTypes.Unit ? profile.UnitId : null;
			target.InventoryAssetId = profile.SubjectType == (int)ResourceSubjectTypes.InventoryAsset ? profile.InventoryAssetId?.Trim() : null;
			target.ExternalResourceKey = profile.SubjectType == (int)ResourceSubjectTypes.External ? profile.ExternalResourceKey?.Trim() : null;
			target.Name = Trim(profile.Name); target.EffectiveOn = profile.EffectiveOn.Date; target.ExpiresOn = profile.ExpiresOn?.Date; target.Currency = string.IsNullOrWhiteSpace(profile.Currency) ? "USD" : profile.Currency.Trim().ToUpperInvariant();
			target.AcquisitionCost = profile.AcquisitionCost; target.AcquisitionDate = profile.AcquisitionDate; target.InServiceDate = profile.InServiceDate; target.SalvageValue = profile.SalvageValue;
			target.DepreciationMethod = (int)DepreciationMethods.StraightLine; target.AllocationBasis = profile.AllocationBasis; target.UsefulLifeQuantity = profile.UsefulLifeQuantity; target.UsefulLifeMonths = profile.UsefulLifeMonths;
			target.ExpectedAnnualUtilization = profile.ExpectedAnnualUtilization; target.Source = Trim(profile.Source) ?? "manual";
			if (existing != null && (existing.AcquisitionCost != target.AcquisitionCost || existing.SalvageValue != target.SalvageValue || existing.UsefulLifeQuantity != target.UsefulLifeQuantity || existing.UsefulLifeMonths != target.UsefulLifeMonths || existing.ExpectedAnnualUtilization != target.ExpectedAnnualUtilization || existing.AllocationBasis != target.AllocationBasis))
			{ target.IsApproved = false; target.ApprovedByUserId = null; target.ApprovedOn = null; }
			if (existing == null) target.IsApproved = profile.IsApproved;
			if (target.IsApproved && target.ApprovedOn == null) { target.ApprovedByUserId = userId; target.ApprovedOn = now; }
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _profiles.SaveOrUpdateAsync(target, cancellationToken);
			Audit(profile.DepartmentId, userId, AuditLogTypes.ResourceCostProfileChanged, ipAddress, userAgent, before, Snapshot(saved));
			return await GetResourceProfileAsync(saved.ResourceCostProfileId, profile.DepartmentId);
		}

		private static bool SameSubject(ResourceCostProfile a, ResourceCostProfile b) => a.SubjectType == b.SubjectType && a.UnitId == b.UnitId && string.Equals(a.InventoryAssetId, b.InventoryAssetId, StringComparison.OrdinalIgnoreCase) && string.Equals(a.ExternalResourceKey, b.ExternalResourceKey, StringComparison.OrdinalIgnoreCase);
		private static bool Overlaps(ResourceCostProfile a, ResourceCostProfile b) => a.EffectiveOn.Date <= (b.ExpiresOn ?? DateTime.MaxValue).Date && (a.ExpiresOn ?? DateTime.MaxValue).Date >= b.EffectiveOn.Date;

		public async Task<ResourceCostProfile> SaveResourceComponentsAsync(string profileId, int departmentId, List<ResourceCostComponent> components, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(profileId ?? string.Empty, departmentId);
			if (profile == null || profile.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = Snapshot(profile);
			var now = DateTime.UtcNow;
			var existingRows = (await _components.GetByProfileAsync(profile.ResourceCostProfileId))?.Where(c => !c.IsDeleted).ToList() ?? new List<ResourceCostComponent>();
			var inputs = components ?? new List<ResourceCostComponent>();
			foreach (var input in inputs)
			{
				if (!Enum.IsDefined(typeof(ResourceCostCategories), input.Category) || !Enum.IsDefined(typeof(ResourceCostBases), input.Basis) || !Enum.IsDefined(typeof(ResourceCostSources), input.Source)) throw new InvalidOperationException("workforce_component_invalid");
				if (input.Rate < 0 || input.UnitPrice < 0 || input.ConsumptionQuantity < 0) throw new InvalidOperationException("workforce_amount_invalid");
				var existing = string.IsNullOrWhiteSpace(input.ResourceCostComponentId) ? null : existingRows.FirstOrDefault(c => c.ResourceCostComponentId == input.ResourceCostComponentId);
				var target = existing ?? new ResourceCostComponent { DepartmentId = departmentId, ResourceCostProfileId = profile.ResourceCostProfileId, AddedOn = now, AddedByUserId = userId };
				target.EffectiveOn = input.EffectiveOn?.Date; target.ExpiresOn = input.ExpiresOn?.Date; target.Category = input.Category; target.Basis = input.Basis; target.Rate = input.Rate;
				target.ConsumptionQuantity = input.ConsumptionQuantity; target.ConsumptionUnit = Trim(input.ConsumptionUnit); target.UnitPrice = input.UnitPrice; target.Source = input.Source;
				target.SourceWindowStart = input.SourceWindowStart; target.SourceWindowEnd = input.SourceWindowEnd; target.SourceMeterStart = input.SourceMeterStart; target.SourceMeterEnd = input.SourceMeterEnd;
				target.IsApproved = input.IsApproved;
				if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
				await _components.SaveOrUpdateAsync(target, cancellationToken);
			}
			foreach (var stale in existingRows.Where(c => inputs.All(i => i.ResourceCostComponentId != c.ResourceCostComponentId)))
			{
				stale.IsDeleted = true; stale.EditedOn = now; stale.EditedByUserId = userId;
				await _components.SaveOrUpdateAsync(stale, cancellationToken);
			}
			profile.RowVersion++; profile.EditedOn = now; profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ResourceCostProfileChanged, ipAddress, userAgent, before, Snapshot(profile));
			return await GetResourceProfileAsync(profile.ResourceCostProfileId, departmentId);
		}

		public async Task<bool> DeleteResourceProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(profileId ?? string.Empty, departmentId);
			if (profile == null || profile.IsDeleted) return false;
			var before = Snapshot(profile);
			profile.IsDeleted = true; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ResourceCostProfileChanged, ipAddress, userAgent, before, Snapshot(profile));
			return true;
		}

		#endregion

		#region Usage entries

		public async Task<List<ResourceUsageEntry>> GetUsageForDeploymentAsync(string deploymentId, int departmentId) =>
			string.IsNullOrWhiteSpace(deploymentId) ? new List<ResourceUsageEntry>() : (await _usage.GetByDeploymentAsync(deploymentId))?.Where(u => u.DepartmentId == departmentId && !u.IsDeleted).OrderBy(u => u.UsageDate).ToList() ?? new List<ResourceUsageEntry>();

		public async Task<List<ResourceUsageEntry>> GetUsageForCallAsync(int callId, int departmentId) =>
			(await _usage.GetByCallAsync(callId))?.Where(u => u.DepartmentId == departmentId && !u.IsDeleted).OrderBy(u => u.UsageDate).ToList() ?? new List<ResourceUsageEntry>();

		public async Task<ResourceUsageEntry> SaveUsageEntryAsync(ResourceUsageEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (entry == null) throw new ArgumentNullException(nameof(entry));
			if (!Enum.IsDefined(typeof(ResourceSubjectTypes), entry.SubjectType) || !Enum.IsDefined(typeof(UsagePhases), entry.Phase) || !Enum.IsDefined(typeof(UsageSources), entry.Source)) throw new InvalidOperationException("workforce_usage_invalid");
			if (string.IsNullOrWhiteSpace(entry.DeploymentId) && !entry.CallId.HasValue) throw new InvalidOperationException("workforce_usage_context_required");
			if (entry.SubjectType == (int)ResourceSubjectTypes.Unit && !entry.UnitId.HasValue) throw new InvalidOperationException("workforce_unit_required");
			if (!string.IsNullOrWhiteSpace(entry.DeploymentId))
			{
				var deployment = await _deployments.GetByIdForDepartmentAsync(entry.DeploymentId, entry.DepartmentId);
				if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("workforce_deployment_not_found");
			}
			Canonicalize(entry);
			if (entry.CanonicalDistanceMiles < 0 || entry.EngineHours < 0 || entry.OperatingHours < 0 || entry.IdleHours < 0 || entry.DeployedDays < 0 || entry.StandbyDays < 0 || entry.FuelQuantity < 0 || entry.FuelActualCost < 0) throw new InvalidOperationException("workforce_amount_invalid");
			var existing = string.IsNullOrWhiteSpace(entry.ResourceUsageEntryId) ? null : await _usage.GetByIdForDepartmentAsync(entry.ResourceUsageEntryId, entry.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = existing == null ? null : existing.CloneJsonToString();
			var now = DateTime.UtcNow;
			var target = existing ?? new ResourceUsageEntry { DepartmentId = entry.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.SubjectType = entry.SubjectType; target.UnitId = entry.UnitId; target.InventoryAssetId = Trim(entry.InventoryAssetId); target.ExternalResourceKey = Trim(entry.ExternalResourceKey);
			target.CallId = entry.CallId; target.DeploymentId = Trim(entry.DeploymentId); target.DeploymentTimeReportId = Trim(entry.DeploymentTimeReportId); target.UsageDate = entry.UsageDate.Date; target.Phase = entry.Phase;
			target.StartOdometer = entry.StartOdometer; target.EndOdometer = entry.EndOdometer; target.DistanceUnit = entry.DistanceUnit; target.OriginalDistance = entry.OriginalDistance; target.CanonicalDistanceMiles = entry.CanonicalDistanceMiles;
			target.StartEngineMeter = entry.StartEngineMeter; target.EndEngineMeter = entry.EndEngineMeter; target.EngineHours = entry.EngineHours; target.OperatingHours = entry.OperatingHours; target.IdleHours = entry.IdleHours;
			target.DeployedDays = entry.DeployedDays; target.StandbyDays = entry.StandbyDays; target.FuelQuantity = entry.FuelQuantity; target.FuelUnit = Trim(entry.FuelUnit); target.FuelActualCost = entry.FuelActualCost;
			target.Source = entry.Source; target.ExternalId = Trim(entry.ExternalId); target.IsApproved = entry.IsApproved;
			target.NeedsReview = false; target.ReviewReason = null;
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			// Conflicting automatic vs manual readings for the same unit / date / context are queued for review (both sides).
			var siblings = (!string.IsNullOrWhiteSpace(target.DeploymentId) ? await _usage.GetByDeploymentAsync(target.DeploymentId) : await _usage.GetByCallAsync(target.CallId.Value))?
				.Where(u => !u.IsDeleted && u.ResourceUsageEntryId != target.ResourceUsageEntryId && u.UnitId == target.UnitId && u.SubjectType == target.SubjectType && u.UsageDate.Date == target.UsageDate.Date && IsAutomatic(u.Source) != IsAutomatic(target.Source) && u.CanonicalDistanceMiles.HasValue && target.CanonicalDistanceMiles.HasValue).ToList() ?? new List<ResourceUsageEntry>();
			foreach (var sibling in siblings)
			{
				var reference = Math.Max(sibling.CanonicalDistanceMiles.Value, target.CanonicalDistanceMiles.Value);
				if (reference <= 0) continue;
				var variance = Math.Abs(sibling.CanonicalDistanceMiles.Value - target.CanonicalDistanceMiles.Value) / reference * 100m;
				if (variance <= Config.WorkforceConfig.UsageConflictTolerancePercent) continue;
				target.NeedsReview = true; target.ReviewReason = "distance_conflict";
				if (!sibling.NeedsReview) { sibling.NeedsReview = true; sibling.ReviewReason = "distance_conflict"; await _usage.SaveOrUpdateAsync(sibling, cancellationToken); }
			}
			var saved = await _usage.SaveOrUpdateAsync(target, cancellationToken);
			Audit(entry.DepartmentId, userId, AuditLogTypes.ResourceUsageChanged, ipAddress, userAgent, before, saved.CloneJsonToString());
			return saved;
		}

		private static bool IsAutomatic(int source) => source == (int)UsageSources.Gps || source == (int)UsageSources.HardwareTracker;

		/// <summary>Distance and engine hours derive from meters when readings exist; distance is canonical in miles.</summary>
		public static void Canonicalize(ResourceUsageEntry entry)
		{
			var unit = string.IsNullOrWhiteSpace(entry.DistanceUnit) ? "mi" : entry.DistanceUnit.Trim().ToLowerInvariant();
			entry.DistanceUnit = unit == "km" ? "km" : "mi";
			if (entry.StartOdometer.HasValue && entry.EndOdometer.HasValue && entry.EndOdometer >= entry.StartOdometer) entry.OriginalDistance = entry.EndOdometer - entry.StartOdometer;
			entry.CanonicalDistanceMiles = entry.OriginalDistance.HasValue ? FieldCostCalculator.ToMiles(entry.OriginalDistance.Value, entry.DistanceUnit) : null;
			if (entry.StartEngineMeter.HasValue && entry.EndEngineMeter.HasValue && entry.EndEngineMeter >= entry.StartEngineMeter) entry.EngineHours = FieldCostCalculator.Round(entry.EndEngineMeter.Value - entry.StartEngineMeter.Value);
		}

		public async Task<bool> DeleteUsageEntryAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _usage.GetByIdForDepartmentAsync(id ?? string.Empty, departmentId);
			if (row == null || row.IsDeleted) return false;
			var before = row.CloneJsonToString();
			row.IsDeleted = true; row.EditedOn = DateTime.UtcNow; row.EditedByUserId = userId;
			await _usage.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ResourceUsageChanged, ipAddress, userAgent, before, row.CloneJsonToString());
			return true;
		}

		#endregion

		#region Runs

		public async Task<List<FieldCostRun>> GetRunsAsync(int departmentId, int skip = 0, int take = 100) => (await _runs.GetForDepartmentAsync(departmentId, Math.Max(0, skip), Math.Clamp(take, 1, 500)))?.Where(r => !r.IsDeleted).ToList() ?? new List<FieldCostRun>();
		public async Task<List<FieldCostRun>> GetRunsForDeploymentAsync(string deploymentId, int departmentId) => string.IsNullOrWhiteSpace(deploymentId) ? new List<FieldCostRun>() : (await _runs.GetByDeploymentAsync(deploymentId, departmentId))?.Where(r => !r.IsDeleted).OrderByDescending(r => r.AddedOn).ToList() ?? new List<FieldCostRun>();
		public async Task<List<FieldCostRun>> GetRunsForBidAsync(string bidId, int departmentId) => string.IsNullOrWhiteSpace(bidId) ? new List<FieldCostRun>() : (await _runs.GetByBidAsync(bidId, departmentId))?.Where(r => !r.IsDeleted).OrderByDescending(r => r.AddedOn).ToList() ?? new List<FieldCostRun>();
		public async Task<List<FieldCostRun>> GetRunsForCallAsync(int callId, int departmentId) => (await _runs.GetByCallAsync(callId, departmentId))?.Where(r => !r.IsDeleted).OrderByDescending(r => r.AddedOn).ToList() ?? new List<FieldCostRun>();

		public async Task<FieldCostRun> GetRunAsync(string runId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(runId)) return null;
			var run = await _runs.GetByIdForDepartmentAsync(runId, departmentId);
			if (run == null || run.IsDeleted) return null;
			run.Lines = (await _lines.GetByRunAsync(run.FieldCostRunId))?.OrderBy(l => l.SortOrder).ToList() ?? new List<FieldCostLine>();
			await _seam.ResolveForReadAsync(run.Lines, departmentId, WorkforceProtectedFields.CostLine);
			return run;
		}

		public async Task<FieldCostRun> EstimateBidCostAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var bid = await _bids.GetByIdForDepartmentAsync(bidId ?? string.Empty, departmentId);
			if (bid == null || bid.IsDeleted) throw new InvalidOperationException("workforce_bid_not_found");
			var lines = (await _bidLines.GetByBidAsync(bid.BidId))?.OrderBy(l => l.SortOrder).ToList() ?? new List<BidLineItem>();
			var asOf = bid.RequestedStartOn ?? DateTime.UtcNow.Date;
			var builder = new RunBuilder("USD", asOf);
			var profiles = await ActiveResourceProfilesAsync(departmentId, asOf);
			foreach (var line in lines)
			{
				var hoursPerDay = line.EstimatedHoursPerDay ?? 8m;
				var days = line.EstimatedDays ?? 1m;
				var quantity = line.Quantity <= 0 ? 1m : line.Quantity;
				switch ((BidLineTypes)line.LineType)
				{
					case BidLineTypes.PersonnelCertification:
					case BidLineTypes.Crew:
					{
						var heads = quantity * (line.CrewSize ?? 1);
						var hours = FieldCostCalculator.Round(hoursPerDay * days * heads);
						if (hours <= 0) break;
						var regular = Math.Min(hours, Config.WorkforceConfig.DailyOvertimeThresholdHours * days * heads);
						var overtime = hours - regular;
						var work = new LaborWorkQuantity { SubjectLabel = line.Description, WorkDate = asOf, PayCode = (int)PayCodes.Regular, Hours = regular, SourceType = "BidLineItem", SourceId = line.BidLineItemId };
						builder.AddLabor(FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = work, Profile = (await _compensation.ResolveProfileAsync(null, null, departmentId, asOf)).Profile, IsFallback = true, AsOf = asOf, Currency = builder.Currency }), work, "BidLineItem", line.BidLineItemId, line.Description, true);
						if (overtime > 0)
						{
							var otWork = new LaborWorkQuantity { SubjectLabel = line.Description, WorkDate = asOf, PayCode = (int)PayCodes.Overtime, Hours = overtime, SourceType = "BidLineItem", SourceId = line.BidLineItemId };
							builder.AddLabor(FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = otWork, Profile = (await _compensation.ResolveProfileAsync(null, null, departmentId, asOf)).Profile, IsFallback = true, AsOf = asOf, Currency = builder.Currency }), otWork, "BidLineItem", line.BidLineItemId, line.Description, true);
						}
						break;
					}
					case BidLineTypes.Vehicle:
					case BidLineTypes.Equipment:
					{
						var usage = new ResourceUsageQuantity { SubjectLabel = line.Description, UsageDate = asOf, Days = days * quantity, OperatingHours = hoursPerDay * days * quantity, Deployments = quantity, SourceType = "BidLineItem", SourceId = line.BidLineItemId };
						var profile = profiles.FirstOrDefault(p => p.SubjectType == (int)ResourceSubjectTypes.External && !string.IsNullOrWhiteSpace(line.RateScheduleEntryId) && string.Equals(p.ExternalResourceKey, line.RateScheduleEntryId, StringComparison.OrdinalIgnoreCase));
						builder.AddResource(FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = usage, Profile = profile, IsFallback = profile != null, AsOf = asOf }), usage, "BidLineItem", line.BidLineItemId, line.Description, true);
						break;
					}
					default:
						break;
				}
			}
			var run = builder.Build(new FieldCostRun { DepartmentId = departmentId, ContextType = (int)FieldCostContextTypes.Bid, BidId = bid.BidId, RunType = (int)FieldCostRunTypes.Estimate, ThroughDate = bid.RequestedEndOn, AddedOn = DateTime.UtcNow, AddedByUserId = userId });
			run.RevenueSource = (int)RevenueSources.BidEstimate; run.RevenueAmount = bid.EstimatedTotal; run.RevenueSourceId = bid.BidId; run.RevenueSourceVersion = bid.EditedOn?.ToString("O") ?? bid.AddedOn.ToString("O");
			ApplyMargin(run);
			return await PersistRunAsync(run, builder.Lines, departmentId, userId, ipAddress, userAgent, cancellationToken);
		}

		public async Task<FieldCostRun> CalculateCallCostAsync(int callId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var workEntries = (await _workEntries.GetByCallAsync(callId))?.Where(w => w.DepartmentId == departmentId && !w.IsDeleted).ToList() ?? new List<WorkforceWorkEntry>();
			var usage = await GetUsageForCallAsync(callId, departmentId);
			if (workEntries.Count == 0 && usage.Count == 0) throw new InvalidOperationException("workforce_no_inputs");
			var asOf = workEntries.Select(w => w.WorkDate).Concat(usage.Select(u => u.UsageDate)).DefaultIfEmpty(DateTime.UtcNow.Date).Max();
			var builder = new RunBuilder("USD", asOf);
			await _seam.ResolveForWorkloadAsync(workEntries, departmentId, WorkforceProtectedFields.CostingWorkloadPurpose, WorkforceProtectedFields.WorkEntry);
			foreach (var entry in workEntries.OrderBy(w => w.WorkDate))
			{
				var work = new LaborWorkQuantity { WorkforceEmploymentId = entry.WorkforceEmploymentId, SubjectLabel = ((WorkHoursTypes)entry.HoursType).ToString(), WorkDate = entry.WorkDate, PayCode = PayCodeFor((WorkHoursTypes)entry.HoursType), Hours = entry.Hours, SourceType = "WorkforceWorkEntry", SourceId = entry.WorkforceWorkEntryId, ApprovedPayrollCost = WorkforceProtectionSeam.IsUnavailable(entry.ApprovedPayrollCost) ? null : entry.ApprovedPayrollCostValue };
				var (profile, fallback) = await _compensation.ResolveProfileAsync(entry.WorkforceEmploymentId, null, departmentId, entry.WorkDate);
				builder.AddLabor(FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = work, Profile = profile, IsFallback = fallback, AsOf = entry.WorkDate, Currency = builder.Currency }), work, "WorkforceWorkEntry", entry.WorkforceWorkEntryId, work.SubjectLabel, false);
			}
			await AddUsageLinesAsync(builder, usage, departmentId, asOf, false);
			var run = builder.Build(new FieldCostRun { DepartmentId = departmentId, ContextType = (int)FieldCostContextTypes.Call, CallId = callId, RunType = (int)FieldCostRunTypes.Actual, ThroughDate = asOf, AddedOn = DateTime.UtcNow, AddedByUserId = userId });
			run.RevenueSource = (int)RevenueSources.None; run.RevenueAmount = null;
			ApplyMargin(run);
			return await PersistRunAsync(run, builder.Lines, departmentId, userId, ipAddress, userAgent, cancellationToken);
		}

		public async Task<FieldCostRun> CalculateDeploymentCostAsync(string deploymentId, int departmentId, DateTime? throughDate, RevenueSources revenueSource, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId ?? string.Empty, departmentId);
			if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("workforce_deployment_not_found");
			var through = (throughDate ?? DateTime.UtcNow).Date;
			var personnel = (await _deploymentPersonnel.GetByDeploymentAsync(deployment.DeploymentId))?.ToList() ?? new List<DeploymentPersonnel>();
			var units = (await _deploymentUnits.GetByDeploymentAsync(deployment.DeploymentId))?.ToList() ?? new List<DeploymentUnit>();
			var reports = (await _reports.GetByDeploymentAsync(deployment.DeploymentId))?.Where(r => !r.IsDeleted && r.DepartmentId == departmentId && (r.Status == (int)DeploymentTimeReportStatuses.Approved || r.Status == (int)DeploymentTimeReportStatuses.Billed) && r.ReportDate.Date <= through).ToList() ?? new List<DeploymentTimeReport>();
			var reportIds = new HashSet<string>(reports.Select(r => r.DeploymentTimeReportId));
			var entries = (await _entries.GetByDeploymentAsync(deployment.DeploymentId))?.Where(e => e.DeploymentTimeReportId != null && reportIds.Contains(e.DeploymentTimeReportId)).ToList() ?? new List<DeploymentTimeEntry>();
			var builder = new RunBuilder(string.IsNullOrWhiteSpace(deployment.Currency) ? "USD" : deployment.Currency, through);

			// Personnel: approved DTR hours by member and day; hours above the daily threshold price at Overtime. A payroll-approved
			// cost on a matching work entry replaces the estimate.
			var workEntries = (await _workEntries.GetByDeploymentAsync(deployment.DeploymentId))?.Where(w => w.DepartmentId == departmentId && !w.IsDeleted).ToList() ?? new List<WorkforceWorkEntry>();
			await _seam.ResolveForWorkloadAsync(workEntries, departmentId, WorkforceProtectedFields.CostingWorkloadPurpose, WorkforceProtectedFields.WorkEntry);
			var workerCache = new Dictionary<string, WorkforceWorker>(StringComparer.OrdinalIgnoreCase);
			var employmentCache = new Dictionary<string, List<WorkforceEmployment>>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Personnel && !string.IsNullOrWhiteSpace(e.DeploymentPersonnelId)).GroupBy(e => (e.DeploymentPersonnelId, Date: e.StartTime.Date)).OrderBy(g => g.Key.Date))
			{
				var member = personnel.FirstOrDefault(p => p.DeploymentPersonnelId == group.Key.DeploymentPersonnelId);
				if (member == null) continue;
				if (!workerCache.TryGetValue(member.UserId, out var worker)) { worker = await _workers.GetByUserIdAsync(departmentId, member.UserId); workerCache[member.UserId] = worker; }
				List<WorkforceEmployment> employments = null;
				if (worker != null && !employmentCache.TryGetValue(worker.WorkforceWorkerId, out employments)) { employments = (await _employments.GetByWorkerAsync(worker.WorkforceWorkerId))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEmployment>(); employmentCache[worker.WorkforceWorkerId] = employments; }
				var employment = employments?.FirstOrDefault(e => e.Covers(group.Key.Date, group.Key.Date));
				var label = member.CallSign ?? "Personnel";
				var approved = worker == null ? null : workEntries.FirstOrDefault(w => w.WorkforceWorkerId == worker.WorkforceWorkerId && w.WorkDate.Date == group.Key.Date && !WorkforceProtectionSeam.IsUnavailable(w.ApprovedPayrollCost) && w.ApprovedPayrollCostValue.HasValue);
				var byCode = new List<(int PayCode, decimal Hours)>();
				var deploymentHours = group.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Deployment).Sum(e => e.Hours);
				if (deploymentHours > 0)
				{
					var regular = Math.Min(deploymentHours, Config.WorkforceConfig.DailyOvertimeThresholdHours);
					byCode.Add(((int)PayCodes.Regular, FieldCostCalculator.Round(regular)));
					if (deploymentHours > regular) byCode.Add(((int)PayCodes.Overtime, FieldCostCalculator.Round(deploymentHours - regular)));
				}
				var standby = group.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Standby).Sum(e => e.Hours);
				if (standby > 0) byCode.Add(((int)PayCodes.Standby, FieldCostCalculator.Round(standby)));
				var travel = group.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Travel).Sum(e => e.Hours);
				if (travel > 0) byCode.Add(((int)PayCodes.Travel, FieldCostCalculator.Round(travel)));
				var first = true;
				foreach (var (payCode, hours) in byCode)
				{
					var work = new LaborWorkQuantity { WorkforceEmploymentId = employment?.WorkforceEmploymentId, SubjectLabel = label, WorkDate = group.Key.Date, PayCode = payCode, Hours = hours, SourceType = "DeploymentTimeEntry", SourceId = group.First().DeploymentTimeReportId, ApprovedPayrollCost = first ? approved?.ApprovedPayrollCostValue : null };
					first = false;
					var (profile, fallback) = employment == null ? await _compensation.ResolveProfileAsync(null, null, departmentId, group.Key.Date) : await _compensation.ResolveProfileAsync(employment.WorkforceEmploymentId, employment.PersonnelRoleId, departmentId, group.Key.Date);
					var result = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = work, Profile = profile, IsFallback = fallback || employment == null, AsOf = group.Key.Date, Currency = builder.Currency });
					if (employment == null && !result.ReviewReasons.Contains(LaborReviewReasons.NoProfile)) { result.NeedsReview = true; result.ReviewReasons.Add("no_employment_for_member"); }
					builder.AddLabor(result, work, "DeploymentPersonnel", member.DeploymentPersonnelId, label, false);
				}
			}

			// Units: DTR unit hours (operating / idle / days) merged with usage entries (distance, engine hours, fuel).
			var usage = await GetUsageForDeploymentAsync(deployment.DeploymentId, departmentId);
			var profiles = await ActiveResourceProfilesAsync(departmentId, through);
			var unitNames = (await _unitsService.GetUnitsForDepartmentAsync(departmentId))?.ToDictionary(u => u.UnitId, u => u.Name) ?? new Dictionary<int, string>();
			foreach (var unit in units)
			{
				var unitEntries = entries.Where(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Unit && e.DeploymentUnitId == unit.DeploymentUnitId).ToList();
				var unitUsage = usage.Where(u => u.UnitId == unit.UnitId && u.UsageDate.Date <= through).ToList();
				if (unitEntries.Count == 0 && unitUsage.Count == 0) continue;
				var quantity = new ResourceUsageQuantity
				{
					SubjectLabel = unitNames.TryGetValue(unit.UnitId, out var unitName) ? unitName : unit.CallSign ?? $"Unit {unit.UnitId}",
					UsageDate = through,
					OperatingHours = FieldCostCalculator.Round(unitEntries.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Deployment || e.EntryType == (int)DeploymentTimeEntryTypes.Travel).Sum(e => e.Hours) + unitUsage.Sum(u => u.OperatingHours ?? 0m)),
					IdleHours = FieldCostCalculator.Round(unitEntries.Where(e => e.EntryType == (int)DeploymentTimeEntryTypes.Standby).Sum(e => e.Hours) + unitUsage.Sum(u => u.IdleHours ?? 0m)),
					EngineHours = FieldCostCalculator.Round(unitUsage.Sum(u => u.EngineHours ?? 0m)),
					Miles = FieldCostCalculator.Round(unitEntries.Sum(e => FieldCostCalculator.ToMiles(e.MileageKm ?? 0m, "km")) + unitUsage.Sum(u => u.CanonicalDistanceMiles ?? 0m)),
					Days = Math.Max(unitEntries.Select(e => e.StartTime.Date).Distinct().Count(), unitUsage.Sum(u => (u.DeployedDays ?? 0m) + (u.StandbyDays ?? 0m))),
					Deployments = 1,
					ActualFuelCost = unitUsage.Any(u => u.FuelActualCost.HasValue) ? unitUsage.Sum(u => u.FuelActualCost ?? 0m) : null,
					SourceType = "DeploymentUnit", SourceId = unit.DeploymentUnitId
				};
				var profile = profiles.FirstOrDefault(p => p.SubjectType == (int)ResourceSubjectTypes.Unit && p.UnitId == unit.UnitId);
				var result = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = quantity, Profile = profile, IsFallback = false, AsOf = through });
				if (unitUsage.Any(u => u.NeedsReview)) { result.NeedsReview = true; result.ReviewReasons.Add("usage_needs_review"); }
				builder.AddResource(result, quantity, "DeploymentUnit", unit.DeploymentUnitId, quantity.SubjectLabel, false);
			}
			// Usage for resources that are not on the unit roster (assets, external keys).
			await AddUsageLinesAsync(builder, usage.Where(u => u.UsageDate.Date <= through && (u.SubjectType != (int)ResourceSubjectTypes.Unit || units.All(x => x.UnitId != u.UnitId))).ToList(), departmentId, through, false);

			// Expenses through the date (Phase C expense rows; not per-person, not protected).
			foreach (var expense in (await _expenses.GetByDeploymentAsync(deployment.DeploymentId))?.Where(e => !e.IsDeleted && e.ExpenseDate.Date <= through).OrderBy(e => e.ExpenseDate) ?? Enumerable.Empty<DeploymentExpense>())
				builder.AddExpense(expense.ExpenseDate, ((DeploymentExpenseTypes)expense.ExpenseType).ToString(), expense.Description, expense.Amount, "DeploymentExpense", expense.DeploymentExpenseId);

			var run = builder.Build(new FieldCostRun { DepartmentId = departmentId, ContextType = (int)FieldCostContextTypes.Deployment, DeploymentId = deployment.DeploymentId, RunType = (int)FieldCostRunTypes.Actual, ThroughDate = through, AddedOn = DateTime.UtcNow, AddedByUserId = userId });
			await ApplyRevenueAsync(run, deployment, revenueSource, departmentId);
			ApplyMargin(run);
			return await PersistRunAsync(run, builder.Lines, departmentId, userId, ipAddress, userAgent, cancellationToken);
		}

		private async Task AddUsageLinesAsync(RunBuilder builder, List<ResourceUsageEntry> usage, int departmentId, DateTime asOf, bool estimated)
		{
			if (usage.Count == 0) return;
			var profiles = await ActiveResourceProfilesAsync(departmentId, asOf);
			var unitNames = usage.Any(u => u.UnitId.HasValue) ? (await _unitsService.GetUnitsForDepartmentAsync(departmentId))?.ToDictionary(u => u.UnitId, u => u.Name) ?? new Dictionary<int, string>() : new Dictionary<int, string>();
			foreach (var group in usage.GroupBy(u => (u.SubjectType, u.UnitId, u.InventoryAssetId, u.ExternalResourceKey)))
			{
				var rows = group.ToList();
				var label = group.Key.UnitId.HasValue && unitNames.TryGetValue(group.Key.UnitId.Value, out var name) ? name : group.Key.InventoryAssetId ?? group.Key.ExternalResourceKey ?? "Resource";
				var quantity = new ResourceUsageQuantity
				{
					SubjectLabel = label, UsageDate = rows.Max(r => r.UsageDate),
					Miles = FieldCostCalculator.Round(rows.Sum(r => r.CanonicalDistanceMiles ?? 0m)), EngineHours = FieldCostCalculator.Round(rows.Sum(r => r.EngineHours ?? 0m)),
					OperatingHours = FieldCostCalculator.Round(rows.Sum(r => r.OperatingHours ?? 0m)), IdleHours = FieldCostCalculator.Round(rows.Sum(r => r.IdleHours ?? 0m)),
					Days = rows.Sum(r => (r.DeployedDays ?? 0m) + (r.StandbyDays ?? 0m)), Deployments = 1,
					ActualFuelCost = rows.Any(r => r.FuelActualCost.HasValue) ? rows.Sum(r => r.FuelActualCost ?? 0m) : null,
					SourceType = "ResourceUsageEntry", SourceId = string.Join(",", rows.Select(r => r.ResourceUsageEntryId))
				};
				var profile = profiles.FirstOrDefault(p => p.SubjectType == group.Key.SubjectType && p.UnitId == group.Key.UnitId && string.Equals(p.InventoryAssetId, group.Key.InventoryAssetId, StringComparison.OrdinalIgnoreCase) && string.Equals(p.ExternalResourceKey, group.Key.ExternalResourceKey, StringComparison.OrdinalIgnoreCase));
				var result = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = quantity, Profile = profile, IsFallback = false, AsOf = asOf });
				if (rows.Any(r => r.NeedsReview)) { result.NeedsReview = true; result.ReviewReasons.Add("usage_needs_review"); }
				builder.AddResource(result, quantity, "ResourceUsageEntry", rows[0].ResourceUsageEntryId, label, estimated);
			}
		}

		private async Task<List<ResourceCostProfile>> ActiveResourceProfilesAsync(int departmentId, DateTime asOf)
		{
			var rows = (await _profiles.GetForDepartmentAsync(departmentId))?.Where(p => !p.IsDeleted && p.Covers(asOf)).ToList() ?? new List<ResourceCostProfile>();
			await LoadComponentsAsync(rows);
			return rows;
		}

		private static int PayCodeFor(WorkHoursTypes type) => type switch
		{
			WorkHoursTypes.Overtime => (int)PayCodes.Overtime,
			WorkHoursTypes.DoubleTime => (int)PayCodes.DoubleTime,
			WorkHoursTypes.Standby => (int)PayCodes.Standby,
			WorkHoursTypes.Travel => (int)PayCodes.Travel,
			WorkHoursTypes.PaidLeave => (int)PayCodes.PaidLeave,
			_ => (int)PayCodes.Regular
		};

		private async Task ApplyRevenueAsync(FieldCostRun run, Deployment deployment, RevenueSources source, int departmentId)
		{
			run.RevenueSource = (int)source;
			run.RevenueAmount = null; run.RevenueSourceId = null; run.RevenueSourceVersion = null;
			switch (source)
			{
				case RevenueSources.BidEstimate:
				{
					if (string.IsNullOrWhiteSpace(deployment.BidId)) return;
					var bid = await _bids.GetByIdForDepartmentAsync(deployment.BidId, departmentId);
					if (bid == null || bid.IsDeleted) return;
					run.RevenueAmount = bid.EstimatedTotal; run.RevenueSourceId = bid.BidId; run.RevenueSourceVersion = bid.EditedOn?.ToString("O") ?? bid.AddedOn.ToString("O");
					return;
				}
				case RevenueSources.CustomerInvoice:
				{
					var invoices = (await _invoices.GetForDepartmentAsync(departmentId, new InvoiceListFilter { Skip = 0, Take = 1000 }))?.Where(i => !i.IsDeleted && i.DeploymentId == deployment.DeploymentId && i.Status != (int)InvoiceStatus.Void && i.Status != (int)InvoiceStatus.Draft).ToList() ?? new List<Invoice>();
					if (invoices.Count == 0) return;
					run.RevenueAmount = FieldCostCalculator.Round(invoices.Sum(i => i.Total)); run.RevenueSourceId = string.Join(",", invoices.Select(i => i.InvoiceId)); run.RevenueSourceVersion = invoices.Max(i => i.EditedOn ?? i.AddedOn).ToString("O");
					return;
				}
				case RevenueSources.CalOesMarsExpected:
				case RevenueSources.CalOesMarsApproved:
				case RevenueSources.CalOesMarsPaid:
				{
					var items = (await _calOesMars.Value.GetWorkItemsForDeploymentAsync(deployment.DeploymentId, departmentId))?.Where(w => w.RecordType == (int)CalOesMarsRecordTypes.F42 || w.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice).ToList() ?? new List<CalOesMarsWorkItem>();
					var current = items.Where(w => items.All(o => o.SupersedesWorkItemId != w.CalOesMarsWorkItemId)).ToList();
					if (current.Count == 0) return;
					decimal? total = source switch
					{
						RevenueSources.CalOesMarsExpected => current.Any(w => w.ExpectedTotal.HasValue) ? current.Sum(w => w.ExpectedTotal ?? 0m) : null,
						RevenueSources.CalOesMarsApproved => current.Any(w => w.ApprovedTotal.HasValue) ? current.Sum(w => w.ApprovedTotal ?? 0m) : null,
						_ => current.Any(w => w.PaidTotal.HasValue) ? current.Sum(w => w.PaidTotal ?? 0m) : null
					};
					if (!total.HasValue) return;
					run.RevenueAmount = FieldCostCalculator.Round(total.Value); run.RevenueSourceId = string.Join(",", current.Select(w => w.CalOesMarsWorkItemId)); run.RevenueSourceVersion = string.Join(",", current.Select(w => w.RowVersion));
					return;
				}
				default:
					return;
			}
		}

		internal static void ApplyMargin(FieldCostRun run)
		{
			run.TotalLoadedCost = FieldCostCalculator.Round(run.PersonnelTotal + run.ResourceTotal + run.ConsumableTotal + run.ExpenseTotal + run.OverheadTotal);
			run.BreakEvenRevenue = run.TotalLoadedCost;
			if (run.RevenueAmount.HasValue)
			{
				run.ContributionMargin = FieldCostCalculator.Round(run.RevenueAmount.Value - run.TotalLoadedCost);
				run.ContributionMarginPercent = run.RevenueAmount.Value == 0 ? null : FieldCostCalculator.Round(run.ContributionMargin.Value / run.RevenueAmount.Value * 100m);
			}
			else { run.ContributionMargin = null; run.ContributionMarginPercent = null; }
		}

		/// <summary>Earlier unfrozen runs of the same context and type are replaced; the latest frozen one is recorded as superseded-by-this.</summary>
		private async Task<FieldCostRun> PersistRunAsync(FieldCostRun run, List<FieldCostLine> lines, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken)
		{
			var previous = run.ContextType switch
			{
				(int)FieldCostContextTypes.Bid => await GetRunsForBidAsync(run.BidId, departmentId),
				(int)FieldCostContextTypes.Call => await GetRunsForCallAsync(run.CallId.Value, departmentId),
				_ => await GetRunsForDeploymentAsync(run.DeploymentId, departmentId)
			};
			previous = previous.Where(p => p.RunType == run.RunType).ToList();
			foreach (var stale in previous.Where(p => !p.IsFrozen))
			{
				await _lines.DeleteByRunAsync(stale.FieldCostRunId, cancellationToken);
				stale.IsDeleted = true; stale.EditedOn = DateTime.UtcNow; stale.EditedByUserId = userId;
				await _runs.SaveOrUpdateAsync(stale, cancellationToken);
			}
			run.SupersedesRunId = previous.Where(p => p.Status == (int)FieldCostRunStatuses.Frozen).OrderByDescending(p => p.FrozenOn).FirstOrDefault()?.FieldCostRunId;
			var saved = await _runs.SaveOrUpdateAsync(run, cancellationToken);
			var order = 0;
			foreach (var line in lines)
			{
				line.FieldCostRunId = saved.FieldCostRunId; line.DepartmentId = departmentId; line.SortOrder = order++; line.AddedOn = saved.AddedOn; line.AddedByUserId = userId;
				await _seam.SaveAsync(_lines, line, null, departmentId, WorkforceProtectedFields.CostLine, cancellationToken);
			}
			Audit(departmentId, userId, AuditLogTypes.FieldCostRunCreated, ipAddress, userAgent, null, RunSnapshot(saved));
			return await GetRunAsync(saved.FieldCostRunId, departmentId);
		}

		public async Task<FieldCostRun> FreezeCostRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await _runs.GetByIdForDepartmentAsync(runId ?? string.Empty, departmentId);
			if (run == null || run.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			if (run.IsFrozen) return await GetRunAsync(run.FieldCostRunId, departmentId);
			var before = RunSnapshot(run);
			var now = DateTime.UtcNow;
			run.Status = (int)FieldCostRunStatuses.Frozen; run.FrozenByUserId = userId; run.FrozenOn = now; run.EditedOn = now; run.EditedByUserId = userId;
			await _runs.SaveOrUpdateAsync(run, cancellationToken);
			if (!string.IsNullOrWhiteSpace(run.SupersedesRunId))
			{
				var superseded = await _runs.GetByIdForDepartmentAsync(run.SupersedesRunId, departmentId);
				if (superseded != null && superseded.Status == (int)FieldCostRunStatuses.Frozen) { superseded.Status = (int)FieldCostRunStatuses.Superseded; superseded.EditedOn = now; superseded.EditedByUserId = userId; await _runs.SaveOrUpdateAsync(superseded, cancellationToken); }
			}
			Audit(departmentId, userId, AuditLogTypes.FieldCostRunFrozen, ipAddress, userAgent, before, RunSnapshot(run));
			return await GetRunAsync(run.FieldCostRunId, departmentId);
		}

		public async Task<FieldCostComparison> CompareEstimateToActualAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId ?? string.Empty, departmentId);
			if (deployment == null || deployment.IsDeleted) return null;
			var actual = (await GetRunsForDeploymentAsync(deployment.DeploymentId, departmentId)).Where(r => r.RunType == (int)FieldCostRunTypes.Actual).OrderByDescending(r => r.Status == (int)FieldCostRunStatuses.Frozen).ThenByDescending(r => r.AddedOn).FirstOrDefault();
			var estimate = string.IsNullOrWhiteSpace(deployment.BidId) ? null : (await GetRunsForBidAsync(deployment.BidId, departmentId)).Where(r => r.RunType == (int)FieldCostRunTypes.Estimate).OrderByDescending(r => r.Status == (int)FieldCostRunStatuses.Frozen).ThenByDescending(r => r.AddedOn).FirstOrDefault();
			return new FieldCostComparison { Estimate = estimate, Actual = actual };
		}

		public async Task<FieldCostSummary> GetFieldCostSummaryAsync(string runId, int departmentId)
		{
			var run = await _runs.GetByIdForDepartmentAsync(runId ?? string.Empty, departmentId);
			if (run == null || run.IsDeleted) return null;
			return new FieldCostSummary
			{
				FieldCostRunId = run.FieldCostRunId, ContextType = run.ContextType, RunType = run.RunType, Status = run.Status, ThroughDate = run.ThroughDate, Currency = run.Currency,
				PersonnelTotal = run.PersonnelTotal, ResourceTotal = run.ResourceTotal, ConsumableTotal = run.ConsumableTotal, ExpenseTotal = run.ExpenseTotal, OverheadTotal = run.OverheadTotal, TotalLoadedCost = run.TotalLoadedCost,
				RevenueSource = run.RevenueSource, RevenueAmount = run.RevenueAmount, ContributionMargin = run.ContributionMargin, ContributionMarginPercent = run.ContributionMarginPercent, BreakEvenRevenue = run.BreakEvenRevenue,
				MissingInputCount = run.MissingInputCount, FrozenOn = run.FrozenOn
			};
		}

		public async Task<bool> DeleteRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await _runs.GetByIdForDepartmentAsync(runId ?? string.Empty, departmentId);
			if (run == null || run.IsDeleted) return false;
			if (run.IsFrozen) throw new InvalidOperationException("workforce_run_frozen");
			await _lines.DeleteByRunAsync(run.FieldCostRunId, cancellationToken);
			run.IsDeleted = true; run.EditedOn = DateTime.UtcNow; run.EditedByUserId = userId;
			await _runs.SaveOrUpdateAsync(run, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.FieldCostRunCreated, ipAddress, userAgent, RunSnapshot(run), null);
			return true;
		}

		#endregion

		#region Run builder

		/// <summary>Accumulates lines and totals for one run. Personnel lines keep the rate out of the clear column and put the priced detail into the protected envelope.</summary>
		internal sealed class RunBuilder
		{
			private readonly Dictionary<string, int?> _versions = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
			public string Currency { get; }
			public DateTime AsOf { get; }
			public List<FieldCostLine> Lines { get; } = new List<FieldCostLine>();
			public decimal Personnel { get; private set; }
			public decimal Resource { get; private set; }
			public decimal Consumable { get; private set; }
			public decimal Expense { get; private set; }
			public int Missing { get; private set; }

			public RunBuilder(string currency, DateTime asOf) { Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant(); AsOf = asOf; }

			public void AddLabor(LaborCostResult result, LaborWorkQuantity work, string subjectType, string subjectId, string label, bool estimated)
			{
				Personnel += result.LoadedCost;
				if (result.NeedsReview) Missing++;
				foreach (var d in result.Details) if (!string.IsNullOrWhiteSpace(d.ComponentId)) _versions[d.ComponentId] = d.Version;
				Lines.Add(new FieldCostLine
				{
					LineDate = work.WorkDate, Category = (int)FieldCostCategories.Personnel, SubjectType = subjectType, SubjectId = subjectId, SubjectLabel = label, Component = ((PayCodes)work.PayCode).ToString(),
					Quantity = work.Hours, Unit = "hour", Rate = null, Amount = result.LoadedCost, SourceType = work.SourceType, SourceId = work.SourceId, IsEstimated = estimated || result.IsEstimated, IsFallback = result.IsFallback,
					ReviewReason = result.ReviewReasons.Count == 0 ? null : string.Join(",", result.ReviewReasons.Distinct()),
					ProtectedDetailJson = JsonConvert.SerializeObject(new { work.WorkforceEmploymentId, result.BaseRate, result.Multiplier, result.PayAmount, result.PayComponentAmount, result.EmployerCostAmount, result.Details })
				});
			}

			public void AddResource(ResourceCostResult result, ResourceUsageQuantity usage, string subjectType, string subjectId, string label, bool estimated)
			{
				if (result.NeedsReview) Missing++;
				if (result.Details.Count == 0)
				{
					Lines.Add(new FieldCostLine { LineDate = usage.UsageDate, Category = (int)FieldCostCategories.Resource, SubjectType = subjectType, SubjectId = subjectId, SubjectLabel = label, Component = "NoProfile", Quantity = usage.Miles + usage.EngineHours + usage.OperatingHours + usage.Days, Unit = "mixed", Rate = null, Amount = 0m, SourceType = usage.SourceType, SourceId = usage.SourceId, IsEstimated = estimated, IsFallback = result.IsFallback, ReviewReason = string.Join(",", result.ReviewReasons.Distinct()) });
					return;
				}
				foreach (var d in result.Details)
				{
					if (!string.IsNullOrWhiteSpace(d.ComponentId)) _versions[d.ComponentId] = d.Version;
					var category = d.Category == ResourceCostCategories.Consumables.ToString() ? FieldCostCategories.Consumable : FieldCostCategories.Resource;
					if (category == FieldCostCategories.Consumable) Consumable += d.Amount; else Resource += d.Amount;
					Lines.Add(new FieldCostLine
					{
						LineDate = usage.UsageDate, Category = (int)category, SubjectType = subjectType, SubjectId = subjectId, SubjectLabel = label, Component = d.Category, Quantity = d.Quantity, Unit = d.Unit ?? d.Basis, Rate = d.Blocked ? null : d.Rate, Amount = d.Amount,
						SourceType = usage.SourceType, SourceId = usage.SourceId, IsEstimated = estimated, IsFallback = result.IsFallback, ReviewReason = d.Blocked ? d.Reason : (result.ReviewReasons.Contains("usage_needs_review") ? "usage_needs_review" : null)
					});
				}
			}

			public void AddExpense(DateTime date, string component, string description, decimal amount, string sourceType, string sourceId)
			{
				var value = FieldCostCalculator.Round(amount);
				Expense += value;
				Lines.Add(new FieldCostLine { LineDate = date, Category = (int)FieldCostCategories.Expense, SubjectType = "Expense", SubjectId = sourceId, SubjectLabel = description, Component = component, Quantity = 1, Unit = "each", Rate = value, Amount = value, SourceType = sourceType, SourceId = sourceId, IsEstimated = false });
			}

			public FieldCostRun Build(FieldCostRun run)
			{
				run.Currency = Currency;
				run.PersonnelTotal = FieldCostCalculator.Round(Personnel); run.ResourceTotal = FieldCostCalculator.Round(Resource); run.ConsumableTotal = FieldCostCalculator.Round(Consumable); run.ExpenseTotal = FieldCostCalculator.Round(Expense); run.OverheadTotal = 0m;
				run.MissingInputCount = Missing;
				run.Status = Missing > 0 ? (int)FieldCostRunStatuses.NeedsReview : (int)FieldCostRunStatuses.Draft;
				run.InputVersions = JsonConvert.SerializeObject(_versions);
				return run;
			}
		}

		#endregion

		#region Helpers

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		private static string Snapshot(ResourceCostProfile profile) { var clone = profile.CloneJson(); clone.Components = null; clone.SubjectName = null; return clone.CloneJsonToString(); }
		private static string RunSnapshot(FieldCostRun run) { var clone = run.CloneJson(); clone.Lines = null; return clone.CloneJsonToString(); }

		private void Audit(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, string after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before; audit.After = after;
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
