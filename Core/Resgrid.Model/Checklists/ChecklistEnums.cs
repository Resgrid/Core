namespace Resgrid.Model.Checklists
{
	public enum ChecklistCategory
	{
		StartOfShift = 0, UnitCheck = 1, PersonalGear = 2, AnnualReview = 3,
		Facility = 4, SafetyAudit = 5, EquipmentCheck = 6, Other = 7
	}

	public enum ChecklistItemType
	{
		PassFail = 0, YesNo = 1, Checkbox = 2, NumericReading = 3, Quantity = 4,
		FreeText = 5, SelectList = 6, DateValue = 7, Photo = 8, Signature = 9
	}

	public enum ChecklistTargetType
	{
		Department = 0, Unit = 1, Group = 2, Personnel = 3,
		// 4 is reserved for non-inventory equipment in the design contract.
		InventoryAsset = 5
	}

	public enum ChecklistScheduleFrequency
	{
		OnDemand = 0, PerShift = 1, Daily = 2, Weekly = 3, Monthly = 4,
		Quarterly = 5, SemiAnnual = 6, Annual = 7
	}
}
