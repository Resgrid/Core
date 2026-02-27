namespace Resgrid.Model.Events
{
	public class TrainingAddedEvent
	{
		public int DepartmentId { get; set; }
		public Training Training { get; set; }
	}
}

