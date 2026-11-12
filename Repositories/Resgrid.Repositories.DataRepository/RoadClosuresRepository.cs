using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Road closures (M0314).</summary>
	public class RoadClosuresRepository : RmsRepositoryBase<RoadClosure>, IRoadClosuresRepository
	{
		public RoadClosuresRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<RoadClosure>> GetForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<RoadClosure>(
				$"SELECT * FROM {Tbl("RoadClosures")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("CreatedOn")} DESC",
				new { DepartmentId = departmentId });

			return rows.ToList();
		}

		public Task<RoadClosure> GetByIdForDepartmentAsync(string roadClosureId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<RoadClosure>(
				$"SELECT * FROM {Tbl("RoadClosures")} WHERE {Col("RoadClosureId")} = {P}RoadClosureId AND {Col("DepartmentId")} = {P}DepartmentId",
				new { RoadClosureId = roadClosureId, DepartmentId = departmentId });
		}
	}
}
