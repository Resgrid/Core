using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>In-memory IRepository&lt;T&gt;: the entity instance handed in is the one stored, matching the fixture rule the RMS suites rely on.</summary>
	public class InMemoryRepo<T> : IRepository<T> where T : class, IEntity
	{
		public List<T> Rows { get; } = new List<T>();
		public int Inserts { get; private set; }
		public int Updates { get; private set; }

		protected static int DeptOf(T e) => (int)e.GetType().GetProperty("DepartmentId").GetValue(e);
		protected static DateTime? DeletedOf(T e) => e.GetType().GetProperty("DeletedOn")?.GetValue(e) as DateTime?;
		protected IEnumerable<T> Live(int departmentId) => Rows.Where(r => DeptOf(r) == departmentId && DeletedOf(r) == null);
		protected static string S(T e, string property) => e.GetType().GetProperty(property)?.GetValue(e) as string;
		protected static int I(T e, string property) => (int)(e.GetType().GetProperty(property)?.GetValue(e) ?? 0);

		public Task<IEnumerable<T>> GetAllAsync() => Task.FromResult<IEnumerable<T>>(Rows.ToList());
		public Task<T> GetByIdAsync(object id) => Task.FromResult(Rows.FirstOrDefault(r => Equals(r.IdValue?.ToString(), id?.ToString())));
		public Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult<IEnumerable<T>>(Rows.Where(r => DeptOf(r) == departmentId).ToList());
		public Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false) { Rows.Add(entity); Inserts++; return Task.FromResult(entity); }
		public Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false) { if (!Rows.Contains(entity)) { Rows.RemoveAll(r => Equals(r.IdValue, entity.IdValue)); Rows.Add(entity); } Updates++; return Task.FromResult(entity); }
		public Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken) => Task.FromResult(Rows.Remove(entity) || Rows.RemoveAll(r => Equals(r.IdValue, entity.IdValue)) > 0);
		public Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => Rows.Contains(entity) ? UpdateAsync(entity, cancellationToken, firstLevelOnly) : InsertAsync(entity, cancellationToken, firstLevelOnly);
		public Task<IEnumerable<T>> GetAllByUserIdAsync(string userId) => Task.FromResult<IEnumerable<T>>(new List<T>());
		public Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => Task.FromResult(true);
	}

	public class FakeSequences : InMemoryRepo<RmsPreventionSequence>, IRmsPreventionSequencesRepository
	{
		public Task<int> NextAsync(int departmentId, string kind, int year, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.Kind == kind && r.Year == year);
			if (row == null) { row = new RmsPreventionSequence { RmsPreventionSequenceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, Kind = kind, Year = year }; Rows.Add(row); }
			row.LastValue++;
			return Task.FromResult(row.LastValue);
		}
	}

	public class FakeAudits : InMemoryRepo<RmsAccessAudit>, IRmsAccessAuditsRepository
	{
		public Task<IEnumerable<RmsAccessAudit>> GetForRecordAsync(int departmentId, string recordId, int take) => Task.FromResult<IEnumerable<RmsAccessAudit>>(Rows.Where(a => a.DepartmentId == departmentId && a.RecordId == recordId).Take(take).ToList());
		public Task<int> CountByActionSinceAsync(int departmentId, int action, DateTime sinceUtc) => Task.FromResult(Rows.Count(a => a.DepartmentId == departmentId && a.Action == action && a.OccurredOn >= sinceUtc));
		public Task<IEnumerable<RmsAccessAudit>> GetForAggregateAsync(int departmentId, string aggregateId, int take) => Task.FromResult<IEnumerable<RmsAccessAudit>>(Rows.Where(a => a.DepartmentId == departmentId && a.CorrelationId == aggregateId).OrderByDescending(a => a.OccurredOn).Take(take).ToList());
	}

	public class FakeOccupancies : InMemoryRepo<RmsOccupancy>, IRmsOccupanciesRepository
	{
		public Task<RmsOccupancy> GetByIdForDepartmentAsync(int departmentId, string occupancyId) => Task.FromResult(Rows.FirstOrDefault(o => o.DepartmentId == departmentId && o.RmsOccupancyId == occupancyId));
		public Task<IEnumerable<RmsOccupancy>> GetByIdsAsync(int departmentId, IEnumerable<string> occupancyIds) { var ids = occupancyIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsOccupancy>>(Rows.Where(o => o.DepartmentId == departmentId && ids.Contains(o.RmsOccupancyId)).ToList()); }
		public Task<IEnumerable<RmsOccupancy>> QueryAsync(int departmentId, RmsOccupancyQuery query) => Task.FromResult<IEnumerable<RmsOccupancy>>(Live(departmentId).Where(o => o.Status != (int)RmsOccupancyStatus.Merged && (query.Status == null || o.Status == query.Status) && (string.IsNullOrEmpty(query.Search) || (o.Name ?? "").Contains(query.Search, StringComparison.OrdinalIgnoreCase))).OrderBy(o => o.Name).Skip(query.Skip).Take(query.Take).ToList());
		public Task<int> CountAsync(int departmentId, RmsOccupancyQuery query) => Task.FromResult(Live(departmentId).Count(o => o.Status != (int)RmsOccupancyStatus.Merged));
		public Task<IEnumerable<RmsOccupancy>> GetByNormalizedAddressAsync(int departmentId, string normalizedAddress) => Task.FromResult<IEnumerable<RmsOccupancy>>(Live(departmentId).Where(o => o.NormalizedAddress == normalizedAddress).ToList());
		public Task<IEnumerable<RmsOccupancy>> GetAllLiveAsync(int departmentId) => Task.FromResult<IEnumerable<RmsOccupancy>>(Live(departmentId).Where(o => o.Status != (int)RmsOccupancyStatus.Merged).ToList());
		public Task<int> CountLiveAsync(int departmentId) => Task.FromResult(Live(departmentId).Count(o => o.Status != (int)RmsOccupancyStatus.Merged));
		public Task<int> CountReviewOverdueAsync(int departmentId, DateTime utcNow) => Task.FromResult(Live(departmentId).Count(o => o.Status == (int)RmsOccupancyStatus.Active && o.NextReviewDue < utcNow));
		public Task<bool> TryBumpRowVersionAsync(int departmentId, string occupancyId, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
	}

	public class FakeContactLinks : InMemoryRepo<RmsOccupancyContactLink>, IRmsOccupancyContactLinksRepository
	{
		public Task<IEnumerable<RmsOccupancyContactLink>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsOccupancyContactLink>>(Live(departmentId).Where(l => l.RmsOccupancyId == occupancyId).ToList());
		public Task<IEnumerable<RmsOccupancyContactLink>> GetForContactAsync(int departmentId, string contactId) => Task.FromResult<IEnumerable<RmsOccupancyContactLink>>(Live(departmentId).Where(l => l.ContactId == contactId).ToList());
		public Task<IEnumerable<RmsOccupancyContactLink>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds) { var ids = contactIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsOccupancyContactLink>>(Live(departmentId).Where(l => ids.Contains(l.ContactId)).ToList()); }
		public Task<RmsOccupancyContactLink> GetByIdForDepartmentAsync(int departmentId, string linkId) => Task.FromResult(Rows.FirstOrDefault(l => l.DepartmentId == departmentId && l.RmsOccupancyContactLinkId == linkId));
	}

	public class FakeOccupancyHazards : InMemoryRepo<RmsOccupancyHazard>, IRmsOccupancyHazardsRepository
	{
		public Task<IEnumerable<RmsOccupancyHazard>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsOccupancyHazard>>(Live(departmentId).Where(h => h.RmsOccupancyId == occupancyId).ToList());
		public Task<IEnumerable<RmsOccupancyHazard>> GetForOccupanciesAsync(int departmentId, IEnumerable<string> occupancyIds) { var ids = occupancyIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsOccupancyHazard>>(Live(departmentId).Where(h => ids.Contains(h.RmsOccupancyId)).ToList()); }
		public Task<RmsOccupancyHazard> GetByIdForDepartmentAsync(int departmentId, string hazardId) => Task.FromResult(Rows.FirstOrDefault(h => h.DepartmentId == departmentId && h.RmsOccupancyHazardId == hazardId));
	}

	public class FakeCrosswalks : InMemoryRepo<RmsOccupancyCrosswalk>, IRmsOccupancyCrosswalksRepository
	{
		public Task<RmsOccupancyCrosswalk> GetByIdForDepartmentAsync(int departmentId, string crosswalkId) => Task.FromResult(Rows.FirstOrDefault(c => c.DepartmentId == departmentId && c.RmsOccupancyCrosswalkId == crosswalkId));
		public Task<RmsOccupancyCrosswalk> GetBySourceAsync(int departmentId, RmsOccupancyCrosswalkSourceKind sourceKind, string sourceId) => Task.FromResult(Rows.FirstOrDefault(c => c.DepartmentId == departmentId && c.SourceKind == (int)sourceKind && c.SourceId == sourceId));
		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetByStateAsync(int departmentId, RmsOccupancyCrosswalkState state, int skip, int take) => Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(Rows.Where(c => c.DepartmentId == departmentId && c.State == (int)state).Skip(skip).Take(take).ToList());
		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(Rows.Where(c => c.DepartmentId == departmentId && c.RmsOccupancyId == occupancyId).ToList());
		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactAsync(int departmentId, string contactId) => Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(Rows.Where(c => c.DepartmentId == departmentId && c.ContactId == contactId).ToList());
		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds) { var ids = contactIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(Rows.Where(c => c.DepartmentId == departmentId && c.ContactId != null && ids.Contains(c.ContactId)).ToList()); }
		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetAllForDepartmentAsync(int departmentId) => Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(Rows.Where(c => c.DepartmentId == departmentId).ToList());
		public Task<int> CountByStateAsync(int departmentId, RmsOccupancyCrosswalkState state) => Task.FromResult(Rows.Count(c => c.DepartmentId == departmentId && c.State == (int)state));
	}

	public class FakeProvenance : InMemoryRepo<RmsOccupancyFieldProvenance>, IRmsOccupancyFieldProvenancesRepository
	{
		public Task<IEnumerable<RmsOccupancyFieldProvenance>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsOccupancyFieldProvenance>>(Rows.Where(p => p.DepartmentId == departmentId && p.RmsOccupancyId == occupancyId).ToList());
		public Task<int> DeleteForOccupancyAsync(int departmentId, string occupancyId, CancellationToken cancellationToken = default) => Task.FromResult(Rows.RemoveAll(p => p.DepartmentId == departmentId && p.RmsOccupancyId == occupancyId));
	}

	public class FakeOwnerships : InMemoryRepo<RmsOccupancyOwnership>, IRmsOccupancyOwnershipsRepository
	{
		public Task<RmsOccupancyOwnership> GetForDepartmentAsync(int departmentId) => Task.FromResult(Rows.FirstOrDefault(o => o.DepartmentId == departmentId));
	}

	public class FakeCodeSets : InMemoryRepo<RmsCodeSet>, IRmsCodeSetsRepository
	{
		public Task<RmsCodeSet> GetByIdForDepartmentAsync(int departmentId, string codeSetId) => Task.FromResult(Rows.FirstOrDefault(c => c.DepartmentId == departmentId && c.RmsCodeSetId == codeSetId));
		public Task<IEnumerable<RmsCodeSet>> GetForDepartmentAsync(int departmentId, bool includeInactive) => Task.FromResult<IEnumerable<RmsCodeSet>>(Live(departmentId).Where(c => includeInactive || c.IsActive).ToList());
	}

	public class FakeCodeSections : InMemoryRepo<RmsCodeSection>, IRmsCodeSectionsRepository
	{
		public Task<RmsCodeSection> GetByIdForDepartmentAsync(int departmentId, string sectionId) => Task.FromResult(Rows.FirstOrDefault(c => c.DepartmentId == departmentId && c.RmsCodeSectionId == sectionId));
		public Task<IEnumerable<RmsCodeSection>> GetForCodeSetAsync(int departmentId, string codeSetId) => Task.FromResult<IEnumerable<RmsCodeSection>>(Live(departmentId).Where(c => c.RmsCodeSetId == codeSetId).ToList());
		public Task<IEnumerable<RmsCodeSection>> GetByIdsAsync(int departmentId, IEnumerable<string> sectionIds) { var ids = sectionIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsCodeSection>>(Rows.Where(c => c.DepartmentId == departmentId && ids.Contains(c.RmsCodeSectionId)).ToList()); }
	}

	public class FakePrograms : InMemoryRepo<RmsInspectionProgram>, IRmsInspectionProgramsRepository
	{
		public Task<RmsInspectionProgram> GetByIdForDepartmentAsync(int departmentId, string programId) => Task.FromResult(Rows.FirstOrDefault(p => p.DepartmentId == departmentId && p.RmsInspectionProgramId == programId));
		public Task<IEnumerable<RmsInspectionProgram>> GetForDepartmentAsync(int departmentId, bool includeInactive) => Task.FromResult<IEnumerable<RmsInspectionProgram>>(Live(departmentId).Where(p => includeInactive || p.IsActive).ToList());
	}

	public class FakeInspections : InMemoryRepo<RmsInspection>, IRmsInspectionsRepository
	{
		public Task<RmsInspection> GetByIdForDepartmentAsync(int departmentId, string inspectionId) => Task.FromResult(Rows.FirstOrDefault(i => i.DepartmentId == departmentId && i.RmsInspectionId == inspectionId));
		private IEnumerable<RmsInspection> Filter(int departmentId, RmsInspectionQuery q) => Live(departmentId).Where(i => (q.OccupancyId == null || i.RmsOccupancyId == q.OccupancyId) && (q.ProgramId == null || i.RmsInspectionProgramId == q.ProgramId) && (q.States == null || q.States.Count == 0 || q.States.Contains(i.State)) && (q.ScheduledBefore == null || i.ScheduledOn <= q.ScheduledBefore));
		public Task<IEnumerable<RmsInspection>> QueryAsync(int departmentId, RmsInspectionQuery query) => Task.FromResult<IEnumerable<RmsInspection>>(Filter(departmentId, query).Skip(query.Skip).Take(query.Take).ToList());
		public Task<int> CountAsync(int departmentId, RmsInspectionQuery query) => Task.FromResult(Filter(departmentId, query).Count());
		public Task<IEnumerable<RmsInspection>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsInspection>>(Live(departmentId).Where(i => i.RmsOccupancyId == occupancyId).ToList());
		public Task<IDictionary<string, DateTime>> GetLastCompletedByOccupancyAsync(int departmentId, string programId) => Task.FromResult<IDictionary<string, DateTime>>(Live(departmentId).Where(i => i.RmsInspectionProgramId == programId && i.CompletedOn.HasValue).GroupBy(i => i.RmsOccupancyId).ToDictionary(g => g.Key, g => g.Max(i => i.CompletedOn.Value)));
		public Task<IEnumerable<RmsInspection>> GetOpenForProgramAsync(int departmentId, string programId) => Task.FromResult<IEnumerable<RmsInspection>>(Live(departmentId).Where(i => i.RmsInspectionProgramId == programId && (i.State == (int)RmsInspectionState.Scheduled || i.State == (int)RmsInspectionState.InProgress || i.State == (int)RmsInspectionState.ReinspectionRequired)).ToList());
		public Task<IEnumerable<RmsInspection>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take) => Task.FromResult<IEnumerable<RmsInspection>>(Live(departmentId).Where(i => { var d = i.CompletedOn ?? i.ScheduledOn ?? i.CreatedOn; return d >= startUtc && d < endUtc; }).Take(take).ToList());
		public Task<bool> TryBumpRowVersionAsync(int departmentId, string inspectionId, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
	}

	public class FakeViolations : InMemoryRepo<RmsViolation>, IRmsViolationsRepository
	{
		public Task<RmsViolation> GetByIdForDepartmentAsync(int departmentId, string violationId) => Task.FromResult(Rows.FirstOrDefault(v => v.DepartmentId == departmentId && v.RmsViolationId == violationId));
		public Task<IEnumerable<RmsViolation>> GetForInspectionAsync(int departmentId, string inspectionId) => Task.FromResult<IEnumerable<RmsViolation>>(Live(departmentId).Where(v => v.RmsInspectionId == inspectionId).ToList());
		public Task<IEnumerable<RmsViolation>> GetForOccupancyAsync(int departmentId, string occupancyId, bool openOnly) => Task.FromResult<IEnumerable<RmsViolation>>(Live(departmentId).Where(v => v.RmsOccupancyId == occupancyId && (!openOnly || v.IsOpen)).ToList());
		public Task<IEnumerable<RmsViolation>> GetOpenAsync(int departmentId, int take) => Task.FromResult<IEnumerable<RmsViolation>>(Live(departmentId).Where(v => v.IsOpen).Take(take).ToList());
		public Task<IEnumerable<RmsViolation>> GetOverdueNotEmittedAsync(int departmentId, DateTime utcNow, int take) => Task.FromResult<IEnumerable<RmsViolation>>(Live(departmentId).Where(v => v.IsOpen && v.DueOn < utcNow && v.OverdueEmittedOn == null).Take(take).ToList());
		public Task<int> CountOpenAsync(int departmentId) => Task.FromResult(Live(departmentId).Count(v => v.IsOpen));
		public Task<int> CountOverdueAsync(int departmentId, DateTime utcNow) => Task.FromResult(Live(departmentId).Count(v => v.IsOpen && v.DueOn < utcNow));
		public Task<IDictionary<string, int>> CountOpenByOccupancyAsync(int departmentId, IEnumerable<string> occupancyIds) { var ids = occupancyIds.ToHashSet(); return Task.FromResult<IDictionary<string, int>>(Live(departmentId).Where(v => v.IsOpen && ids.Contains(v.RmsOccupancyId)).GroupBy(v => v.RmsOccupancyId).ToDictionary(g => g.Key, g => g.Count())); }
		public Task<IEnumerable<RmsViolation>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take) => Task.FromResult<IEnumerable<RmsViolation>>(Live(departmentId).Where(v => v.CreatedOn >= startUtc && v.CreatedOn < endUtc).Take(take).ToList());
	}

	public class FakeHydrants : InMemoryRepo<RmsHydrant>, IRmsHydrantsRepository
	{
		public Task<RmsHydrant> GetByIdForDepartmentAsync(int departmentId, string hydrantId) => Task.FromResult(Rows.FirstOrDefault(h => h.DepartmentId == departmentId && h.RmsHydrantId == hydrantId));
		public Task<RmsHydrant> GetByNumberAsync(int departmentId, string hydrantNumber) => Task.FromResult(Live(departmentId).FirstOrDefault(h => string.Equals(h.HydrantNumber, hydrantNumber, StringComparison.OrdinalIgnoreCase)));
		public Task<IEnumerable<RmsHydrant>> GetAllLiveAsync(int departmentId) => Task.FromResult<IEnumerable<RmsHydrant>>(Live(departmentId).ToList());
		public Task<IEnumerable<RmsHydrant>> GetByIdsAsync(int departmentId, IEnumerable<string> hydrantIds) { var ids = hydrantIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsHydrant>>(Rows.Where(h => h.DepartmentId == departmentId && ids.Contains(h.RmsHydrantId)).ToList()); }
		public Task<IEnumerable<RmsHydrant>> GetInBoundsAsync(int departmentId, decimal minLat, decimal maxLat, decimal minLon, decimal maxLon, int take) => Task.FromResult<IEnumerable<RmsHydrant>>(Live(departmentId).Where(h => h.Latitude >= minLat && h.Latitude <= maxLat && h.Longitude >= minLon && h.Longitude <= maxLon).Take(take).ToList());
		public Task<int> CountLiveAsync(int departmentId) => Task.FromResult(Live(departmentId).Count());
		public Task<int> CountOutOfServiceAsync(int departmentId) => Task.FromResult(Live(departmentId).Count(h => !h.InService));
		public Task<int> CountTestDueAsync(int departmentId, DateTime cutoffUtc) => Task.FromResult(Live(departmentId).Count(h => h.InService && (h.LastTestedOn == null || h.LastTestedOn < cutoffUtc)));
	}

	public class FakeFlowTests : InMemoryRepo<RmsHydrantFlowTest>, IRmsHydrantFlowTestsRepository
	{
		public Task<IEnumerable<RmsHydrantFlowTest>> GetForHydrantAsync(int departmentId, string hydrantId) => Task.FromResult<IEnumerable<RmsHydrantFlowTest>>(Rows.Where(t => t.DepartmentId == departmentId && t.RmsHydrantId == hydrantId).ToList());
		public Task<IEnumerable<RmsHydrantFlowTest>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take) => Task.FromResult<IEnumerable<RmsHydrantFlowTest>>(Rows.Where(t => t.DepartmentId == departmentId && t.TestedOn >= startUtc && t.TestedOn < endUtc).Take(take).ToList());
	}

	public class FakeMaintenance : InMemoryRepo<RmsHydrantMaintenance>, IRmsHydrantMaintenancesRepository
	{
		public Task<IEnumerable<RmsHydrantMaintenance>> GetForHydrantAsync(int departmentId, string hydrantId) => Task.FromResult<IEnumerable<RmsHydrantMaintenance>>(Rows.Where(t => t.DepartmentId == departmentId && t.RmsHydrantId == hydrantId).ToList());
	}

	public class FakePermitTypes : InMemoryRepo<RmsPermitType>, IRmsPermitTypesRepository
	{
		public Task<RmsPermitType> GetByIdForDepartmentAsync(int departmentId, string permitTypeId) => Task.FromResult(Rows.FirstOrDefault(t => t.DepartmentId == departmentId && t.RmsPermitTypeId == permitTypeId));
		public Task<IEnumerable<RmsPermitType>> GetForDepartmentAsync(int departmentId, bool includeInactive) => Task.FromResult<IEnumerable<RmsPermitType>>(Live(departmentId).Where(t => includeInactive || t.IsActive).ToList());
	}

	public class FakePermits : InMemoryRepo<RmsPermit>, IRmsPermitsRepository
	{
		public Task<RmsPermit> GetByIdForDepartmentAsync(int departmentId, string permitId) => Task.FromResult(Rows.FirstOrDefault(p => p.DepartmentId == departmentId && p.RmsPermitId == permitId));
		private IEnumerable<RmsPermit> Filter(int departmentId, RmsPermitQuery q) => Live(departmentId).Where(p => (q.OccupancyId == null || p.RmsOccupancyId == q.OccupancyId) && (q.PermitTypeId == null || p.RmsPermitTypeId == q.PermitTypeId) && (q.States == null || q.States.Count == 0 || q.States.Contains(p.State)) && (q.ExpiresBefore == null || p.ExpiresOn <= q.ExpiresBefore));
		public Task<IEnumerable<RmsPermit>> QueryAsync(int departmentId, RmsPermitQuery query) => Task.FromResult<IEnumerable<RmsPermit>>(Filter(departmentId, query).Skip(query.Skip).Take(query.Take).ToList());
		public Task<int> CountAsync(int departmentId, RmsPermitQuery query) => Task.FromResult(Filter(departmentId, query).Count());
		public Task<IEnumerable<RmsPermit>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsPermit>>(Live(departmentId).Where(p => p.RmsOccupancyId == occupancyId).ToList());
		public Task<IEnumerable<RmsPermit>> GetExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc, int take) => Task.FromResult<IEnumerable<RmsPermit>>(Live(departmentId).Where(p => p.State == (int)RmsPermitState.Issued && p.ExpiresOn != null && p.ExpiresOn <= horizonUtc).Take(take).ToList());
		public Task<int> CountExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc) => Task.FromResult(Live(departmentId).Count(p => p.State == (int)RmsPermitState.Issued && p.ExpiresOn > utcNow && p.ExpiresOn <= horizonUtc));
		public Task<IEnumerable<RmsPermit>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take) => Task.FromResult<IEnumerable<RmsPermit>>(Live(departmentId).Where(p => p.AppliedOn >= startUtc && p.AppliedOn < endUtc).Take(take).ToList());
		public Task<bool> TryBumpRowVersionAsync(int departmentId, string permitId, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
	}

	public class FakePlanReviews : InMemoryRepo<RmsPlanReview>, IRmsPlanReviewsRepository
	{
		public Task<IEnumerable<RmsPlanReview>> GetForPermitAsync(int departmentId, string permitId) => Task.FromResult<IEnumerable<RmsPlanReview>>(Rows.Where(r => r.DepartmentId == departmentId && r.RmsPermitId == permitId).ToList());
		public Task<RmsPlanReview> GetByIdForDepartmentAsync(int departmentId, string reviewId) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.RmsPlanReviewId == reviewId));
	}

	public class FakeCrr : InMemoryRepo<RmsCrrActivity>, IRmsCrrActivitiesRepository
	{
		public Task<RmsCrrActivity> GetByIdForDepartmentAsync(int departmentId, string activityId) => Task.FromResult(Rows.FirstOrDefault(a => a.DepartmentId == departmentId && a.RmsCrrActivityId == activityId));
		public Task<IEnumerable<RmsCrrActivity>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take) => Task.FromResult<IEnumerable<RmsCrrActivity>>(Live(departmentId).Where(a => a.OccurredOn >= startUtc && a.OccurredOn < endUtc).Take(take).ToList());
		public Task<IEnumerable<RmsCrrActivity>> GetForOccupancyAsync(int departmentId, string occupancyId) => Task.FromResult<IEnumerable<RmsCrrActivity>>(Live(departmentId).Where(a => a.RmsOccupancyId == occupancyId).ToList());
	}

	public class FakePreventionAttachments : InMemoryRepo<RmsPreventionAttachment>, IRmsPreventionAttachmentsRepository
	{
		public Task<RmsPreventionAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId) => Task.FromResult(Rows.FirstOrDefault(a => a.DepartmentId == departmentId && a.RmsPreventionAttachmentId == attachmentId));
		public Task<IEnumerable<RmsPreventionAttachment>> GetMetadataForParentAsync(int departmentId, RmsPreventionParentKind parentKind, string parentId) => Task.FromResult<IEnumerable<RmsPreventionAttachment>>(Live(departmentId).Where(a => a.ParentKind == (int)parentKind && a.ParentId == parentId).ToList());
	}

	public class FakeCases : InMemoryRepo<RmsInvestigationCase>, IRmsInvestigationCasesRepository
	{
		public Task<RmsInvestigationCase> GetByIdForDepartmentAsync(int departmentId, string caseId) => Task.FromResult(Rows.FirstOrDefault(c => c.DepartmentId == departmentId && c.RmsInvestigationCaseId == caseId));
		public Task<IEnumerable<RmsInvestigationCase>> GetByIdsAsync(int departmentId, IEnumerable<string> caseIds) { var ids = caseIds.ToHashSet(); return Task.FromResult<IEnumerable<RmsInvestigationCase>>(Live(departmentId).Where(c => ids.Contains(c.RmsInvestigationCaseId)).ToList()); }
		public Task<IEnumerable<RmsInvestigationCase>> GetForDepartmentAsync(int departmentId, bool includeClosed, int skip, int take) => Task.FromResult<IEnumerable<RmsInvestigationCase>>(Live(departmentId).Where(c => includeClosed || !c.IsClosed).Skip(skip).Take(take).ToList());
		public Task<int> CountOpenAsync(int departmentId) => Task.FromResult(Live(departmentId).Count(c => !c.IsClosed));
		public Task<bool> TryBumpRowVersionAsync(int departmentId, string caseId, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
	}

	public class FakeCaseIncidents : InMemoryRepo<RmsInvestigationCaseIncident>, IRmsInvestigationCaseIncidentsRepository
	{
		public Task<IEnumerable<RmsInvestigationCaseIncident>> GetForCaseAsync(int departmentId, string caseId) => Task.FromResult<IEnumerable<RmsInvestigationCaseIncident>>(Rows.Where(i => i.DepartmentId == departmentId && i.RmsInvestigationCaseId == caseId).ToList());
		public Task<IEnumerable<RmsInvestigationCaseIncident>> GetForRecordAsync(int departmentId, string recordId) => Task.FromResult<IEnumerable<RmsInvestigationCaseIncident>>(Rows.Where(i => i.DepartmentId == departmentId && i.RecordId == recordId).ToList());
	}

	public class FakeCaseMembers : InMemoryRepo<RmsInvestigationCaseMember>, IRmsInvestigationCaseMembersRepository
	{
		public Task<IEnumerable<RmsInvestigationCaseMember>> GetForCaseAsync(int departmentId, string caseId) => Task.FromResult<IEnumerable<RmsInvestigationCaseMember>>(Rows.Where(m => m.DepartmentId == departmentId && m.RmsInvestigationCaseId == caseId).ToList());
		public Task<IEnumerable<RmsInvestigationCaseMember>> GetActiveForUserAsync(int departmentId, string userId) => Task.FromResult<IEnumerable<RmsInvestigationCaseMember>>(Rows.Where(m => m.DepartmentId == departmentId && m.UserId == userId && m.IsActive).ToList());
		public Task<RmsInvestigationCaseMember> GetByIdForDepartmentAsync(int departmentId, string memberId) => Task.FromResult(Rows.FirstOrDefault(m => m.DepartmentId == departmentId && m.RmsInvestigationCaseMemberId == memberId));
	}

	public class FakeCaseNotes : InMemoryRepo<RmsInvestigationNote>, IRmsInvestigationNotesRepository
	{
		public Task<RmsInvestigationNote> GetByIdForDepartmentAsync(int departmentId, string noteId) => Task.FromResult(Rows.FirstOrDefault(n => n.DepartmentId == departmentId && n.RmsInvestigationNoteId == noteId));
		public Task<IEnumerable<RmsInvestigationNote>> GetForCaseAsync(int departmentId, string caseId) => Task.FromResult<IEnumerable<RmsInvestigationNote>>(Live(departmentId).Where(n => n.RmsInvestigationCaseId == caseId).ToList());
		public Task<int> LockForCaseAsync(int departmentId, string caseId, DateTime utcNow, CancellationToken cancellationToken = default) { var n = 0; foreach (var note in Live(departmentId).Where(x => x.RmsInvestigationCaseId == caseId && !x.IsLocked)) { note.IsLocked = true; n++; } return Task.FromResult(n); }
	}

	public class FakeEvidence : InMemoryRepo<RmsInvestigationEvidence>, IRmsInvestigationEvidenceRepository
	{
		public Task<RmsInvestigationEvidence> GetByIdForDepartmentAsync(int departmentId, string evidenceId) => Task.FromResult(Rows.FirstOrDefault(e => e.DepartmentId == departmentId && e.RmsInvestigationEvidenceId == evidenceId));
		public Task<IEnumerable<RmsInvestigationEvidence>> GetForCaseAsync(int departmentId, string caseId) => Task.FromResult<IEnumerable<RmsInvestigationEvidence>>(Live(departmentId).Where(e => e.RmsInvestigationCaseId == caseId).ToList());
	}

	public class FakeCustody : InMemoryRepo<RmsInvestigationCustody>, IRmsInvestigationCustodyRepository
	{
		public Task<IEnumerable<RmsInvestigationCustody>> GetForEvidenceAsync(int departmentId, string evidenceId) => Task.FromResult<IEnumerable<RmsInvestigationCustody>>(Rows.Where(c => c.DepartmentId == departmentId && c.RmsInvestigationEvidenceId == evidenceId).OrderBy(c => c.Sequence).ToList());
	}

	public class FakeReferrals : InMemoryRepo<RmsInvestigationReferral>, IRmsInvestigationReferralsRepository
	{
		public Task<RmsInvestigationReferral> GetByIdForDepartmentAsync(int departmentId, string referralId) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.RmsInvestigationReferralId == referralId));
		public Task<IEnumerable<RmsInvestigationReferral>> GetForCaseAsync(int departmentId, string caseId) => Task.FromResult<IEnumerable<RmsInvestigationReferral>>(Rows.Where(r => r.DepartmentId == departmentId && r.RmsInvestigationCaseId == caseId).ToList());
	}

	public class FakeRubrics : InMemoryRepo<RmsQualityRubric>, IRmsQualityRubricsRepository
	{
		public Task<RmsQualityRubric> GetByIdForDepartmentAsync(int departmentId, string rubricId) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.RmsQualityRubricId == rubricId));
		public Task<IEnumerable<RmsQualityRubric>> GetForDepartmentAsync(int departmentId, bool includeInactive) => Task.FromResult<IEnumerable<RmsQualityRubric>>(Live(departmentId).Where(r => includeInactive || r.IsActive).ToList());
	}

	public class FakeQualityReviews : InMemoryRepo<RmsQualityReview>, IRmsQualityReviewsRepository
	{
		public Task<RmsQualityReview> GetByIdForDepartmentAsync(int departmentId, string reviewId) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.RmsQualityReviewId == reviewId));
		public Task<IEnumerable<RmsQualityReview>> GetForRecordAsync(int departmentId, string recordId) => Task.FromResult<IEnumerable<RmsQualityReview>>(Rows.Where(r => r.DepartmentId == departmentId && r.RecordId == recordId).ToList());
		public Task<IEnumerable<RmsQualityReview>> GetPendingAsync(int departmentId, int take) => Task.FromResult<IEnumerable<RmsQualityReview>>(Rows.Where(r => r.DepartmentId == departmentId && r.ScoredOn == null).Take(take).ToList());
		public Task<IEnumerable<RmsQualityReview>> GetScoredSinceAsync(int departmentId, DateTime sinceUtc, int take) => Task.FromResult<IEnumerable<RmsQualityReview>>(Rows.Where(r => r.DepartmentId == departmentId && r.ScoredOn >= sinceUtc).Take(take).ToList());
		public Task<IEnumerable<string>> GetReviewedRecordIdsAsync(int departmentId, IEnumerable<string> recordIds) { var ids = recordIds.ToHashSet(); return Task.FromResult<IEnumerable<string>>(Rows.Where(r => r.DepartmentId == departmentId && ids.Contains(r.RecordId)).Select(r => r.RecordId).Distinct().ToList()); }
	}

	/// <summary>Captures enqueued outbox envelopes so a test can assert on trigger and payload without a database.</summary>
	public class FakeOutbox : IDomainEventOutboxService
	{
		public List<(int DepartmentId, DomainEventEnvelope Envelope)> Enqueued { get; } = new List<(int, DomainEventEnvelope)>();
		public List<long> Dispatched { get; } = new List<long>();
		public Task<DomainEventOutboxEntry> EnqueueAsync(int departmentId, string producerSubsystem, DomainEventEnvelope envelope, CancellationToken cancellationToken = default)
		{
			Enqueued.Add((departmentId, envelope));
			return Task.FromResult(new DomainEventOutboxEntry { DomainEventOutboxId = Enqueued.Count, DepartmentId = departmentId, EventName = envelope.EventName, TriggerEventType = (int)envelope.Trigger });
		}
		public Task<int> DispatchAfterCommitAsync(IEnumerable<long> domainEventOutboxIds, CancellationToken cancellationToken = default) { Dispatched.AddRange(domainEventOutboxIds); return Task.FromResult(Dispatched.Count); }
		public Task<int> DispatchPendingAsync(string leaseOwner, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
		public Task<DomainEventOutboxHealth> GetHealthAsync() => Task.FromResult(new DomainEventOutboxHealth());
		public Task<int> PurgeDispatchedAsync(int olderThanDays, CancellationToken cancellationToken = default) => Task.FromResult(0);
	}

	/// <summary>
	/// One department with Records active and every RMS-5 flag on; three people: an administrator with the
	/// prevention permission and restricted view, a member with neither, and a second administrator. Every fake
	/// repository is exposed so a test can seed rows or inspect what a service wrote.
	/// </summary>
	public sealed class RmsPreventionHarness
	{
		public const int Dept = 7;
		public const string Admin = "admin-1";
		public const string Admin2 = "admin-2";
		public const string Member = "member-1";
		public const string Outsider = "outsider-1";

		public Mock<IRecordsCutoverService> Cutover { get; } = new Mock<IRecordsCutoverService>();
		public Mock<IFeatureToggleService> Flags { get; } = new Mock<IFeatureToggleService>();
		public Mock<IRecordsAuthorizationService> Authorization { get; } = new Mock<IRecordsAuthorizationService>();
		public Mock<IUnitOfWork> UnitOfWork { get; } = new Mock<IUnitOfWork>();
		public Mock<IProtectedReadService> ProtectedReads { get; } = new Mock<IProtectedReadService>();
		public Mock<IProtectedGrantContext> Grant { get; } = new Mock<IProtectedGrantContext>();
		public Mock<IContactPreplanRepository> ContactPreplans { get; } = new Mock<IContactPreplanRepository>();
		public Mock<IContactPreplanHazardRepository> ContactHazards { get; } = new Mock<IContactPreplanHazardRepository>();
		public Mock<IContactsRepository> Contacts { get; } = new Mock<IContactsRepository>();
		public Mock<IAddressRepository> Addresses { get; } = new Mock<IAddressRepository>();
		public Mock<IPoisRepository> Pois { get; } = new Mock<IPoisRepository>();
		public Mock<IRmsIncidentReportsRepository> Reports { get; } = new Mock<IRmsIncidentReportsRepository>();
		public Mock<IRmsOperationalRecordsRepository> Records { get; } = new Mock<IRmsOperationalRecordsRepository>();
		public Mock<IRmsRecordUnitResponsesRepository> Units { get; } = new Mock<IRmsRecordUnitResponsesRepository>();
		public Mock<Resgrid.Model.Providers.IRecordAttachmentScanner> Scanner { get; } = new Mock<Resgrid.Model.Providers.IRecordAttachmentScanner>();
		public PassthroughRecordsProtection Protection { get; } = new PassthroughRecordsProtection();
		public FakeOutbox Outbox { get; } = new FakeOutbox();
		public HashSet<string> DisabledFlags { get; } = new HashSet<string>();
		public HashSet<string> PreventionAdmins { get; } = new HashSet<string> { Admin, Admin2 };
		public HashSet<string> RestrictedViewers { get; } = new HashSet<string> { Admin, Admin2 };
		public HashSet<string> Reviewers { get; } = new HashSet<string> { Admin, Admin2 };
		public HashSet<string> DepartmentAdmins { get; } = new HashSet<string> { Admin, Admin2 };
		public bool RecordsUsable { get; set; } = true;

		public FakeSequences Sequences { get; } = new FakeSequences();
		public FakeAudits Audits { get; } = new FakeAudits();
		public FakeOccupancies Occupancies { get; } = new FakeOccupancies();
		public FakeContactLinks Links { get; } = new FakeContactLinks();
		public FakeOccupancyHazards Hazards { get; } = new FakeOccupancyHazards();
		public FakeCrosswalks Crosswalks { get; } = new FakeCrosswalks();
		public FakeProvenance Provenance { get; } = new FakeProvenance();
		public FakeOwnerships Ownerships { get; } = new FakeOwnerships();
		public FakeCodeSets CodeSets { get; } = new FakeCodeSets();
		public FakeCodeSections CodeSections { get; } = new FakeCodeSections();
		public FakePrograms Programs { get; } = new FakePrograms();
		public FakeInspections Inspections { get; } = new FakeInspections();
		public FakeViolations Violations { get; } = new FakeViolations();
		public FakeHydrants Hydrants { get; } = new FakeHydrants();
		public FakeFlowTests FlowTests { get; } = new FakeFlowTests();
		public FakeMaintenance Maintenance { get; } = new FakeMaintenance();
		public FakePermitTypes PermitTypes { get; } = new FakePermitTypes();
		public FakePermits Permits { get; } = new FakePermits();
		public FakePlanReviews PlanReviews { get; } = new FakePlanReviews();
		public FakeCrr Crr { get; } = new FakeCrr();
		public FakePreventionAttachments Attachments { get; } = new FakePreventionAttachments();
		public FakeCases Cases { get; } = new FakeCases();
		public FakeCaseIncidents CaseIncidents { get; } = new FakeCaseIncidents();
		public FakeCaseMembers CaseMembers { get; } = new FakeCaseMembers();
		public FakeCaseNotes CaseNotes { get; } = new FakeCaseNotes();
		public FakeEvidence Evidence { get; } = new FakeEvidence();
		public FakeCustody Custody { get; } = new FakeCustody();
		public FakeReferrals Referrals { get; } = new FakeReferrals();
		public FakeRubrics Rubrics { get; } = new FakeRubrics();
		public FakeQualityReviews QualityReviews { get; } = new FakeQualityReviews();

		public RecordsPreventionGate Gate { get; }
		public RecordsOccupancyService OccupancyService { get; }
		public RecordsInspectionsService InspectionsService { get; }
		public RecordsHydrantsService HydrantsService { get; }
		public RecordsPermitsService PermitsService { get; }
		public RecordsCrrService CrrService { get; }
		public RecordsPreventionAttachmentsService AttachmentsService { get; }
		public RecordsInvestigationsService InvestigationsService { get; }
		public RecordsQualityReviewService QualityService { get; }

		public RmsPreventionHarness()
		{
			Cutover.Setup(c => c.GetModuleStateAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int d, bool b) => new RecordsModuleState { DepartmentId = d, FlagEnabled = RecordsUsable, Activated = RecordsUsable, ActivatedOn = DateTime.UtcNow.AddDays(-30), CutoverState = RecordsUsable ? RmsDepartmentCutoverState.Active : (RmsDepartmentCutoverState?)null });
			Flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync((string key, int d, bool dv, IDictionary<string, string> ctx) => !DisabledFlags.Contains(key));
			Authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string u, int d) => u != Outsider);
			Authorization.Setup(a => a.IsDepartmentAdminAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string u, int d) => DepartmentAdmins.Contains(u));
			Authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync((string u, int d, PermissionTypes p) =>
				p == PermissionTypes.RecordsPreventionAdmin ? PreventionAdmins.Contains(u) : p == PermissionTypes.ViewRestrictedRecords ? RestrictedViewers.Contains(u) : p == PermissionTypes.ReviewRecords ? Reviewers.Contains(u) : DepartmentAdmins.Contains(u));
			Authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			ProtectedReads.Setup(r => r.ResolveContactPreplansForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<ContactPreplan>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());
			ProtectedReads.Setup(r => r.ResolveContactPreplanHazardsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<ContactPreplanHazard>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());
			Scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Resgrid.Model.Providers.RecordAttachmentScanResult { State = RmsAttachmentScanState.Skipped });
			Grant.SetupGet(g => g.UserId).Returns(Admin);

			Gate = new RecordsPreventionGate(Cutover.Object, Flags.Object, Authorization.Object, Sequences, Audits);
			OccupancyService = new RecordsOccupancyService(Gate, Occupancies, Links, Hazards, Crosswalks, Provenance, Ownerships, Violations, Hydrants, ContactPreplans.Object, ContactHazards.Object, Contacts.Object, Addresses.Object, Pois.Object, ProtectedReads.Object, Grant.Object, Protection, UnitOfWork.Object);
			InspectionsService = new RecordsInspectionsService(Gate, CodeSets, CodeSections, Programs, Inspections, Violations, Occupancies, Attachments, Protection, Outbox, UnitOfWork.Object);
			HydrantsService = new RecordsHydrantsService(Gate, Hydrants, FlowTests, Maintenance, Attachments);
			PermitsService = new RecordsPermitsService(Gate, PermitTypes, Permits, PlanReviews, Occupancies, Attachments, Protection, Outbox, UnitOfWork.Object);
			CrrService = new RecordsCrrService(Gate, Crr, Occupancies);
			AttachmentsService = new RecordsPreventionAttachmentsService(Gate, Attachments, CaseMembers, Evidence, Scanner.Object, Protection);
			InvestigationsService = new RecordsInvestigationsService(Gate, Cases, CaseIncidents, CaseMembers, CaseNotes, Evidence, Custody, Referrals, Attachments, Reports.Object, Audits, Authorization.Object, Protection, UnitOfWork.Object);
			QualityService = new RecordsQualityReviewService(Gate, Rubrics, QualityReviews, Records.Object, Units.Object, Authorization.Object, Protection, Audits);
		}

		public RmsOccupancy SeedOccupancy(string name = "Riverside Mill", string address = "12 River Rd", decimal? lat = 45.5m, decimal? lon = -122.6m, int occupancyType = 3)
		{
			var o = new RmsOccupancy { RmsOccupancyId = Guid.NewGuid().ToString(), DepartmentId = Dept, ProtectionId = Guid.NewGuid().ToString(), OccupancyNumber = $"OCC-2026-{Occupancies.Rows.Count + 1:0000}", Name = name, Status = (int)RmsOccupancyStatus.Active, AddressText = address, NormalizedAddress = AddressNormalizer.Normalize(address), Latitude = lat, Longitude = lon, OccupancyType = occupancyType, CreatedOn = DateTime.UtcNow.AddYears(-2), ModifiedOn = DateTime.UtcNow.AddYears(-2), RowVersion = 1 };
			Occupancies.Rows.Add(o);
			return o;
		}
	}
}
