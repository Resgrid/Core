using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Contractor rate schedules (Workforce &amp; Business Operations plan, C4; decisions 16, 17, 21). Bands are
	/// explicit dollars, crew families share a <see cref="RateScheduleEntry.GroupKey"/>, the policy rides the
	/// schedule as typed JSON. Nothing here is protected data (rates are commercial, not personal). Callers authorize.
	/// </summary>
	public class RateScheduleService : IRateScheduleService
	{
		private readonly IRateScheduleRepository _schedules;
		private readonly IRateScheduleEntryRepository _entries;
		private readonly IRateScheduleEntryBandRepository _bands;
		private readonly IRatePremiumRepository _premiums;
		private readonly IServiceContractRepository _contracts;
		private readonly ICustomerBillingProfileRepository _profiles;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;

		public RateScheduleService(IRateScheduleRepository schedules, IRateScheduleEntryRepository entries, IRateScheduleEntryBandRepository bands, IRatePremiumRepository premiums,
			IServiceContractRepository contracts, ICustomerBillingProfileRepository profiles, IEventAggregator eventAggregator, IUnitOfWork unitOfWork)
		{
			_schedules = schedules;
			_entries = entries;
			_bands = bands;
			_premiums = premiums;
			_contracts = contracts;
			_profiles = profiles;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
		}

		#region Schedules

		public async Task<List<RateSchedule>> GetSchedulesForDepartmentAsync(int departmentId, bool includeInactive = false) =>
			(await _schedules.GetForDepartmentAsync(departmentId, includeInactive))?.ToList() ?? new List<RateSchedule>();

		public async Task<RateSchedule> GetScheduleByIdAsync(string rateScheduleId, int departmentId, bool includeInactive = false)
		{
			if (string.IsNullOrWhiteSpace(rateScheduleId)) return null;
			var schedule = await _schedules.GetByIdForDepartmentAsync(rateScheduleId, departmentId);
			if (schedule == null || schedule.IsDeleted) return null;
			await LoadGraphAsync(schedule, includeInactive);
			return schedule;
		}

		private async Task LoadGraphAsync(RateSchedule schedule, bool includeInactive)
		{
			schedule.Entries = (await _entries.GetByScheduleAsync(schedule.RateScheduleId, includeInactive))?.ToList() ?? new List<RateScheduleEntry>();
			var bands = (await _bands.GetByScheduleAsync(schedule.RateScheduleId))?.ToList() ?? new List<RateScheduleEntryBand>();
			foreach (var entry in schedule.Entries)
				entry.Bands = bands.Where(b => string.Equals(b.RateScheduleEntryId, entry.RateScheduleEntryId, StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.SortOrder).ThenBy(b => b.BandType).ToList();
			schedule.Premiums = (await _premiums.GetByScheduleAsync(schedule.RateScheduleId, includeInactive))?.ToList() ?? new List<RatePremium>();
		}

		public async Task<RateSchedule> SaveScheduleAsync(RateSchedule schedule, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (schedule == null) throw new ArgumentNullException(nameof(schedule));
			if (string.IsNullOrWhiteSpace(schedule.Name)) throw new InvalidOperationException("rateschedules_name_required");
			if (schedule.EffectiveOn.HasValue && schedule.ExpiresOn.HasValue && schedule.ExpiresOn < schedule.EffectiveOn) throw new InvalidOperationException("rateschedules_dates_invalid");
			schedule.Currency = NormalizeCurrency(schedule.Currency);
			schedule.PolicyJson = string.IsNullOrWhiteSpace(schedule.PolicyJson) ? new RateSchedulePolicy().ToJson() : RateSchedulePolicy.Parse(schedule.PolicyJson).ToJson();

			var existing = string.IsNullOrWhiteSpace(schedule.RateScheduleId) ? null : await _schedules.GetByIdForDepartmentAsync(schedule.RateScheduleId, schedule.DepartmentId);
			var now = DateTime.UtcNow;
			RateSchedule saved;
			if (existing == null)
			{
				schedule.RateScheduleId = null;
				schedule.AddedOn = now;
				schedule.AddedByUserId = userId;
				schedule.IsDeleted = false;
				saved = await _schedules.SaveOrUpdateAsync(schedule, cancellationToken);
				Audit(schedule.DepartmentId, userId, AuditLogTypes.RateScheduleCreated, ipAddress, userAgent, null, saved);
			}
			else
			{
				if (existing.IsDeleted) throw new InvalidOperationException("rateschedules_not_found");
				var before = Snapshot(existing);
				existing.Name = schedule.Name.Trim();
				existing.Description = Trim(schedule.Description);
				existing.Currency = schedule.Currency;
				existing.EffectiveOn = schedule.EffectiveOn;
				existing.ExpiresOn = schedule.ExpiresOn;
				existing.PolicyJson = schedule.PolicyJson;
				existing.IsActive = schedule.IsActive;
				existing.EditedOn = now;
				existing.EditedByUserId = userId;
				saved = await _schedules.SaveOrUpdateAsync(existing, cancellationToken);
				Audit(schedule.DepartmentId, userId, AuditLogTypes.RateScheduleUpdated, ipAddress, userAgent, before, saved);
			}
			return await GetScheduleByIdAsync(saved.RateScheduleId, saved.DepartmentId, includeInactive: true);
		}

		public async Task<bool> DeleteScheduleAsync(string rateScheduleId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var existing = await _schedules.GetByIdForDepartmentAsync(rateScheduleId, departmentId);
			if (existing == null || existing.IsDeleted) return false;
			var inUse = (await _contracts.GetForDepartmentAsync(departmentId, null))?.Any(c => string.Equals(c.RateScheduleId, rateScheduleId, StringComparison.OrdinalIgnoreCase) && c.Status is (int)ServiceContractStatuses.Active or (int)ServiceContractStatuses.Draft) ?? false;
			if (inUse) throw new InvalidOperationException("rateschedules_in_use");

			var before = Snapshot(existing);
			existing.IsDeleted = true;
			existing.IsActive = false;
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;
			await _schedules.SaveOrUpdateAsync(existing, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.RateScheduleDeleted, ipAddress, userAgent, before, existing);
			return true;
		}

		public async Task<RateSchedule> CloneScheduleAsync(string rateScheduleId, int departmentId, string newName, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var source = await GetScheduleByIdAsync(rateScheduleId, departmentId, includeInactive: true);
			if (source == null) throw new InvalidOperationException("rateschedules_not_found");
			var name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} (copy)" : newName.Trim();
			return await CreateGraphAsync(departmentId, source, name, userId, ipAddress, userAgent, cancellationToken);
		}

		/// <summary>Writes a full schedule graph from a template (clone or import), allocating new ids and remapping premium references.</summary>
		private async Task<RateSchedule> CreateGraphAsync(int departmentId, RateSchedule template, string name, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var schedule = new RateSchedule
			{
				DepartmentId = departmentId, Name = name, Description = Trim(template.Description), Currency = NormalizeCurrency(template.Currency),
				EffectiveOn = template.EffectiveOn, ExpiresOn = template.ExpiresOn, PolicyJson = RateSchedulePolicy.Parse(template.PolicyJson).ToJson(),
				IsActive = true, AddedOn = now, AddedByUserId = userId
			};
			var saved = await _schedules.SaveOrUpdateAsync(schedule, cancellationToken);

			foreach (var entry in (template.Entries ?? new List<RateScheduleEntry>()).Where(e => !e.IsDeleted).OrderBy(e => e.SortOrder))
			{
				var copy = new RateScheduleEntry
				{
					RateScheduleId = saved.RateScheduleId, DepartmentId = departmentId, EntryType = entry.EntryType, Name = entry.Name, Code = entry.Code, GroupKey = entry.GroupKey, CrewSize = entry.CrewSize,
					CertificationCode = entry.CertificationCode, UnitTypeId = entry.UnitTypeId, InventoryItemId = entry.InventoryItemId, InventoryCategoryId = entry.InventoryCategoryId, BillingBasis = entry.BillingBasis,
					RequiredCertificationsJson = entry.RequiredCertificationsJson, SortOrder = entry.SortOrder, IsActive = entry.IsActive, AddedOn = now, AddedByUserId = userId
				};
				var savedEntry = await _entries.SaveOrUpdateAsync(copy, cancellationToken);
				foreach (var band in (entry.Bands ?? new List<RateScheduleEntryBand>()).OrderBy(b => b.SortOrder))
					await _bands.SaveOrUpdateAsync(CopyBand(band, savedEntry.RateScheduleEntryId), cancellationToken);
			}

			foreach (var premium in (template.Premiums ?? new List<RatePremium>()).Where(p => !p.IsDeleted))
				await _premiums.SaveOrUpdateAsync(new RatePremium
				{
					RateScheduleId = saved.RateScheduleId, DepartmentId = departmentId, Name = premium.Name, Code = premium.Code,
					StandbyAdder = premium.StandbyAdder, DeploymentAdder = premium.DeploymentAdder, Overtime1Adder = premium.Overtime1Adder, Overtime2Adder = premium.Overtime2Adder,
					IsActive = premium.IsActive, AddedOn = now, AddedByUserId = userId
				}, cancellationToken);

			var result = await GetScheduleByIdAsync(saved.RateScheduleId, departmentId, includeInactive: true);
			Audit(departmentId, userId, AuditLogTypes.RateScheduleCreated, ipAddress, userAgent, null, result);
			return result;
		}

		private static RateScheduleEntryBand CopyBand(RateScheduleEntryBand band, string entryId) => new RateScheduleEntryBand
		{
			RateScheduleEntryId = entryId, BandType = band.BandType, Rate = band.Rate, ThresholdStartHours = band.ThresholdStartHours, ThresholdEndHours = band.ThresholdEndHours,
			DailyTierMinHours = band.DailyTierMinHours, DailyTierMaxHours = band.DailyTierMaxHours, FreeUnitsPerDay = band.FreeUnitsPerDay, RequiresAirTravel = band.RequiresAirTravel,
			MealCode = Trim(band.MealCode), Label = Trim(band.Label), SortOrder = band.SortOrder
		};

		#endregion

		#region Entries, bands, premiums

		public async Task<RateScheduleEntry> GetEntryByIdAsync(string rateScheduleEntryId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(rateScheduleEntryId)) return null;
			var entry = await _entries.GetByIdAsync(rateScheduleEntryId);
			if (entry == null || entry.IsDeleted || entry.DepartmentId != departmentId) return null;
			entry.Bands = (await _bands.GetByEntryAsync(entry.RateScheduleEntryId))?.ToList() ?? new List<RateScheduleEntryBand>();
			return entry;
		}

		public async Task<RateScheduleEntry> SaveEntryAsync(RateScheduleEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (entry == null) throw new ArgumentNullException(nameof(entry));
			var schedule = await _schedules.GetByIdForDepartmentAsync(entry.RateScheduleId, entry.DepartmentId);
			if (schedule == null || schedule.IsDeleted) throw new InvalidOperationException("rateschedules_not_found");
			if (string.IsNullOrWhiteSpace(entry.Name)) throw new InvalidOperationException("rateschedules_entry_name_required");
			if (!Enum.IsDefined(typeof(RateEntryTypes), entry.EntryType) || !Enum.IsDefined(typeof(BillingBases), entry.BillingBasis)) throw new InvalidOperationException("rateschedules_entry_invalid");
			if (entry.EntryType == (int)RateEntryTypes.Crew && (!entry.CrewSize.HasValue || entry.CrewSize <= 0)) throw new InvalidOperationException("rateschedules_crew_size_required");
			var bands = (entry.Bands ?? new List<RateScheduleEntryBand>()).Where(b => b != null).ToList();
			if (bands.Any(b => !Enum.IsDefined(typeof(RateBandTypes), b.BandType) || b.Rate < 0)) throw new InvalidOperationException("rateschedules_band_invalid");
			if (bands.GroupBy(b => b.BandType).Any(g => g.Count() > 1 && g.Key is not ((int)RateBandTypes.DailyDeployment or (int)RateBandTypes.DailyStandby or (int)RateBandTypes.PerDiemMeal or (int)RateBandTypes.Custom)))
				throw new InvalidOperationException("rateschedules_band_duplicate");
			if (!string.IsNullOrWhiteSpace(entry.RequiredCertificationsJson)) { _ = RequiredCertification.Parse(entry.RequiredCertificationsJson); }

			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(entry.RateScheduleEntryId) ? null : await _entries.GetByIdAsync(entry.RateScheduleEntryId);
			if (existing != null && (existing.DepartmentId != entry.DepartmentId || existing.IsDeleted)) throw new InvalidOperationException("rateschedules_entry_not_found");
			var before = existing == null ? null : Snapshot(existing);

			var target = existing ?? new RateScheduleEntry { RateScheduleId = schedule.RateScheduleId, DepartmentId = entry.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.EntryType = entry.EntryType;
			target.Name = entry.Name.Trim();
			target.Code = Trim(entry.Code);
			target.GroupKey = Trim(entry.GroupKey);
			target.CrewSize = entry.CrewSize;
			target.CertificationCode = Trim(entry.CertificationCode);
			target.UnitTypeId = entry.UnitTypeId;
			target.InventoryItemId = Trim(entry.InventoryItemId);
			target.InventoryCategoryId = Trim(entry.InventoryCategoryId);
			target.BillingBasis = entry.BillingBasis;
			target.RequiredCertificationsJson = Trim(entry.RequiredCertificationsJson);
			target.SortOrder = entry.SortOrder;
			target.IsActive = entry.IsActive;
			if (existing != null) { target.EditedOn = now; target.EditedByUserId = userId; }

			var saved = await _entries.SaveOrUpdateAsync(target, cancellationToken);
			if (existing != null) await _bands.DeleteByEntryAsync(saved.RateScheduleEntryId, cancellationToken);
			var order = 0;
			foreach (var band in bands.OrderBy(b => b.SortOrder).ThenBy(b => b.BandType))
			{
				var copy = CopyBand(band, saved.RateScheduleEntryId);
				copy.SortOrder = order++;
				await _bands.SaveOrUpdateAsync(copy, cancellationToken);
			}
			Touch(schedule, userId, now);
			await _schedules.SaveOrUpdateAsync(schedule, cancellationToken);
			var result = await GetEntryByIdAsync(saved.RateScheduleEntryId, entry.DepartmentId);
			Audit(entry.DepartmentId, userId, AuditLogTypes.RateScheduleEntryChanged, ipAddress, userAgent, before, result);
			return result;
		}

		public async Task<bool> DeleteEntryAsync(string rateScheduleEntryId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var existing = await _entries.GetByIdAsync(rateScheduleEntryId);
			if (existing == null || existing.IsDeleted || existing.DepartmentId != departmentId) return false;
			var before = Snapshot(existing);
			existing.IsDeleted = true;
			existing.IsActive = false;
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;
			await _entries.SaveOrUpdateAsync(existing, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.RateScheduleEntryChanged, ipAddress, userAgent, before, existing);
			return true;
		}

		public async Task<RatePremium> SavePremiumAsync(RatePremium premium, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (premium == null) throw new ArgumentNullException(nameof(premium));
			var schedule = await _schedules.GetByIdForDepartmentAsync(premium.RateScheduleId, premium.DepartmentId);
			if (schedule == null || schedule.IsDeleted) throw new InvalidOperationException("rateschedules_not_found");
			if (string.IsNullOrWhiteSpace(premium.Name)) throw new InvalidOperationException("rateschedules_premium_name_required");
			if (premium.StandbyAdder < 0 || premium.DeploymentAdder < 0 || premium.Overtime1Adder < 0 || premium.Overtime2Adder < 0) throw new InvalidOperationException("rateschedules_premium_invalid");

			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(premium.RatePremiumId) ? null : await _premiums.GetByIdAsync(premium.RatePremiumId);
			if (existing != null && (existing.DepartmentId != premium.DepartmentId || existing.IsDeleted)) throw new InvalidOperationException("rateschedules_premium_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var target = existing ?? new RatePremium { RateScheduleId = schedule.RateScheduleId, DepartmentId = premium.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.Name = premium.Name.Trim();
			target.Code = Trim(premium.Code);
			target.StandbyAdder = premium.StandbyAdder;
			target.DeploymentAdder = premium.DeploymentAdder;
			target.Overtime1Adder = premium.Overtime1Adder;
			target.Overtime2Adder = premium.Overtime2Adder;
			target.IsActive = premium.IsActive;
			if (existing != null) { target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _premiums.SaveOrUpdateAsync(target, cancellationToken);
			Touch(schedule, userId, now);
			await _schedules.SaveOrUpdateAsync(schedule, cancellationToken);
			Audit(premium.DepartmentId, userId, AuditLogTypes.RatePremiumChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<bool> DeletePremiumAsync(string ratePremiumId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var existing = await _premiums.GetByIdAsync(ratePremiumId);
			if (existing == null || existing.IsDeleted || existing.DepartmentId != departmentId) return false;
			var before = Snapshot(existing);
			existing.IsDeleted = true;
			existing.IsActive = false;
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;
			await _premiums.SaveOrUpdateAsync(existing, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.RatePremiumChanged, ipAddress, userAgent, before, existing);
			return true;
		}

		#endregion

		#region Resolution, prefill, import/export

		public async Task<RateSchedule> GetEffectiveScheduleForContactAsync(string contactId, int departmentId, string serviceContractId = null)
		{
			// Contract → profile → department default; a schedule outside its effective window or inactive is skipped.
			var now = DateTime.UtcNow;
			async Task<RateSchedule> Current(string id)
			{
				var schedule = await GetScheduleByIdAsync(id, departmentId);
				return schedule != null && schedule.IsCurrent(now) ? schedule : null;
			}

			if (!string.IsNullOrWhiteSpace(serviceContractId))
			{
				var contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
				var fromContract = contract == null || contract.IsDeleted ? null : await Current(contract.RateScheduleId);
				if (fromContract != null) return fromContract;
			}
			if (!string.IsNullOrWhiteSpace(contactId))
			{
				var profile = await _profiles.GetByContactIdAsync(contactId, departmentId);
				var fromProfile = profile == null || profile.IsDeleted ? null : await Current(profile.DefaultRateScheduleId);
				if (fromProfile != null) return fromProfile;
			}
			var schedules = await GetSchedulesForDepartmentAsync(departmentId);
			var first = schedules.Where(s => s.IsCurrent(now)).OrderBy(s => s.EffectiveOn ?? DateTime.MinValue).ThenBy(s => s.AddedOn).ThenBy(s => s.Name).FirstOrDefault();
			return first == null ? null : await GetScheduleByIdAsync(first.RateScheduleId, departmentId);
		}

		public List<RateScheduleEntryBand> PrefillHourlyBands(decimal baseRate, decimal? standbyRate, decimal overtime1Multiplier, decimal? overtime1StartHours, decimal? overtime2Multiplier, decimal? overtime2StartHours)
		{
			if (baseRate < 0) throw new ArgumentOutOfRangeException(nameof(baseRate));
			var list = new List<RateScheduleEntryBand>();
			var order = 0;
			if (standbyRate.HasValue) list.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Standby, Rate = Money(standbyRate.Value), Label = "Standby", SortOrder = order++ });
			list.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Deployment, Rate = Money(baseRate), Label = "Deployment", ThresholdStartHours = 0, ThresholdEndHours = overtime1StartHours, SortOrder = order++ });
			if (overtime1Multiplier > 0 && overtime1StartHours.HasValue)
				list.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Overtime1, Rate = Money(baseRate * overtime1Multiplier), Label = $"Overtime ×{overtime1Multiplier:0.##}", ThresholdStartHours = overtime1StartHours, ThresholdEndHours = overtime2StartHours, SortOrder = order++ });
			if (overtime2Multiplier.HasValue && overtime2Multiplier > 0 && overtime2StartHours.HasValue)
				list.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Overtime2, Rate = Money(baseRate * overtime2Multiplier.Value), Label = $"Overtime ×{overtime2Multiplier.Value:0.##}", ThresholdStartHours = overtime2StartHours, SortOrder = order++ });
			return list;
		}

		public async Task<string> ExportScheduleJsonAsync(string rateScheduleId, int departmentId)
		{
			var schedule = await GetScheduleByIdAsync(rateScheduleId, departmentId, includeInactive: true);
			if (schedule == null) return null;
			var export = new RateScheduleExport
			{
				Name = schedule.Name, Description = schedule.Description, Currency = schedule.Currency, EffectiveOn = schedule.EffectiveOn, ExpiresOn = schedule.ExpiresOn, Policy = schedule.Policy,
				Entries = schedule.Entries.Where(e => !e.IsDeleted).OrderBy(e => e.SortOrder).Select(e => new RateScheduleExport.Entry
				{
					EntryType = e.EntryType, Name = e.Name, Code = e.Code, GroupKey = e.GroupKey, CrewSize = e.CrewSize, CertificationCode = e.CertificationCode, BillingBasis = e.BillingBasis,
					RequiredCertifications = e.RequiredCertifications, SortOrder = e.SortOrder, IsActive = e.IsActive,
					Bands = e.Bands.OrderBy(b => b.SortOrder).Select(b => new RateScheduleExport.Band
					{
						BandType = b.BandType, Rate = b.Rate, ThresholdStartHours = b.ThresholdStartHours, ThresholdEndHours = b.ThresholdEndHours, DailyTierMinHours = b.DailyTierMinHours, DailyTierMaxHours = b.DailyTierMaxHours,
						FreeUnitsPerDay = b.FreeUnitsPerDay, RequiresAirTravel = b.RequiresAirTravel, MealCode = b.MealCode, Label = b.Label, SortOrder = b.SortOrder
					}).ToList()
				}).ToList(),
				Premiums = schedule.Premiums.Where(p => !p.IsDeleted).Select(p => new RateScheduleExport.Premium { Name = p.Name, Code = p.Code, StandbyAdder = p.StandbyAdder, DeploymentAdder = p.DeploymentAdder, Overtime1Adder = p.Overtime1Adder, Overtime2Adder = p.Overtime2Adder, IsActive = p.IsActive }).ToList()
			};
			return JsonConvert.SerializeObject(export, Formatting.Indented);
		}

		public async Task<RateSchedule> ImportScheduleJsonAsync(int departmentId, string json, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var export = RateScheduleJsonImport.Read(json);

			// Import is a template, never a cross-department reference: ids, department scoping and inventory links are dropped.
			var template = new RateSchedule
			{
				Name = export.Name, Description = export.Description, Currency = export.Currency, EffectiveOn = export.EffectiveOn, ExpiresOn = export.ExpiresOn, PolicyJson = (export.Policy ?? new RateSchedulePolicy()).ToJson(),
				Entries = (export.Entries ?? new List<RateScheduleExport.Entry>()).Select(e => new RateScheduleEntry
				{
					EntryType = e.EntryType, Name = e.Name, Code = e.Code, GroupKey = e.GroupKey, CrewSize = e.CrewSize, CertificationCode = e.CertificationCode, BillingBasis = e.BillingBasis,
					RequiredCertificationsJson = e.RequiredCertifications == null || e.RequiredCertifications.Count == 0 ? null : JsonConvert.SerializeObject(e.RequiredCertifications), SortOrder = e.SortOrder, IsActive = e.IsActive,
					Bands = (e.Bands ?? new List<RateScheduleExport.Band>()).Select(b => new RateScheduleEntryBand
					{
						BandType = b.BandType, Rate = b.Rate, ThresholdStartHours = b.ThresholdStartHours, ThresholdEndHours = b.ThresholdEndHours, DailyTierMinHours = b.DailyTierMinHours, DailyTierMaxHours = b.DailyTierMaxHours,
						FreeUnitsPerDay = b.FreeUnitsPerDay, RequiresAirTravel = b.RequiresAirTravel, MealCode = b.MealCode, Label = b.Label, SortOrder = b.SortOrder
					}).ToList()
				}).ToList(),
				Premiums = (export.Premiums ?? new List<RateScheduleExport.Premium>()).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => new RatePremium { Name = p.Name, Code = p.Code, StandbyAdder = p.StandbyAdder, DeploymentAdder = p.DeploymentAdder, Overtime1Adder = p.Overtime1Adder, Overtime2Adder = p.Overtime2Adder, IsActive = p.IsActive }).ToList()
			};
			return await CreateGraphAsync(departmentId, template, export.Name.Trim(), userId, ipAddress, userAgent, cancellationToken);
		}

		/// <summary>The portable schedule document (no ids, no department references).</summary>
		public sealed class RateScheduleExport
		{
			public int FormatVersion { get; set; } = 1;
			[System.ComponentModel.DataAnnotations.Required] public string Name { get; set; }
			public string Description { get; set; }
			public string Currency { get; set; }
			public DateTime? EffectiveOn { get; set; }
			public DateTime? ExpiresOn { get; set; }
			public RateSchedulePolicy Policy { get; set; }
			public List<Entry> Entries { get; set; } = new List<Entry>();
			public List<Premium> Premiums { get; set; } = new List<Premium>();

			public sealed class Entry
			{
				public int EntryType { get; set; }
				[System.ComponentModel.DataAnnotations.Required] public string Name { get; set; }
				public string Code { get; set; }
				public string GroupKey { get; set; }
				public int? CrewSize { get; set; }
				public string CertificationCode { get; set; }
				public int BillingBasis { get; set; }
				public List<RequiredCertification> RequiredCertifications { get; set; }
				public int SortOrder { get; set; }
				public bool IsActive { get; set; } = true;
				public List<Band> Bands { get; set; } = new List<Band>();
			}

			public sealed class Band
			{
				public int BandType { get; set; }
				public decimal Rate { get; set; }
				public decimal? ThresholdStartHours { get; set; }
				public decimal? ThresholdEndHours { get; set; }
				public decimal? DailyTierMinHours { get; set; }
				public decimal? DailyTierMaxHours { get; set; }
				public decimal? FreeUnitsPerDay { get; set; }
				public bool RequiresAirTravel { get; set; }
				public string MealCode { get; set; }
				public string Label { get; set; }
				public int SortOrder { get; set; }
			}

			public sealed class Premium
			{
				[System.ComponentModel.DataAnnotations.Required] public string Name { get; set; }
				public string Code { get; set; }
				public decimal StandbyAdder { get; set; }
				public decimal DeploymentAdder { get; set; }
				public decimal Overtime1Adder { get; set; }
				public decimal Overtime2Adder { get; set; }
				public bool IsActive { get; set; } = true;
			}
		}

		#endregion

		#region Helpers

		private static void Touch(RateSchedule schedule, string userId, DateTime now) { schedule.EditedOn = now; schedule.EditedByUserId = userId; }
		private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		private static string NormalizeCurrency(string currency) => string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 ? "USD" : currency.Trim().ToUpperInvariant();

		private static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			if (clone is RateSchedule schedule) { schedule.Entries = null; schedule.Premiums = null; }
			return clone.CloneJsonToString();
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
