using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitStatusAlertAcknowledgementsRepository : RmsRepositoryBase<UnitStatusAlertAcknowledgement>, IUnitStatusAlertAcknowledgementsRepository
	{
		public UnitStatusAlertAcknowledgementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<UnitStatusAlertAcknowledgement>> GetActiveForUnitStatesAsync(int departmentId, IEnumerable<int> unitStateIds) =>
			QueryAsync<UnitStatusAlertAcknowledgement>($"SELECT * FROM {Tbl("UnitStatusAlertAcknowledgements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("UnitStateId", "UnitStateIds")} AND {Col("ClearedOn")} IS NULL ORDER BY {Col("AcknowledgedOn")}",
				new { DepartmentId = departmentId, UnitStateIds = InListValue(unitStateIds) });

		public Task<IEnumerable<UnitStatusAlertAcknowledgement>> GetActiveForUnitStateAsync(int departmentId, int unitId, int unitStateId) =>
			QueryAsync<UnitStatusAlertAcknowledgement>($"SELECT * FROM {Tbl("UnitStatusAlertAcknowledgements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UnitId")} = {P}UnitId AND {Col("UnitStateId")} = {P}UnitStateId AND {Col("ClearedOn")} IS NULL ORDER BY {Col("AcknowledgedOn")}",
				new { DepartmentId = departmentId, UnitId = unitId, UnitStateId = unitStateId });
	}
}
