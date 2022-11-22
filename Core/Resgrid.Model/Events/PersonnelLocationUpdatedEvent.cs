namespace Resgrid.Model.Events
{
	public class PersonnelLocationUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string RecordId { get; set; }
	}
}
