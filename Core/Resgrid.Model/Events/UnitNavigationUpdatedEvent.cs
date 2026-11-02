namespace Resgrid.Model.Events
{
	/// <summary>
	/// Raised when a navigating unit's ETA changes or its navigation ends. Fans out over the eventing topic as
	/// <c>unitNavigationUpdated</c> with the unit id and a small payload (unit, call or route ids, state) but no ETA: clients
	/// reload through Routing/GetActiveNavigations, which applies the viewer's call and unit-location visibility.
	/// </summary>
	public class UnitNavigationUpdatedEvent
	{
		public int DepartmentId { get; set; }

		public int UnitId { get; set; }

		public string Payload { get; set; }
	}
}
