using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Members released from open calls (M0316).</summary>
	public class CallPersonnelReleasesRepository : RmsRepositoryBase<CallPersonnelRelease>, ICallPersonnelReleasesRepository
	{
		public CallPersonnelReleasesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<CallPersonnelRelease>> GetByCallIdAsync(int callId)
		{
			return QueryAsync<CallPersonnelRelease>(
				$"SELECT * FROM {Tbl("CallPersonnelReleases")} WHERE {Col("CallId")} = {P}CallId",
				new { CallId = callId });
		}

		public Task<IEnumerable<CallPersonnelRelease>> GetForCallsAsync(int departmentId, IEnumerable<int> callIds)
		{
			var ids = InListValue((callIds ?? Enumerable.Empty<int>()).Distinct());
			if (ids.Length == 0)
				return Task.FromResult(Enumerable.Empty<CallPersonnelRelease>());

			return QueryAsync<CallPersonnelRelease>(
				$"SELECT * FROM {Tbl("CallPersonnelReleases")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("CallId", "CallIds")}",
				new { DepartmentId = departmentId, CallIds = ids });
		}

		public async Task<int> DeleteForUsersAsync(int callId, IEnumerable<string> userIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((userIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
			if (ids.Length == 0)
				return 0;

			// One statement, so the members named back on the call lose their releases together or not at all. The user ids
			// compare case-insensitively in both dialects (citext[] on PostgreSQL, the collation on SQL Server), as the
			// release's unique index does.
			return await ExecuteAsync(
				$"DELETE FROM {Tbl("CallPersonnelReleases")} WHERE {Col("CallId")} = {P}CallId AND {CaseInsensitiveInList("UserId", "UserIds")}",
				new { CallId = callId, UserIds = ids }, cancellationToken);
		}
	}
}
