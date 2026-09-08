namespace Resgrid.Model
{
	/// <summary>
	/// Building construction classification for a contact pre-plan (Contacts plan Phase A, decision 28 / A8).
	/// Values follow the IBC/NFPA 220 construction types so each maps 1:1 onto the NERIS structure value set
	/// through the RMS-owned crosswalk; there is deliberately no combined bucket. Append only.
	/// </summary>
	public enum ContactPreplanConstructionTypes
	{
		Unknown = 0,
		TypeIFireResistive = 1,
		TypeIINonCombustible = 2,
		TypeIIIOrdinary = 3,
		TypeIVHeavyTimber = 4,
		TypeVWoodFrame = 5,
		ManufacturedOrMobile = 6,
		Other = 7
	}

	/// <summary>Roof form for a contact pre-plan. Append only.</summary>
	public enum ContactPreplanRoofTypes
	{
		Unknown = 0,
		Flat = 1,
		Gable = 2,
		Hip = 3,
		Gambrel = 4,
		Mansard = 5,
		Shed = 6,
		BowstringTruss = 7,
		LightweightTruss = 8,
		Other = 9
	}

	/// <summary>
	/// Occupancy classification for a contact pre-plan, following the IBC occupancy groups (with the
	/// residential group split the way NERIS location use distinguishes it). Append only.
	/// </summary>
	public enum ContactPreplanOccupancyTypes
	{
		Unknown = 0,
		ResidentialSingleFamily = 1,
		ResidentialMultiFamily = 2,
		ResidentialLodging = 3,
		ResidentialCareFacility = 4,
		Assembly = 5,
		Business = 6,
		Educational = 7,
		FactoryIndustrial = 8,
		HighHazard = 9,
		Institutional = 10,
		Mercantile = 11,
		Storage = 12,
		Utility = 13,
		Agricultural = 14,
		Vacant = 15,
		Other = 16
	}

	/// <summary>Premise hazard classification. Append only.</summary>
	public enum ContactPreplanHazardTypes
	{
		General = 0,
		Hazmat = 1,
		Structural = 2,
		Electrical = 3,
		Biological = 4,
		Animal = 5,
		Occupant = 6,
		Other = 7
	}

	/// <summary>Premise hazard severity, in ascending order.</summary>
	public enum ContactPreplanHazardSeverities
	{
		Info = 0,
		Caution = 1,
		Danger = 2
	}
}
