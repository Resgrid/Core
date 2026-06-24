using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class IncidentCommandRepository : RepositoryBase<IncidentCommand>, IIncidentCommandRepository
	{
		public IncidentCommandRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class CommandStructureNodeRepository : RepositoryBase<CommandStructureNode>, ICommandStructureNodeRepository
	{
		public CommandStructureNodeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class ResourceAssignmentRepository : RepositoryBase<ResourceAssignment>, IResourceAssignmentRepository
	{
		public ResourceAssignmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class TacticalObjectiveRepository : RepositoryBase<TacticalObjective>, ITacticalObjectiveRepository
	{
		public TacticalObjectiveRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentTimerRepository : RepositoryBase<IncidentTimer>, IIncidentTimerRepository
	{
		public IncidentTimerRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentMapAnnotationRepository : RepositoryBase<IncidentMapAnnotation>, IIncidentMapAnnotationRepository
	{
		public IncidentMapAnnotationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class CommandLogEntryRepository : RepositoryBase<CommandLogEntry>, ICommandLogEntryRepository
	{
		public CommandLogEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class CommandTransferRepository : RepositoryBase<CommandTransfer>, ICommandTransferRepository
	{
		public CommandTransferRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}
}
