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
			var ids = (userIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var removed = 0;

			// Few rows (a dispatcher names people back one at a time): one statement per member keeps the user-id comparison
			// the same in both dialects (citext on PostgreSQL).
			foreach (var id in ids)
			{
				removed += await ExecuteAsync(
					$"DELETE FROM {Tbl("CallPersonnelReleases")} WHERE {Col("CallId")} = {P}CallId AND {Col("UserId")} = {P}UserId",
					new { CallId = callId, UserId = id }, cancellationToken);
			}

			return removed;
		}
	}
}
