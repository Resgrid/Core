using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class CallTypesRepository : RepositoryBase<CallType>, ICallTypesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CallTypesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId)
		{
			//var query = $@"SELECT * FROM CallTypes 
			//				WHERE DepartmentId = @departmentId";

			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var results = await db.QueryAsync<CallType>(query, new { departmentId = departmentId });

			//	return results.ToList();
			//}

			return null;
		}
	}
}
