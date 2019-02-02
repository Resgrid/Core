namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class CallPriorityDataResult
	{
		public int Id { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Color { get; set; }
		public int Sort { get; set; }
		public bool IsDeleted { get; set; }
		public bool IsDefault { get; set; }
		public bool DispatchPersonnel { get; set; }
		public bool DispatchUnits { get; set; }
		public bool ForceNotifyAllPersonnel { get; set; }
		public int Tone { get; set; }
		public bool IsSystemPriority { get; set; }
	}
}
