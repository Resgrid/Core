namespace Resgrid.Model.Repositories
{
	public interface IIncidentCommandRepository : IRepository<IncidentCommand>
	{
		System.Threading.Tasks.Task<IncidentCommand> GetByPublicShareTokenAsync(string publicShareToken);
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

	public interface IIncidentNoteRepository : IRepository<IncidentNote>
	{
	}

	public interface IIncidentAttachmentRepository : IRepository<IncidentAttachment>
	{
		System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<IncidentAttachment>> GetAllMetadataByDepartmentIdAsync(int departmentId);
	}
}
