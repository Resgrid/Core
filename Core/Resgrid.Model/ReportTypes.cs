namespace Resgrid.Model
{
	public enum ReportTypes
	{
		Staffing = 0,
		Personnel = 1,
		Certifications = 2,
		ShiftReadiness = 3,
		ChecklistCompliance = 4,
		ChecklistMissed = 5,
		InventoryOnHand = 6,
		InventoryUsage = 7,
		InventoryExpiration = 8,
		InventoryLowStock = 9,
		InventoryTransferHistory = 10,
		InventoryIssuance = 11,
		InventoryValuation = 12,
		ControlledSubstanceLog = 13,
		/// <summary>Workforce &amp; Business Operations plan Phase D (registry 14): person × type and unit × type certification compliance.</summary>
		CertificationCompliance = 14,
		/// <summary>Incident Back Office (registry 15, plan §9.29): the scheduled situation report of each open activation.</summary>
		IncidentSituationReport = 15,
		/// <summary>Incident Back Office (registry 16, plan §9.29): the daily cost report of each open activation (LI-C).</summary>
		IncidentDailyCost = 16
	}
}
