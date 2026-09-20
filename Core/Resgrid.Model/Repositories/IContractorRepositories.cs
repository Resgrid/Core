using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Repositories
{
	// Workforce & Business Operations plan, Phase C (C2): contractor path repositories (registry M0215–M0217).

	public interface IRateScheduleRepository : IRepository<RateSchedule>
	{
		Task<RateSchedule> GetByIdForDepartmentAsync(string rateScheduleId, int departmentId);
		Task<IEnumerable<RateSchedule>> GetForDepartmentAsync(int departmentId, bool includeInactive);
	}

	public interface IRateScheduleEntryRepository : IRepository<RateScheduleEntry>
	{
		Task<IEnumerable<RateScheduleEntry>> GetByScheduleAsync(string rateScheduleId, bool includeInactive);
		Task<IEnumerable<RateScheduleEntry>> GetByIdsAsync(IEnumerable<string> rateScheduleEntryIds);
	}

	public interface IRateScheduleEntryBandRepository : IRepository<RateScheduleEntryBand>
	{
		Task<IEnumerable<RateScheduleEntryBand>> GetByScheduleAsync(string rateScheduleId);
		Task<IEnumerable<RateScheduleEntryBand>> GetByEntryAsync(string rateScheduleEntryId);
		Task<bool> DeleteByEntryAsync(string rateScheduleEntryId, CancellationToken cancellationToken = default);
	}

	public interface IRatePremiumRepository : IRepository<RatePremium>
	{
		Task<IEnumerable<RatePremium>> GetByScheduleAsync(string rateScheduleId, bool includeInactive);
	}

	public interface IServiceContractRepository : IRepository<ServiceContract>
	{
		Task<ServiceContract> GetByIdForDepartmentAsync(string serviceContractId, int departmentId);
		Task<IEnumerable<ServiceContract>> GetForDepartmentAsync(int departmentId, int? status);
		Task<IEnumerable<ServiceContract>> GetByContactIdAsync(int departmentId, string contactId);
		/// <summary>Active contracts whose <c>EndOn</c> falls inside the window (expiry sweep).</summary>
		Task<IEnumerable<ServiceContract>> GetEndingBetweenAsync(DateTime fromUtc, DateTime toUtc);
		/// <summary>Active contracts whose <c>EndOn</c> is already behind <paramref name="asOfUtc"/>.</summary>
		Task<IEnumerable<ServiceContract>> GetLapsedAsync(DateTime asOfUtc);
	}

	public interface IServiceContractDocumentRequirementRepository : IRepository<ServiceContractDocumentRequirement>
	{
		Task<IEnumerable<ServiceContractDocumentRequirement>> GetByContractAsync(string serviceContractId);
		Task<bool> DeleteByContractAsync(string serviceContractId, CancellationToken cancellationToken = default);
	}

	public interface IDepartmentComplianceDocumentRepository : IRepository<DepartmentComplianceDocument>
	{
		/// <summary>Document rows without their bytes.</summary>
		Task<IEnumerable<DepartmentComplianceDocument>> GetForDepartmentAsync(int departmentId);
		Task<DepartmentComplianceDocument> GetByIdWithDataAsync(int departmentComplianceDocumentId);
		/// <summary>Documents (all departments, no bytes) whose expiry is inside <c>[asOf, asOf + AlertLeadDays]</c> or already past.</summary>
		Task<IEnumerable<DepartmentComplianceDocument>> GetExpiringAsync(DateTime asOfUtc);
	}

	public interface IBidRepository : IRepository<Bid>
	{
		Task<Bid> GetByIdForDepartmentAsync(string bidId, int departmentId);
		Task<IEnumerable<Bid>> GetForDepartmentAsync(int departmentId, int? status, int skip, int take);
		Task<int> CountForDepartmentAsync(int departmentId, int? status);
		Task<IEnumerable<Bid>> GetByContactIdAsync(int departmentId, string contactId, int skip, int take);
		Task<IEnumerable<Bid>> GetByContractAsync(string serviceContractId);
		/// <summary>Submitted bids (all departments) whose <c>ValidUntil</c> is behind <paramref name="asOfUtc"/>.</summary>
		Task<IEnumerable<Bid>> GetExpiryCandidatesAsync(DateTime asOfUtc);
	}

	public interface IBidLineItemRepository : IRepository<BidLineItem>
	{
		Task<IEnumerable<BidLineItem>> GetByBidAsync(string bidId);
	}

	public interface IBidNumberSequenceRepository : IRepository<BidNumberSequence>
	{
		/// <summary>Atomically returns the next bid number for the department and advances the sequence (dialect-specific SQL).</summary>
		Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
