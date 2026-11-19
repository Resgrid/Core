using System;
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
	/// <summary>Destination rules (M0317).</summary>
	public class DestinationRulesRepository : RmsRepositoryBase<DestinationRule>, IDestinationRulesRepository
	{
		public DestinationRulesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> UpdateRuleAsync(DestinationRule rule, CancellationToken cancellationToken = default)
		{
			if (rule == null)
				throw new ArgumentNullException(nameof(rule));

			Utf8WriteGuard.Sanitize(rule);

			// The base UpdateAsync returns the entity whatever the row count; this one reports a rule deleted meanwhile.
			var sql = $"UPDATE {Tbl("DestinationRules")} SET {Col("Name")} = {P}Name, {Col("CallTypeId")} = {P}CallTypeId, " +
				$"{Col("CallPriority")} = {P}CallPriority, {Col("UnitTypeId")} = {P}UnitTypeId, {Col("PoiTypeIds")} = {P}PoiTypeIds, " +
				$"{Col("RequiredCapabilityIds")} = {P}RequiredCapabilityIds, {Col("PreferredCapabilityIds")} = {P}PreferredCapabilityIds, " +
				$"{Col("UpdatedByUserId")} = {P}UpdatedByUserId, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("DestinationRuleId")} = {CaseInsensitive("DestinationRuleId")} AND {Col("DepartmentId")} = {P}DepartmentId";

			var parameters = new DynamicParameters();
			parameters.Add("Name", rule.Name);
			parameters.Add("CallTypeId", rule.CallTypeId);
			parameters.Add("CallPriority", rule.CallPriority);
			parameters.Add("UnitTypeId", rule.UnitTypeId);
			parameters.Add("PoiTypeIds", rule.PoiTypeIds);
			parameters.Add("RequiredCapabilityIds", rule.RequiredCapabilityIds);
			parameters.Add("PreferredCapabilityIds", rule.PreferredCapabilityIds);
			parameters.Add("UpdatedByUserId", rule.UpdatedByUserId);
			parameters.Add("UpdatedOn", rule.UpdatedOn.HasValue ? DatabaseTimestamp(rule.UpdatedOn.Value) : (DateTime?)null);
			parameters.Add("DestinationRuleId", rule.DestinationRuleId);
			parameters.Add("DepartmentId", rule.DepartmentId);

			var rows = await RunAsync(conn => conn.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, UnitOfWork?.Transaction, cancellationToken: cancellationToken)),
				cancellationToken);

			return rows > 0;
		}
	}
}
