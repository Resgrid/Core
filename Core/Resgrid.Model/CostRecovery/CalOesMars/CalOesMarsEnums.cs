namespace Resgrid.Model.CostRecovery.CalOesMars
{
	// Workforce & Business Operations plan, Phase C-M3 (C11): Cal OES Mutual Aid Reimbursement System (CFAA cost
	// recovery). Every value below is a local mirror of an external authority's vocabulary; the authority profile
	// (CalOesMarsAuthorityProfile) pins the mapping to the official material and a changed official list needs a new profile.

	/// <summary>What a resource-inventory (F-5) crosswalk row points at.</summary>
	public enum CalOesMarsSubjectTypes
	{
		Unit = 0,
		InventoryAsset = 1,
		External = 2
	}

	public enum CalOesMarsOwnerships
	{
		LocalAgency = 0,
		CalOes = 1,
		CalFire = 2,
		Private = 3,
		Rental = 4,
		Other = 5
	}

	/// <summary>Review state of a locally prepared profile row against what MARS shows.</summary>
	public enum CalOesMarsReviewStates
	{
		Draft = 0,
		Reviewed = 1,
		Observed = 2,
		Mismatch = 3
	}

	/// <summary>Annual submission a rate profile represents.</summary>
	public enum CalOesMarsSubmissionTypes
	{
		SalarySurvey = 0,
		AttachmentA = 1,
		AdministrativeRate = 2,
		RateLetter = 3,
		SpecialEquipment = 4
	}

	public enum CalOesMarsRateProfileStatuses
	{
		Draft = 0,
		Reviewed = 1,
		SignedLocally = 2,
		SubmittedExternal = 3,
		Accepted = 4,
		Superseded = 5
	}

	/// <summary>How the administrative (indirect) rate was chosen (CFAA administrative-rate instructions).</summary>
	public enum CalOesMarsAdministrativeRateMethods
	{
		None = 0,
		DeMinimis = 1,
		Calculated = 2
	}

	public enum CalOesMarsRateLineKinds
	{
		SalarySurvey = 0,
		AttachmentANonSuppression = 1,
		AdministrativeRate = 2,
		OfficialApparatus = 3,
		OfficialSupportVehicle = 4,
		PrivatelyOwnedVehicle = 5,
		Rental = 6,
		MealLodgingIncidentals = 7,
		SpecialEquipment = 8
	}

	public enum CalOesMarsRateBases
	{
		Hourly = 0,
		Daily = 1,
		PerMile = 2,
		Percent = 3,
		Flat = 4
	}

	public enum CalOesMarsRateAuthorities
	{
		AgencySubmitted = 0,
		CalOesBase = 1,
		CalOesRateLetter = 2,
		Fema = 3,
		AgencySpecialEquipment = 4
	}

	/// <summary>Indirect-cost rate proposal classification of an actual-cost input.</summary>
	public enum CalOesMarsCostClassifications
	{
		Direct = 0,
		Indirect = 1,
		Unallowable = 2
	}

	public enum CalOesMarsInputReviewStatuses
	{
		Pending = 0,
		Accepted = 1,
		Excluded = 2
	}

	public enum CalOesMarsDocumentKinds
	{
		Mou = 0,
		Moa = 1,
		Gbr = 2,
		Equivalent = 3
	}

	public enum CalOesMarsCompensationMethods
	{
		ActualHours = 0,
		PortalToPortal = 1
	}

	public enum CalOesMarsOvertimeMethods
	{
		None = 0,
		AfterEightHoursPerDay = 1,
		AfterTwelveHoursPerDay = 2,
		PerAgreement = 3
	}

	public enum CalOesMarsRecordTypes
	{
		AgencyProfile = 0,
		ResourceInventoryF5 = 1,
		SalarySurvey = 2,
		AdministrativeRate = 3,
		AttachmentA = 4,
		SpecialEquipment = 5,
		Agreement = 6,
		F42 = 7,
		ExpenseClaim = 8,
		GeneratedInvoice = 9
	}

	/// <summary>
	/// Local mirror state of a work item. Only an observed external action (recorded by a MARS manager) moves a
	/// record past ReadyForPortal; opening a handoff view or downloading a packet never does.
	/// </summary>
	public enum CalOesMarsLocalStates
	{
		Draft = 0,
		NeedsReview = 1,
		ReadyForPortal = 2,
		SubmittedExternal = 3,
		ReturnedForAgencyReview = 4,
		Approved = 5,
		DocumentationOnly = 6,
		PendingLocalAgencyApproval = 7,
		LocalAgencyRejected = 8,
		PendingPayingEntityApproval = 9,
		Paid = 10,
		Closed = 11
	}

	public enum CalOesMarsLineKinds
	{
		Personnel = 0,
		Apparatus = 1,
		SupportVehicle = 2,
		PovMileage = 3,
		Rental = 4,
		Expense = 5,
		SpecialEquipment = 6,
		Administrative = 7
	}

	public enum CalOesMarsEligibilityStates
	{
		Eligible = 0,
		Excluded = 1,
		Uncertain = 2
	}

	/// <summary>Where an external fact came from. P0 has only manual observation.</summary>
	public static class CalOesMarsObservationSources
	{
		public const string Manual = "manual";
		public const string Connector = "connector";
	}
}
