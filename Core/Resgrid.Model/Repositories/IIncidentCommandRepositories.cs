namespace Resgrid.Model.Repositories
{
	public interface IIncidentCommandRepository : IRepository<IncidentCommand>
	{
	}

	public interface ICommandStructureNodeRepository : IRepository<CommandStructureNode>
	{
	}

	public interface IResourceAssignmentRepository : IRepository<ResourceAssignment>
	{
	}

	public interface ITacticalObjectiveRepository : IRepository<TacticalObjective>
	{
	}

	public interface IIncidentTimerRepository : IRepository<IncidentTimer>
	{
	}

	public interface IIncidentMapAnnotationRepository : IRepository<IncidentMapAnnotation>
	{
	}

	public interface ICommandLogEntryRepository : IRepository<CommandLogEntry>
	{
	}

	public interface ICommandTransferRepository : IRepository<CommandTransfer>
	{
	}
}
