using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class RmsUdfDefinitionsRepository : RmsRepositoryBase<UdfDefinition>, IRmsUdfDefinitionsRepository
	{
		public RmsUdfDefinitionsRepository(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unit, IQueryFactory queries) : base(connections, configuration, unit, queries) { }
		private string Scope => $"{Col("DepartmentId")}={P}Department AND {Col("EntityType")}={(int)UdfEntityType.Record} AND {Col("RecordDefinitionKey")}={P}Key AND {Col("RecordDefinitionVersion")}={P}Version";
		public Task<UdfDefinition> GetActiveAsync(int departmentId, string key, int version) => QueryFirstOrDefaultAsync<UdfDefinition>($"SELECT * FROM {Tbl("UdfDefinitions")} WHERE {Scope} AND {Col("IsActive")}={P}Active ORDER BY {Col("Version")} DESC", new { Department=departmentId, Key=key, Version=version, Active=true });
		public Task<UdfDefinition> GetScopedAsync(int departmentId, string definitionId, string key, int version) => QueryFirstOrDefaultAsync<UdfDefinition>($"SELECT * FROM {Tbl("UdfDefinitions")} WHERE {Scope} AND {Col("UdfDefinitionId")}={P}Id", new { Department=departmentId, Key=key, Version=version, Id=definitionId });
		public async Task DeactivateAsync(int departmentId, string key, int version, CancellationToken ct) => await ExecuteAsync($"UPDATE {Tbl("UdfDefinitions")} SET {Col("IsActive")}={P}Active WHERE {Scope}", new { Department=departmentId, Key=key, Version=version, Active=false }, ct);
		public Task LockDepartmentAsync(int departmentId, CancellationToken ct) => LockRecordsDepartmentAsync(departmentId, ct);
		public Task GuardRecordAsync(int departmentId, string recordId, CancellationToken ct) => LockLiveContentParentAsync(departmentId, recordId, ct);
		public async Task DeleteRecordValuesAsync(int departmentId, string recordId, CancellationToken ct) => await ExecuteAsync($"DELETE FROM {Tbl("UdfFieldValues")} WHERE {Col("EntityType")}={(int)UdfEntityType.Record} AND {Col("EntityId")}={P}Id AND {Col("UdfDefinitionId")} IN (SELECT {Col("UdfDefinitionId")} FROM {Tbl("UdfDefinitions")} WHERE {Col("DepartmentId")}={P}Department AND {Col("EntityType")}={(int)UdfEntityType.Record})", new {Department=departmentId,Id=recordId},ct);
	}
}
