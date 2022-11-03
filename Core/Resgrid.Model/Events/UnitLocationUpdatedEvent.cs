namespace Resgrid.Model.Events
{
	public class UnitLocationUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public string UnitId { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public string RecordId { get; set; }
	}
}
