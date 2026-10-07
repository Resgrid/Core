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
	/// <summary>Department API keys (M0265).</summary>
	public class DepartmentApiKeysRepository : RmsRepositoryBase<DepartmentApiKey>, IDepartmentApiKeysRepository
	{
		public DepartmentApiKeysRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DepartmentApiKey> GetBySecretHashAsync(string secretHash)
		{
			return QueryFirstOrDefaultAsync<DepartmentApiKey>(
				$"SELECT * FROM {Tbl("DepartmentApiKeys")} WHERE {Col("SecretHash")} = {P}SecretHash",
				new { SecretHash = secretHash });
		}

		public async Task<List<DepartmentApiKey>> GetAllForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<DepartmentApiKey>(
				$"SELECT * FROM {Tbl("DepartmentApiKeys")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("CreatedOn")} DESC",
				new { DepartmentId = departmentId });

			return rows?.ToList() ?? new List<DepartmentApiKey>();
		}

		public Task MarkUsedAsync(string departmentApiKeyId, DateTime usedOn, string ipAddress, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("DepartmentApiKeys")} SET {Col("LastUsedOn")} = {P}UsedOn, {Col("LastUsedIp")} = {P}Ip WHERE {Col("DepartmentApiKeyId")} = {P}Id",
				new { Id = departmentApiKeyId, UsedOn = DatabaseTimestamp(usedOn), Ip = Truncate(ipAddress, 64) },
				cancellationToken);
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
