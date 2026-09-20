namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (E2): protected workforce pay data, field costing and California
	// §12999 pay-data reporting. Every enum is routing metadata; the values that identify or price a person are
	// stored through the Advanced Data Protection seam (WorkforceProtectedFields, catalog 28).

	#region Employment (M0220)

	public enum CaliforniaPayDataCoverageStatuses
	{
		Unknown = 0,
		NotCovered = 1,
		CoveredPayroll = 2,
		CoveredLaborContractor = 3,
		CoveredBoth = 4
	}

	public enum WorkerKinds
	{
		PayrollEmployee = 0,
		LaborContractorEmployee = 1,
		Volunteer = 2,
		IndependentContractor = 3
	}

	public enum EmploymentTypes
	{
		Unknown = 0,
		FullTime = 1,
		PartTime = 2,
		Intermittent = 3
	}

	public enum ExemptionStatuses
	{
		Unknown = 0,
		Exempt = 1,
		NonExempt = 2
	}

	/// <summary>Why a worker counts as a California employee (E1: never inferred from a home address).</summary>
	public enum CaliforniaEmployeeBases
	{
		NotCalifornia = 0,
		AssignedToCaliforniaEstablishment = 1,
		WorksInCalifornia = 2,
		Both = 3
	}

	/// <summary>CRD remote-work classification; exactly one per employee snapshot.</summary>
	public enum WorkModes
	{
		NonRemote = 0,
		RemoteWithinCalifornia = 1,
		RemoteOutsideCaliforniaAssignedToCaliforniaEstablishment = 2
	}

	#endregion

	#region Compensation (M0221)

	public enum PayBases
	{
		Hourly = 0,
		Salary = 1,
		Daily = 2,
		Shift = 3,
		Stipend = 4
	}

	public enum PayCodes
	{
		Regular = 0,
		Overtime = 1,
		DoubleTime = 2,
		Standby = 3,
		Travel = 4,
		PaidLeave = 5,
		Other = 6
	}

	public enum PayComponentCategories
	{
		Specialty = 0,
		Incentive = 1,
		Longevity = 2,
		Education = 3,
		Ems = 4,
		HazMat = 5,
		Usar = 6,
		Differential = 7,
		Other = 8
	}

	public enum PayComponentBases
	{
		PerHour = 0,
		PercentOfBase = 1,
		PerShift = 2,
		PerPayPeriod = 3,
		FixedAnnual = 4
	}

	public enum CostComponentCategories
	{
		EmployerPayrollTax = 0,
		WorkersCompensation = 1,
		RetirementPension = 2,
		HealthBenefits = 3,
		OtherBenefits = 4,
		FixedLaborCost = 5,
		AllocatedOverhead = 6,
		Other = 7
	}

	public enum CostComponentBases
	{
		PercentOfEligiblePay = 0,
		PerHour = 1,
		PerShift = 2,
		PerDay = 3,
		FixedAnnual = 4
	}

	/// <summary>Which profile a compensation / cost row belongs to (E3 precedence: employee → role default → department default).</summary>
	public enum CompensationScopes
	{
		Employee = 0,
		RoleDefault = 1,
		DepartmentDefault = 2
	}

	public enum WorkHoursTypes
	{
		Regular = 0,
		Overtime = 1,
		DoubleTime = 2,
		Standby = 3,
		Travel = 4,
		PaidLeave = 5,
		Other = 6
	}

	public enum ExemptProxyMethods
	{
		None = 0,
		ActualPlusPaidLeave = 1,
		DaysTimesAverageHours = 2
	}

	public enum EarningsSources
	{
		W2Box5 = 0,
		W2Box1Fallback = 1,
		ClientAllocated = 2
	}

	#endregion

	#region Costing (M0222)

	public enum ResourceSubjectTypes
	{
		Unit = 0,
		InventoryAsset = 1,
		External = 2
	}

	public enum DepreciationMethods
	{
		StraightLine = 0
	}

	public enum AllocationBases
	{
		Mile = 0,
		Kilometer = 1,
		EngineHour = 2,
		OperatingHour = 3,
		Day = 4
	}

	public enum ResourceCostCategories
	{
		FuelEnergy = 0,
		Maintenance = 1,
		TiresWear = 2,
		Depreciation = 3,
		InsuranceLicensing = 4,
		LeaseRental = 5,
		Storage = 6,
		FixedOverhead = 7,
		Consumables = 8,
		Other = 9
	}

	public enum ResourceCostBases
	{
		PerMile = 0,
		PerKilometer = 1,
		PerEngineHour = 2,
		PerOperatingHour = 3,
		PerIdleHour = 4,
		PerDay = 5,
		PerDeployment = 6,
		FixedAnnual = 7
	}

	public enum ResourceCostSources
	{
		Manual = 0,
		Imported = 1,
		AcquisitionCalculated = 2,
		WorkOrderRollingActual = 3
	}

	public enum UsagePhases
	{
		Mobilization = 0,
		Standby = 1,
		Incident = 2,
		Return = 3
	}

	public enum UsageSources
	{
		Manual = 0,
		Dtr = 1,
		Gps = 2,
		HardwareTracker = 3,
		Import = 4
	}

	public enum FieldCostContextTypes
	{
		Bid = 0,
		Call = 1,
		Deployment = 2
	}

	public enum FieldCostRunTypes
	{
		Estimate = 0,
		Actual = 1
	}

	public enum FieldCostRunStatuses
	{
		Draft = 0,
		NeedsReview = 1,
		Frozen = 2,
		Superseded = 3
	}

	public enum RevenueSources
	{
		None = 0,
		BidEstimate = 1,
		CustomerInvoice = 2,
		CalOesMarsExpected = 3,
		CalOesMarsApproved = 4,
		CalOesMarsPaid = 5
	}

	public enum FieldCostCategories
	{
		Personnel = 0,
		Resource = 1,
		Consumable = 2,
		Expense = 3,
		Overhead = 4
	}

	#endregion

	#region Pay data reporting (M0223)

	public enum PayDataReportTypes
	{
		PayrollEmployee = 0,
		LaborContractorEmployee = 1
	}

	public enum PayDataReportRunStatuses
	{
		Draft = 0,
		Validated = 1,
		FrozenForExport = 2,
		Exported = 3,
		CertifiedExternally = 4,
		Correction = 5,
		Void = 6
	}

	public enum DemographicCollectionSources
	{
		SelfIdentified = 0,
		EmploymentRecord = 1,
		ReliableRecord = 2,
		ObserverPerception = 3
	}

	public enum PayDataExportFormats
	{
		Csv = 0,
		Xlsx = 1
	}

	#endregion
}
