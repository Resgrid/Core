using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Round 9A (M0405-M0406): certification requirement sets and the issuance ledger. Every read is department scoped.

	public interface ILmsCertificationRequirementSetsRepository : IRepository<LmsCertificationRequirementSet>
	{
		Task<List<LmsCertificationRequirementSet>> GetForDepartmentAsync(int departmentId);

		Task<LmsCertificationRequirementSet> GetForTypeAsync(int departmentId, int departmentCertificationTypeId);

		/// <summary>The due-work query of worker 94's requirement-set step: departments with at least one active set.</summary>
		Task<List<int>> GetDepartmentIdsWithActiveSetsAsync();

		/// <summary>A cheap check before any engine work on a write: the department has an active set.</summary>
		Task<bool> HasActiveSetsAsync(int departmentId);
	}

	public interface ILmsCertificationRequirementItemsRepository : IRepository<LmsCertificationRequirementItem>
	{
		Task<List<LmsCertificationRequirementItem>> GetForSetsAsync(int departmentId, IEnumerable<int> setIds);

		/// <summary>Removes a set's items (the editor replaces them all on save).</summary>
		Task<int> DeleteForSetAsync(int departmentId, int setId, CancellationToken cancellationToken = default);
	}

	public interface ILmsCertificationIssuancesRepository : IRepository<LmsCertificationIssuance>
	{
		/// <summary>The member's live issuances (a void re-evaluates these), oldest first.</summary>
		Task<List<LmsCertificationIssuance>> GetLiveForUserAsync(int departmentId, string userId);

		/// <summary>Every issuance on one record, oldest first (the certification page's history).</summary>
		Task<List<LmsCertificationIssuance>> GetForCertificationAsync(int departmentId, int personnelCertificationId);

		/// <summary>The member's most recent issuances, newest first.</summary>
		Task<List<LmsCertificationIssuance>> GetRecentForUserAsync(int departmentId, string userId, int take);

		/// <summary>Writes the state, basis and roll-back columns of one row.</summary>
		Task<bool> UpdateStateAsync(LmsCertificationIssuance issuance, CancellationToken cancellationToken = default);
	}
}
