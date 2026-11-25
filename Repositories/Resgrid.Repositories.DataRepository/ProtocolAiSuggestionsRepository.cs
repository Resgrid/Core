using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Enhanced AI protocol suggestions (Protocols P4, M0400). Every read is scoped to the department.</summary>
	public class ProtocolAiSuggestionsRepository : RmsRepositoryBase<ProtocolAiSuggestion>, IProtocolAiSuggestionsRepository
	{
		public ProtocolAiSuggestionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ProtocolAiSuggestion> GetByIdForDepartmentAsync(int departmentId, int protocolAiSuggestionId)
		{
			return QueryFirstOrDefaultAsync<ProtocolAiSuggestion>(
				$"SELECT * FROM {Tbl("ProtocolAiSuggestions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolAiSuggestionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolAiSuggestionId });
		}

		public async Task<List<ProtocolAiSuggestion>> GetForCallAsync(int departmentId, int callId)
		{
			return (await QueryAsync<ProtocolAiSuggestion>(
				$"SELECT * FROM {Tbl("ProtocolAiSuggestions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId " +
				$"ORDER BY {Col("CreatedOn")} DESC, {Col("ProtocolAiSuggestionId")} DESC",
				new { DepartmentId = departmentId, CallId = callId })).ToList();
		}

		public async Task<bool> TryConfirmAsync(int departmentId, int protocolAiSuggestionId, string userId, DateTime confirmedOnUtc, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || protocolAiSuggestionId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", protocolAiSuggestionId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("On", DatabaseTimestamp(confirmedOnUtc));
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolAiSuggestions")} SET {Col("ConfirmedOn")} = {P}On, {Col("ConfirmedByUserId")} = {P}UserId " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolAiSuggestionId")} = {P}Id AND {Col("ConfirmedOn")} IS NULL",
				parameters, cancellationToken);
			return changed == 1;
		}
	}
}
