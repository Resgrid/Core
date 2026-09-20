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
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Invoicing;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// The separately stored demographic responses for California pay data reporting (plan E1/E3): a worker's own
	/// self-identification, the compliance officer's completeness view (counts only), and the officer's record for a
	/// worker (employment record / reliable record / observer perception — always with a reason, always reviewed).
	/// Values ride ADP catalog 28; the costing engine never reads this table and no other screen renders it.
	/// </summary>
	public class PayDataDemographicsService : IPayDataDemographicsService
	{
		private readonly IPayDataReportingDemographicRepository _demographics;
		private readonly IWorkforceWorkerRepository _workers;
		private readonly IWorkforceEmploymentRepository _employments;
		private readonly IWorkforceService _workforceService;
		private readonly IEventAggregator _eventAggregator;
		private readonly WorkforceProtectionSeam _seam;

		public PayDataDemographicsService(IPayDataReportingDemographicRepository demographics, IWorkforceWorkerRepository workers, IWorkforceEmploymentRepository employments, IWorkforceService workforceService, IEventAggregator eventAggregator,
			Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_demographics = demographics;
			_workers = workers;
			_employments = employments;
			_workforceService = workforceService;
			_eventAggregator = eventAggregator;
			_seam = new WorkforceProtectionSeam(protectedWrite, protectedRead, grant);
		}

		public async Task<PayDataReportingDemographic> GetOwnAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId)) return null;
			var worker = await _workers.GetByUserIdAsync(departmentId, userId);
			if (worker == null || worker.IsDeleted) return null;
			var row = await _demographics.GetCurrentForWorkerAsync(worker.WorkforceWorkerId, DateTime.UtcNow.Date);
			if (row == null || row.IsDeleted) return null;
			// The subject reads their own answers: resolved through the reporting workload purpose, never through a grant they would not hold.
			await _seam.ResolveForWorkloadAsync(new[] { row }, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Demographic);
			return row;
		}

		public async Task<PayDataReportingDemographic> SaveOwnAsync(int departmentId, string userId, PayDataReportingDemographic response, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (response == null) throw new ArgumentNullException(nameof(response));
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));
			var worker = await _workforceService.GetOrCreateWorkerForUserAsync(departmentId, userId, userId, cancellationToken);
			response.CollectionSource = (int)DemographicCollectionSources.SelfIdentified;
			response.CollectedByUserId = userId;
			return await SaveVersionAsync(departmentId, worker.WorkforceWorkerId, response, userId, userId, ipAddress, userAgent, cancellationToken);
		}

		public async Task<PayDataReportingDemographic> SaveForWorkerAsync(int departmentId, string workerId, PayDataReportingDemographic response, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (response == null) throw new ArgumentNullException(nameof(response));
			if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("paydata_reason_required");
			if (response.CollectionSource == (int)DemographicCollectionSources.SelfIdentified || !Enum.IsDefined(typeof(DemographicCollectionSources), response.CollectionSource)) throw new InvalidOperationException("paydata_collection_source_invalid");
			var worker = await _workers.GetByIdForDepartmentAsync(workerId ?? string.Empty, departmentId);
			if (worker == null || worker.IsDeleted) throw new InvalidOperationException("workforce_worker_not_found");
			response.CollectedByUserId = userId;
			response.ReviewedByUserId = userId;
			response.ReviewedOn = DateTime.UtcNow;
			return await SaveVersionAsync(departmentId, worker.WorkforceWorkerId, response, userId, reason, ipAddress, userAgent, cancellationToken);
		}

		private async Task<PayDataReportingDemographic> SaveVersionAsync(int departmentId, string workerId, PayDataReportingDemographic response, string userId, string reason, string ipAddress, string userAgent, CancellationToken cancellationToken)
		{
			var profile = CaPayDataSchemaProfile.Current;
			var hispanic = Normalize(response.HispanicLatino);
			if (hispanic != null && hispanic != "Yes" && hispanic != "No") throw new InvalidOperationException("paydata_hispanic_invalid");
			var races = (response.RaceEthnicityCodes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
			if (races.Any(c => profile.RaceEthnicities.All(r => r.Code != c) || c == "A" || c == "G")) throw new InvalidOperationException("paydata_race_invalid");
			var sex = Normalize(response.SexCode);
			if (sex != null && profile.Sexes.All(s => s.Code != sex)) throw new InvalidOperationException("paydata_sex_invalid");
			if (!response.DeclinedRaceEthnicity && hispanic == null && races.Count == 0) throw new InvalidOperationException("paydata_race_required");
			if (!response.DeclinedSex && sex == null) throw new InvalidOperationException("paydata_sex_required");
			var today = DateTime.UtcNow.Date;
			var current = await _demographics.GetCurrentForWorkerAsync(workerId, today);
			if (current != null && current.IsDeleted) current = null;
			var before = current == null ? null : Snapshot(current);
			if (current != null)
			{
				// Versions are kept: the current row closes the day before the new one starts; a same-day re-answer replaces it.
				if (current.EffectiveOn.Date < today) current.ExpiresOn = today.AddDays(-1);
				else current.IsDeleted = true;
				current.EditedOn = DateTime.UtcNow; current.EditedByUserId = userId;
				await _demographics.SaveOrUpdateAsync(current, cancellationToken);
			}
			var target = new PayDataReportingDemographic
			{
				DepartmentId = departmentId, WorkforceWorkerId = workerId, EffectiveOn = today, ExpiresOn = null,
				HispanicLatino = response.DeclinedRaceEthnicity ? null : hispanic, RaceEthnicityCodes = response.DeclinedRaceEthnicity ? null : (races.Count == 0 ? null : string.Join(",", races)), SexCode = response.DeclinedSex ? null : sex,
				DeclinedRaceEthnicity = response.DeclinedRaceEthnicity, DeclinedSex = response.DeclinedSex, CollectionSource = response.CollectionSource, CollectedOn = DateTime.UtcNow, CollectedByUserId = response.CollectedByUserId ?? userId,
				ReviewedOn = response.ReviewedOn, ReviewedByUserId = response.ReviewedByUserId, Version = (current?.Version ?? 0) + 1, AddedOn = DateTime.UtcNow, AddedByUserId = userId
			};
			var saved = await _seam.SaveAsync(_demographics, target, null, departmentId, WorkforceProtectedFields.Demographic, cancellationToken);
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.PayDataDemographicChanged, ipAddress, userAgent);
			audit.Before = before; audit.After = Snapshot(saved);
			if (!string.IsNullOrWhiteSpace(reason) && reason != userId) audit.After = audit.After.TrimEnd('}') + ",\"Reason\":" + Newtonsoft.Json.JsonConvert.ToString(reason) + "}";
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return saved;
		}

		public async Task<DemographicCompleteness> GetCompletenessAsync(int departmentId, DateTime asOf)
		{
			var employments = (await _employments.GetActiveInWindowAsync(departmentId, asOf.Date, asOf.Date))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEmployment>();
			var activeWorkers = employments.Select(e => e.WorkforceWorkerId).Distinct().ToList();
			var responses = (await _demographics.GetCurrentForDepartmentAsync(departmentId, asOf.Date))?.Where(d => !d.IsDeleted && activeWorkers.Contains(d.WorkforceWorkerId)).GroupBy(d => d.WorkforceWorkerId).Select(g => g.OrderByDescending(d => d.Version).First()).ToList() ?? new List<PayDataReportingDemographic>();
			// Counts only — flags and sources, never the coded values (which stay enveloped).
			return new DemographicCompleteness
			{
				ActiveWorkers = activeWorkers.Count,
				WithResponse = responses.Count,
				SelfIdentified = responses.Count(r => r.CollectionSource == (int)DemographicCollectionSources.SelfIdentified && !r.DeclinedRaceEthnicity && !r.DeclinedSex),
				Declined = responses.Count(r => r.DeclinedRaceEthnicity || r.DeclinedSex),
				ObserverPerception = responses.Count(r => r.CollectionSource == (int)DemographicCollectionSources.ObserverPerception)
			};
		}

		public async Task<PayDataReportingDemographic> GetForWorkerAsync(int departmentId, string workerId, DateTime asOf)
		{
			var worker = await _workers.GetByIdForDepartmentAsync(workerId ?? string.Empty, departmentId);
			if (worker == null || worker.IsDeleted) return null;
			var row = await _demographics.GetCurrentForWorkerAsync(worker.WorkforceWorkerId, asOf.Date);
			if (row == null || row.IsDeleted) return null;
			await _seam.ResolveForReadAsync(new[] { row }, departmentId, WorkforceProtectedFields.Demographic);
			return row;
		}

		private static string Normalize(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return null;
			var trimmed = value.Trim();
			if (string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase)) return "Yes";
			if (string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase)) return "No";
			return trimmed;
		}

		internal static string Snapshot(PayDataReportingDemographic row)
		{
			var clone = row.CloneJson();
			foreach (var a in WorkforceProtectedFields.Demographic) a.Value.Set(clone, WorkforceService.Marker(a.Value.Get(clone)));
			return clone.CloneJsonToString();
		}
	}
}
