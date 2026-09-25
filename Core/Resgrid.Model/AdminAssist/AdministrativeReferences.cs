using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record AdministrativeReferenceCounts(int PolicyReferences, int UnavailablePolicies, int ExpiringPolicies, int SiteReferences, int UnavailableSites);
	public interface IAdministrativeReferenceStore
	{
		Task<AdministrativeReferenceCounts> ReadAdministrativeReferencesAsync(int departmentId, int[] documentIds, int[] groupIds, DateTime asOfUtc, CancellationToken cancellationToken);
	}
}
