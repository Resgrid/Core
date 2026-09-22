using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Queries.Calls;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<IEnumerable<Call>> SearchCallCandidatesAsync(int departmentId, CallSearchQuery query)
		{
			if (departmentId <= 0) throw new ArgumentOutOfRangeException(nameof(departmentId));
			if (query == null) throw new ArgumentNullException(nameof(query));
			var term = (query.Term ?? "").Trim();
			if (term.Length > 200) term = term.Substring(0, 200);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Term", "%" + SearchCallsQuery.EscapeLike(term) + "%");
			DateTime? Timestamp(DateTime? value) => value.HasValue && _sqlConfiguration is Servers.SqlServer.PostgreSqlConfiguration
				? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : value;
			parameters.Add("FromUtc", Timestamp(query.FromUtc), DbType.DateTime2);
			parameters.Add("UntilUtc", Timestamp(query.UntilUtc), DbType.DateTime2);
			parameters.Add("Offset", Math.Max(0, query.Offset));
			parameters.Add("Take", Math.Clamp(query.Take, 1, 101));
			var sql = SearchCallsQuery.Build(_sqlConfiguration, query);
			Task<IEnumerable<Call>> Select(DbConnection connection) => connection.QueryAsync<Call>(sql, parameters, _unitOfWork?.Transaction);
			if (_unitOfWork?.Connection != null) return await Select(_unitOfWork.CreateOrGetConnection());
			using var connection = _connectionProvider.Create();
			await connection.OpenAsync();
			return await Select(connection);
		}
	}
}
