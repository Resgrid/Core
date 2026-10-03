namespace Resgrid.Model
{
	public enum EventingTypes
	{
		PersonnelStatusUpdated = 1,
		UnitStatusUpdated = 2,
		CallsUpdated = 3,
		PersonnelStaffingUpdated = 4,
		CallAdded = 5,
		CallClosed = 6,
		PersonnelLocationUpdated = 7,
		UnitLocationUpdated = 8,
		IncidentCommandUpdated = 9,
		ChatEvent = 10,
		ChecklistUpdated = 11,

		/// <summary>An event for one signed-in session only, such as an approval decision (passkey workbook section 7.4).</summary>
		SessionEvent = 12,

		/// <summary>A unit status timer acknowledgement changed; the item id is the unit id.</summary>
		UnitStatusAlertUpdated = 13
	}
}
