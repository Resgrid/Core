namespace Resgrid.Model
{
	/// <summary>
	/// Discriminator for RunCardAvailabilitySelection rows. Mirrors CustomStateTypes
	/// (Personnel=1, Unit=2, Staffing=3) but ordered by how the engine consumes them.
	/// </summary>
	public enum RunCardSelectionTypes
	{
		UnitStatus = 1,
		PersonnelStatus = 2,
		PersonnelStaffing = 3
	}
}
