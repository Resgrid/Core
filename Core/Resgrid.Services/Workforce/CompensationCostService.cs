using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Invoicing;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// Compensation profiles (employee, role default, department default), pay / employer-cost components and the
	/// loaded-cost estimate for an approved work quantity (plan E3 <c>ICompensationCostService</c>). Every amount and
	/// multiplier rides the ADP seam (catalog 28); the costing engine decrypts through the <c>workforce-costing</c>
	/// workload purpose and never returns a value it could not resolve — it flags the estimate for review instead.
	/// </summary>
	public class CompensationCostService : ICompensationCostService
	{
		private readonly IEmployeeCompensationProfileRepository _profiles;
		private readonly IEmployeePayComponentRepository _payComponents;
		private readonly IEmployeeCostComponentRepository _costComponents;
		private readonly IWorkforceEmploymentRepository _employments;
		private readonly IWorkforceJobAssignmentRepository _assignments;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly WorkforceProtectionSeam _seam;

		public CompensationCostService(IEmployeeCompensationProfileRepository profiles, IEmployeePayComponentRepository payComponents, IEmployeeCostComponentRepository costComponents,
			IWorkforceEmploymentRepository employments, IWorkforceJobAssignmentRepository assignments, IEventAggregator eventAggregator, IUnitOfWork unitOfWork,
			Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_profiles = profiles;
			_payComponents = payComponents;
			_costComponents = costComponents;
			_employments = employments;
			_assignments = assignments;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_seam = new WorkforceProtectionSeam(protectedWrite, protectedRead, grant);
		}

		#region Profiles

		public async Task<List<EmployeeCompensationProfile>> GetProfilesForEmploymentAsync(string employmentId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(employmentId)) return new List<EmployeeCompensationProfile>();
			var rows = (await _profiles.GetByEmploymentAsync(employmentId))?.Where(p => p.DepartmentId == departmentId && !p.IsDeleted).OrderByDescending(p => p.EffectiveOn).ToList() ?? new List<EmployeeCompensationProfile>();
			await LoadComponentsAsync(rows, departmentId, null);
			return rows;
		}

		public async Task<List<EmployeeCompensationProfile>> GetDefaultProfilesAsync(int departmentId)
		{
			var rows = (await _profiles.GetDefaultsForDepartmentAsync(departmentId))?.Where(p => !p.IsDeleted).OrderBy(p => p.Scope).ThenBy(p => p.PersonnelRoleId).ThenByDescending(p => p.EffectiveOn).ToList() ?? new List<EmployeeCompensationProfile>();
			await LoadComponentsAsync(rows, departmentId, null);
			return rows;
		}

		public async Task<EmployeeCompensationProfile> GetProfileAsync(string profileId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(profileId)) return null;
			var row = await _profiles.GetByIdForDepartmentAsync(profileId, departmentId);
			if (row == null || row.IsDeleted) return null;
			await LoadComponentsAsync(new[] { row }, departmentId, null);
			return row;
		}

		/// <summary>Loads components and resolves protected values: a user read honours the grant, a workload read (purpose supplied) decrypts for the engine.</summary>
		private async Task<bool> LoadComponentsAsync(IReadOnlyList<EmployeeCompensationProfile> rows, int departmentId, string workloadPurpose)
		{
			if (rows.Count == 0) return true;
			var ids = rows.Select(r => r.EmployeeCompensationProfileId).ToList();
			var pay = (await _payComponents.GetByProfilesAsync(ids))?.Where(c => !c.IsDeleted).ToList() ?? new List<EmployeePayComponent>();
			var cost = (await _costComponents.GetByProfilesAsync(ids))?.Where(c => !c.IsDeleted).ToList() ?? new List<EmployeeCostComponent>();
			var ok = true;
			if (workloadPurpose == null)
			{
				await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.Compensation);
				await _seam.ResolveForReadAsync(pay, departmentId, WorkforceProtectedFields.PayComponent);
				await _seam.ResolveForReadAsync(cost, departmentId, WorkforceProtectedFields.CostComponent);
			}
			else
			{
				ok &= await _seam.ResolveForWorkloadAsync(rows, departmentId, workloadPurpose, WorkforceProtectedFields.Compensation);
				ok &= await _seam.ResolveForWorkloadAsync(pay, departmentId, workloadPurpose, WorkforceProtectedFields.PayComponent);
				ok &= await _seam.ResolveForWorkloadAsync(cost, departmentId, workloadPurpose, WorkforceProtectedFields.CostComponent);
			}
			foreach (var row in rows)
			{
				row.PayComponents = pay.Where(c => c.EmployeeCompensationProfileId == row.EmployeeCompensationProfileId).OrderBy(c => c.Category).ThenBy(c => c.Name).ToList();
				row.CostComponents = cost.Where(c => c.EmployeeCompensationProfileId == row.EmployeeCompensationProfileId).OrderBy(c => c.Category).ThenBy(c => c.Name).ToList();
			}
			return ok;
		}

		public async Task<EmployeeCompensationProfile> SaveProfileAsync(EmployeeCompensationProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			var audits = new List<AuditEvent>();
			var saved = await WriteProfileAsync(profile, userId, ipAddress, userAgent, audits, cancellationToken);
			Publish(audits);
			return await GetProfileAsync(saved.EmployeeCompensationProfileId, profile.DepartmentId);
		}

		public async Task<EmployeeCompensationProfile> SaveProfileWithComponentsAsync(EmployeeCompensationProfile profile, List<EmployeePayComponent> payComponents, List<EmployeeCostComponent> costComponents,
			string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			// One transaction: a failed component write must not leave the profile (or a partial component set) behind.
			// The audit events are the same two the separate calls raise, published only once the writes have committed.
			var audits = new List<AuditEvent>();
			var saved = await TransactionAsync(async () =>
			{
				var row = await WriteProfileAsync(profile, userId, ipAddress, userAgent, audits, cancellationToken);
				return await WriteComponentsAsync(row.EmployeeCompensationProfileId, profile.DepartmentId, payComponents, costComponents, userId, ipAddress, userAgent, audits, cancellationToken);
			}, cancellationToken);
			Publish(audits);
			return await GetProfileAsync(saved.EmployeeCompensationProfileId, profile.DepartmentId);
		}

		private async Task<EmployeeCompensationProfile> WriteProfileAsync(EmployeeCompensationProfile profile, string userId, string ipAddress, string userAgent, List<AuditEvent> audits, CancellationToken cancellationToken)
		{
			if (!Enum.IsDefined(typeof(CompensationScopes), profile.Scope)) throw new InvalidOperationException("workforce_scope_invalid");
			if (!Enum.IsDefined(typeof(PayBases), profile.PayBasis)) throw new InvalidOperationException("workforce_pay_basis_invalid");
			if (profile.ExpiresOn.HasValue && profile.ExpiresOn < profile.EffectiveOn) throw new InvalidOperationException("workforce_dates_invalid");
			var scope = (CompensationScopes)profile.Scope;
			if (scope == CompensationScopes.Employee)
			{
				var employment = await _employments.GetByIdForDepartmentAsync(profile.WorkforceEmploymentId ?? string.Empty, profile.DepartmentId);
				if (employment == null || employment.IsDeleted) throw new InvalidOperationException("workforce_employment_not_found");
				profile.PersonnelRoleId = null;
			}
			else if (scope == CompensationScopes.RoleDefault) { if (!profile.PersonnelRoleId.HasValue) throw new InvalidOperationException("workforce_role_required"); profile.WorkforceEmploymentId = null; }
			else { profile.WorkforceEmploymentId = null; profile.PersonnelRoleId = null; }
			var existing = string.IsNullOrWhiteSpace(profile.EmployeeCompensationProfileId) ? null : await _profiles.GetByIdForDepartmentAsync(profile.EmployeeCompensationProfileId, profile.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			// A profile's period never overlaps another of the same scope / subject.
			var siblings = scope == CompensationScopes.Employee
				? (await _profiles.GetByEmploymentAsync(profile.WorkforceEmploymentId))?.ToList() ?? new List<EmployeeCompensationProfile>()
				: (await _profiles.GetDefaultsForDepartmentAsync(profile.DepartmentId))?.Where(p => p.Scope == profile.Scope && p.PersonnelRoleId == profile.PersonnelRoleId).ToList() ?? new List<EmployeeCompensationProfile>();
			if (siblings.Any(s => !s.IsDeleted && s.EmployeeCompensationProfileId != profile.EmployeeCompensationProfileId && Overlaps(s, profile))) throw new InvalidOperationException("workforce_profile_overlap");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new EmployeeCompensationProfile { DepartmentId = profile.DepartmentId, AddedOn = now, AddedByUserId = userId };
			var priorRates = existing == null ? null : (existing.BaseAmount, existing.RegularHourlyEquivalent, existing.RateMultipliersJson, existing.PayBasis).ToString();
			target.Scope = profile.Scope; target.WorkforceEmploymentId = profile.WorkforceEmploymentId; target.PersonnelRoleId = profile.PersonnelRoleId;
			target.EffectiveOn = profile.EffectiveOn.Date; target.ExpiresOn = profile.ExpiresOn?.Date; target.Currency = string.IsNullOrWhiteSpace(profile.Currency) ? "USD" : profile.Currency.Trim().ToUpperInvariant();
			target.PayBasis = profile.PayBasis; target.StandardHoursPerDay = profile.StandardHoursPerDay; target.StandardHoursPerWeek = profile.StandardHoursPerWeek; target.StandardHoursPerYear = profile.StandardHoursPerYear;
			target.Source = Trim(profile.Source) ?? "manual"; target.ImportBatchId = Trim(profile.ImportBatchId); target.SourceChecksum = Trim(profile.SourceChecksum);
			foreach (var accessor in WorkforceProtectedFields.Compensation) Keep(existing, target, profile, accessor.Value);
			// Any change to the rates un-approves the profile; approval is an explicit step.
			if (priorRates != null && priorRates != (target.BaseAmount, target.RegularHourlyEquivalent, target.RateMultipliersJson, target.PayBasis).ToString()) { target.IsApproved = false; target.ApprovedByUserId = null; target.ApprovedOn = null; }
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_profiles, target, existing, profile.DepartmentId, WorkforceProtectedFields.Compensation, cancellationToken);
			audits.Add(BuildAudit(profile.DepartmentId, userId, AuditLogTypes.WorkforceCompensationChanged, ipAddress, userAgent, before, saved));
			return saved;
		}

		private static bool Overlaps(EmployeeCompensationProfile a, EmployeeCompensationProfile b) => a.EffectiveOn.Date <= (b.ExpiresOn ?? DateTime.MaxValue).Date && (a.ExpiresOn ?? DateTime.MaxValue).Date >= b.EffectiveOn.Date;

		public async Task<EmployeeCompensationProfile> SaveComponentsAsync(string profileId, int departmentId, List<EmployeePayComponent> payComponents, List<EmployeeCostComponent> costComponents, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var audits = new List<AuditEvent>();
			var profile = await WriteComponentsAsync(profileId, departmentId, payComponents, costComponents, userId, ipAddress, userAgent, audits, cancellationToken);
			Publish(audits);
			return await GetProfileAsync(profile.EmployeeCompensationProfileId, departmentId);
		}

		private async Task<EmployeeCompensationProfile> WriteComponentsAsync(string profileId, int departmentId, List<EmployeePayComponent> payComponents, List<EmployeeCostComponent> costComponents, string userId, string ipAddress, string userAgent,
			List<AuditEvent> audits, CancellationToken cancellationToken)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(profileId ?? string.Empty, departmentId);
			if (profile == null || profile.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var now = DateTime.UtcNow;
			var existingPay = (await _payComponents.GetByProfileAsync(profile.EmployeeCompensationProfileId))?.Where(c => !c.IsDeleted).ToList() ?? new List<EmployeePayComponent>();
			var existingCost = (await _costComponents.GetByProfileAsync(profile.EmployeeCompensationProfileId))?.Where(c => !c.IsDeleted).ToList() ?? new List<EmployeeCostComponent>();
			var before = Snapshot(profile);
			foreach (var input in payComponents ?? new List<EmployeePayComponent>())
			{
				if (!Enum.IsDefined(typeof(PayComponentCategories), input.Category) || !Enum.IsDefined(typeof(PayComponentBases), input.Basis)) throw new InvalidOperationException("workforce_component_invalid");
				var existing = string.IsNullOrWhiteSpace(input.EmployeePayComponentId) ? null : existingPay.FirstOrDefault(c => c.EmployeePayComponentId == input.EmployeePayComponentId);
				var target = existing ?? new EmployeePayComponent { DepartmentId = departmentId, EmployeeCompensationProfileId = profile.EmployeeCompensationProfileId, AddedOn = now, AddedByUserId = userId };
				target.EffectiveOn = input.EffectiveOn?.Date; target.ExpiresOn = input.ExpiresOn?.Date; target.Category = input.Category; target.Name = Trim(input.Name); target.Basis = input.Basis;
				target.EligiblePayCodesCsv = Trim(input.EligiblePayCodesCsv); target.PaidForEachOvertimeHour = input.PaidForEachOvertimeHour; target.SourceAgreement = Trim(input.SourceAgreement);
				foreach (var accessor in WorkforceProtectedFields.PayComponent) Keep(existing, target, input, accessor.Value);
				if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
				await _seam.SaveAsync(_payComponents, target, existing, departmentId, WorkforceProtectedFields.PayComponent, cancellationToken);
			}
			foreach (var stale in existingPay.Where(c => (payComponents ?? new List<EmployeePayComponent>()).All(i => i.EmployeePayComponentId != c.EmployeePayComponentId)))
			{
				stale.IsDeleted = true; stale.EditedOn = now; stale.EditedByUserId = userId;
				await _payComponents.SaveOrUpdateAsync(stale, cancellationToken);
			}
			foreach (var input in costComponents ?? new List<EmployeeCostComponent>())
			{
				if (!Enum.IsDefined(typeof(CostComponentCategories), input.Category) || !Enum.IsDefined(typeof(CostComponentBases), input.Basis)) throw new InvalidOperationException("workforce_component_invalid");
				var existing = string.IsNullOrWhiteSpace(input.EmployeeCostComponentId) ? null : existingCost.FirstOrDefault(c => c.EmployeeCostComponentId == input.EmployeeCostComponentId);
				var target = existing ?? new EmployeeCostComponent { DepartmentId = departmentId, EmployeeCompensationProfileId = profile.EmployeeCompensationProfileId, AddedOn = now, AddedByUserId = userId };
				target.EffectiveOn = input.EffectiveOn?.Date; target.ExpiresOn = input.ExpiresOn?.Date; target.Category = input.Category; target.Name = Trim(input.Name); target.Basis = input.Basis;
				target.EligiblePayCodesCsv = Trim(input.EligiblePayCodesCsv); target.Source = Trim(input.Source);
				foreach (var accessor in WorkforceProtectedFields.CostComponent) Keep(existing, target, input, accessor.Value);
				if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
				await _seam.SaveAsync(_costComponents, target, existing, departmentId, WorkforceProtectedFields.CostComponent, cancellationToken);
			}
			foreach (var stale in existingCost.Where(c => (costComponents ?? new List<EmployeeCostComponent>()).All(i => i.EmployeeCostComponentId != c.EmployeeCostComponentId)))
			{
				stale.IsDeleted = true; stale.EditedOn = now; stale.EditedByUserId = userId;
				await _costComponents.SaveOrUpdateAsync(stale, cancellationToken);
			}
			profile.IsApproved = false; profile.ApprovedByUserId = null; profile.ApprovedOn = null; profile.RowVersion++; profile.EditedOn = now; profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			audits.Add(BuildAudit(departmentId, userId, AuditLogTypes.WorkforceCompensationChanged, ipAddress, userAgent, before, profile));
			return profile;
		}

		public async Task<EmployeeCompensationProfile> ApproveProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(profileId ?? string.Empty, departmentId);
			if (profile == null || profile.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			if (profile.IsApproved) return await GetProfileAsync(profile.EmployeeCompensationProfileId, departmentId);
			var before = Snapshot(profile);
			profile.IsApproved = true; profile.ApprovedByUserId = userId; profile.ApprovedOn = DateTime.UtcNow; profile.RowVersion++; profile.EditedOn = profile.ApprovedOn; profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.WorkforceCompensationChanged, ipAddress, userAgent, before, profile);
			return await GetProfileAsync(profile.EmployeeCompensationProfileId, departmentId);
		}

		public async Task<bool> DeleteProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(profileId ?? string.Empty, departmentId);
			if (profile == null || profile.IsDeleted) return false;
			var before = Snapshot(profile);
			profile.IsDeleted = true; profile.EditedOn = DateTime.UtcNow; profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.WorkforceCompensationChanged, ipAddress, userAgent, before, profile);
			return true;
		}

		#endregion

		#region Costing

		public async Task<(EmployeeCompensationProfile Profile, bool IsFallback)> ResolveProfileAsync(string employmentId, int? personnelRoleId, int departmentId, DateTime asOf)
		{
			var candidates = new List<(EmployeeCompensationProfile Profile, bool IsFallback)>();
			if (!string.IsNullOrWhiteSpace(employmentId))
			{
				var employee = (await _profiles.GetByEmploymentAsync(employmentId))?.Where(p => p.DepartmentId == departmentId && !p.IsDeleted && p.Scope == (int)CompensationScopes.Employee && p.Covers(asOf)).OrderByDescending(p => p.EffectiveOn).FirstOrDefault();
				if (employee != null) candidates.Add((employee, false));
				if (!personnelRoleId.HasValue)
				{
					var employment = await _employments.GetByIdForDepartmentAsync(employmentId, departmentId);
					personnelRoleId = employment?.PersonnelRoleId;
				}
			}
			if (candidates.Count == 0)
			{
				var defaults = (await _profiles.GetDefaultsForDepartmentAsync(departmentId))?.Where(p => !p.IsDeleted && p.Covers(asOf)).ToList() ?? new List<EmployeeCompensationProfile>();
				var role = personnelRoleId.HasValue ? defaults.Where(p => p.Scope == (int)CompensationScopes.RoleDefault && p.PersonnelRoleId == personnelRoleId).OrderByDescending(p => p.EffectiveOn).FirstOrDefault() : null;
				if (role != null) candidates.Add((role, true));
				else
				{
					var department = defaults.Where(p => p.Scope == (int)CompensationScopes.DepartmentDefault).OrderByDescending(p => p.EffectiveOn).FirstOrDefault();
					if (department != null) candidates.Add((department, true));
				}
			}
			if (candidates.Count == 0) return (null, false);
			var chosen = candidates[0];
			var resolved = await LoadComponentsAsync(new[] { chosen.Profile }, departmentId, WorkforceProtectedFields.CostingWorkloadPurpose);
			if (!resolved || WorkforceProtectionSeam.IsUnavailable(chosen.Profile.BaseAmount) || WorkforceProtectionSeam.IsUnavailable(chosen.Profile.RegularHourlyEquivalent))
			{
				// The workload could not decrypt: the engine must not guess. Blank the rates so the calculator flags NoRate.
				chosen.Profile.BaseAmount = null; chosen.Profile.RegularHourlyEquivalent = null; chosen.Profile.RateMultipliersJson = null;
				foreach (var c in chosen.Profile.PayComponents) c.Amount = null;
				foreach (var c in chosen.Profile.CostComponents) { c.RateAmount = null; c.Cap = null; }
			}
			return chosen;
		}

		public async Task<LaborCostResult> CalculateLoadedCostAsync(int departmentId, LaborWorkQuantity work, int? personnelRoleId, DateTime asOf, string currency = "USD")
		{
			if (work == null) throw new ArgumentNullException(nameof(work));
			var (profile, isFallback) = await ResolveProfileAsync(work.WorkforceEmploymentId, personnelRoleId, departmentId, asOf);
			return FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = work, Profile = profile, IsFallback = isFallback, AsOf = asOf, Currency = currency });
		}

		public async Task<List<(string ClassificationCode, int Count, decimal MeanRate, decimal MeanOvertimeAdder)>> GetClassificationRateAggregateAsync(int departmentId, DateTime asOf, string calOesAuthorityProfileCode)
		{
			var result = new List<(string, int, decimal, decimal)>();
			var employments = (await _employments.GetActiveInWindowAsync(departmentId, asOf, asOf))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEmployment>();
			if (employments.Count == 0) return result;
			var assignments = (await _assignments.GetByEmploymentsAsync(employments.Select(e => e.WorkforceEmploymentId)))?.Where(a => !a.IsDeleted && a.Covers(asOf) && !string.IsNullOrWhiteSpace(a.CalOesMarsClassificationCode) && (string.IsNullOrWhiteSpace(calOesAuthorityProfileCode) || string.Equals(a.CalOesMarsAuthorityProfileCode, calOesAuthorityProfileCode, StringComparison.OrdinalIgnoreCase))).ToList() ?? new List<WorkforceJobAssignment>();
			if (assignments.Count == 0) return result;
			var profiles = (await _profiles.GetByEmploymentsAsync(assignments.Select(a => a.WorkforceEmploymentId).Distinct()))?.Where(p => !p.IsDeleted && p.IsApproved && p.Scope == (int)CompensationScopes.Employee && p.Covers(asOf)).ToList() ?? new List<EmployeeCompensationProfile>();
			var current = profiles.GroupBy(p => p.WorkforceEmploymentId).Select(g => g.OrderByDescending(p => p.EffectiveOn).First()).ToList();
			if (current.Count == 0) return result;
			var resolved = await LoadComponentsAsync(current, departmentId, WorkforceProtectedFields.CostingWorkloadPurpose);
			if (!resolved) return result;
			var samples = new List<(string Code, decimal Rate, decimal OvertimeAdder)>();
			foreach (var profile in current)
			{
				var rate = FieldCostCalculator.HourlyRate(profile);
				if (!rate.HasValue) continue;
				var classification = assignments.First(a => a.WorkforceEmploymentId == profile.WorkforceEmploymentId).CalOesMarsClassificationCode;
				var adder = profile.PayComponents.Where(c => c.PaidForEachOvertimeHour && c.Basis == (int)PayComponentBases.PerHour && c.Covers(asOf) && c.AmountValue.HasValue).Sum(c => c.AmountValue.Value);
				samples.Add((classification, rate.Value, adder));
			}
			foreach (var group in samples.GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
				result.Add((group.Key, group.Count(), FieldCostCalculator.Round(group.Average(s => s.Rate)), FieldCostCalculator.Round(group.Average(s => s.OvertimeAdder))));
			return result;
		}

		#endregion

		#region Helpers

		private static void Keep<T>(T existing, T target, T input, (Func<T, string> Get, Action<T, string> Set) accessor)
		{
			var value = accessor.Get(input);
			if (existing != null && value == ProtectedDataEnvelope.RedactionValue) { accessor.Set(target, accessor.Get(existing)); return; }
			accessor.Set(target, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		internal static string Snapshot(EmployeeCompensationProfile profile)
		{
			var clone = profile.CloneJson();
			clone.PayComponents = null; clone.CostComponents = null;
			foreach (var a in WorkforceProtectedFields.Compensation) a.Value.Set(clone, WorkforceService.Marker(a.Value.Get(clone)));
			return clone.CloneJsonToString();
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, EmployeeCompensationProfile after)
			=> _eventAggregator.SendMessage<AuditEvent>(BuildAudit(departmentId, userId, type, ipAddress, userAgent, before, after));

		private static AuditEvent BuildAudit(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, EmployeeCompensationProfile after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			return audit;
		}

		private void Publish(IEnumerable<AuditEvent> audits)
		{
			foreach (var audit in audits) _eventAggregator.SendMessage<AuditEvent>(audit);
		}

		/// <summary>TimeTrackingService pattern: joins an ambient transaction when one is open, otherwise owns commit / discard.</summary>
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
