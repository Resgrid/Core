namespace Resgrid.Model
{
	public enum CallEmailTypes
	{
		CalFireECC = 1,
		CarencroFire = 2,
		Resgrid = 3,
		GrandBlanc = 4,
		Generic = 5,
		LowestoftCoastGuard = 6,
		UnionFire = 7,
		ParklandCounty = 8,
		GenericPage = 9,
		Brockport = 10,
		HancockCounty = 11,
		CalFireSCU = 12,
		Connect = 13,
		SpottedDog = 14,
		PortJervis = 15,
		Yellowhead = 16,
		ParklandCounty2 = 17,
		FourPartPipe = 18,
		RandR = 19,
		Active911 = 20,
		OttawaCounty = 21,
		OttawaKingstonToronto = 22,
		/// <summary>AI dispatch (registry §4D). Enrich mode: the call is built by GenericTemplate and dispatched as today, then enriched off-thread by the aidispatchtriage worker.</summary>
		AI = 23
	}
}
