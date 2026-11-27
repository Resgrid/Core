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
	/// <summary>Protocol text as dispatched, once per protocol and content hash (M0337).</summary>
	public class DispatchProtocolSnapshotsRepository : RmsRepositoryBase<DispatchProtocolSnapshot>, IDispatchProtocolSnapshotsRepository
	{
		public DispatchProtocolSnapshotsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task InsertIfMissingAsync(DispatchProtocolSnapshot snapshot, CancellationToken cancellationToken = default)
		{
			if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ContentHash))
				return;

			var columns = Cols("DepartmentId", "DispatchProtocolId", "ContentHash", "ProtocolText", "FirstUsedOn");
			var parameters = new
			{
				snapshot.DepartmentId,
				snapshot.DispatchProtocolId,
				snapshot.ContentHash,
				snapshot.ProtocolText,
				FirstUsedOn = DatabaseTimestamp(snapshot.FirstUsedOn == default ? DateTime.UtcNow : snapshot.FirstUsedOn)
			};

			// The unique index on (DispatchProtocolId, ContentHash) is the real guard; these forms make the common race (two calls
			// attaching the same text at once) a no-op instead of a failed insert.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("DispatchProtocolSnapshots")} ({columns}) VALUES ({P}DepartmentId, {P}DispatchProtocolId, {P}ContentHash, {P}ProtocolText, {P}FirstUsedOn) " +
				  $"ON CONFLICT ({Cols("DispatchProtocolId", "ContentHash")}) DO NOTHING"
				: $"INSERT INTO {Tbl("DispatchProtocolSnapshots")} ({columns}) SELECT {P}DepartmentId, {P}DispatchProtocolId, {P}ContentHash, {P}ProtocolText, {P}FirstUsedOn " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("DispatchProtocolSnapshots")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DispatchProtocolId")} = {P}DispatchProtocolId AND {Col("ContentHash")} = {P}ContentHash)";

			await ExecuteAsync(sql, parameters, cancellationToken);
		}

		public async Task<IEnumerable<DispatchProtocolSnapshot>> GetByHashesAsync(int departmentId, IEnumerable<string> contentHashes)
		{
			var hashes = InListValue((contentHashes ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal));
			if (hashes.Length == 0)
				return Enumerable.Empty<DispatchProtocolSnapshot>();

			return await QueryAsync<DispatchProtocolSnapshot>(
				$"SELECT * FROM {Tbl("DispatchProtocolSnapshots")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContentHash", "Hashes")}",
				new { DepartmentId = departmentId, Hashes = hashes });
		}
	}
}
