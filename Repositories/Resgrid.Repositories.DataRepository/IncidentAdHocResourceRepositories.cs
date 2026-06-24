using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class IncidentAdHocUnitRepository : RepositoryBase<IncidentAdHocUnit>, IIncidentAdHocUnitRepository
	{
		public IncidentAdHocUnitRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentAdHocPersonnelRepository : RepositoryBase<IncidentAdHocPersonnel>, IIncidentAdHocPersonnelRepository
	{
		public IncidentAdHocPersonnelRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}
}
