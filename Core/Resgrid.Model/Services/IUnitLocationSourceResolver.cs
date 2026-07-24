using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUnitLocationSourceResolver
	{
		Task<ResolvedUnitLocation> ResolveAsync(
			int departmentId,
			IReadOnlyCollection<UnitsLocation> locations,
			DateTime? utcNow = null);
	}
}
