namespace Resgrid.Model.Events
{
	/// <summary>
	/// Raised when the members seated in a unit's active roles change (staffing saved, seats cleared, unit deleted). Fans
	/// out over the eventing topic as <c>unitActiveRolesUpdated</c>, so a map following a unit's crew reloads who rides it.
	/// A <see cref="UnitId"/> of 0 means several units changed at once (the department staffing page).
	/// </summary>
	public class UnitActiveRolesUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int UnitId { get; set; }
	}
}
