namespace Resgrid.Model.IncidentLogistics
{
	// Incident Back Office persisted enums (large-incident-logistics-finance-plan.md rev 4.1). Stored as int columns; append-only,
	// never renumbered. Lifecycles are fixed service-layer state machines (plan §10 R1): departments label sub-states through
	// CustomState, they never add, remove or reorder these values. Unknown values fail closed.

	#region Activation and authority (LI-A, §7, §9.0)

	public enum IncidentActivationKinds { Incident = 0, Complex = 1, PlannedEvent = 2, EocActivation = 3 }

	public enum IncidentActivationStatuses { Preparing = 0, Active = 1, Demobilizing = 2, Closed = 3, Sealed = 4 }

	public enum IncidentAuthorityOwnerKinds { Profile = 0, Snapshot = 1 }

	public enum IncidentAuthorityDomains { ResourceOrdering = 0, Qualifications = 1, InventoryCache = 2, ProcurementFinance = 3, MutualAid = 4, IapRecords = 5 }

	public enum IncidentAuthorityModes { Resgrid = 0, External = 1, Shared = 2 }

	public enum IncidentConnectorStates { NotConfigured = 0, Staged = 1, Exported = 2, Acknowledged = 3, Rejected = 4, Reconciled = 5 }

	public enum IncidentReconciliationStatuses { Open = 0, Resolved = 1, Dismissed = 2 }

	#endregion

	#region Teams, periods, organization (LI-A, §9.1–§9.2)

	public enum IncidentTeamTypes { Type1Imt = 0, Type2Imt = 1, Type3Imt = 2, Type4Imt = 3, Type5Imt = 4, ComplexImt = 5, EocSectionRoster = 6, SupportTeam = 7, Custom = 8 }

	public enum IncidentTeamCandidateKinds { Primary = 0, Alternate = 1, Trainee = 2 }

	/// <summary>Computed roster readiness (plan §9.2) — never stored.</summary>
	public enum IncidentTeamReadiness { Ready = 0, Degraded = 1, NotDeployable = 2 }

	/// <summary>Computed slot readiness (plan §9.2) — never stored.</summary>
	public enum IncidentSlotReadiness { Qualified = 0, QualifiedAsTrainee = 1, ExpiringBeforeWindowEnd = 2, MissingRequirement = 3, NoCandidate = 4 }

	public enum IncidentPeriodStatuses { Planned = 0, Current = 1, Closed = 2 }

	public enum IncidentPlanVersionStatuses { Draft = 0, Assembled = 1, Approved = 2, Distributed = 3, Superseded = 4 }

	public enum IncidentObjectiveStatuses { Open = 0, Met = 1, Carried = 2, Dropped = 3 }

	public enum IncidentSections { Command = 0, Operations = 1, Planning = 2, Logistics = 3, FinanceAdministration = 4, Coordination = 5, Other = 6 }

	public enum IncidentPositionSources { Generic = 0, Nwcg = 1, Nims = 2, Ciccs = 3, Department = 4, Incident = 5 }

	public enum IncidentOrganizationNodeKinds { Section = 0, Branch = 1, Division = 2, Group = 3, Unit = 4, Custom = 5 }

	public enum IncidentAssignmentSubjectTypes { User = 0, Participant = 1, Placeholder = 2 }

	public enum IncidentAssignmentKinds { Primary = 0, Acting = 1, Deputy = 2, Assistant = 3, Trainee = 4, Relief = 5 }

	public enum IncidentShiftPatterns { Either = 0, Day = 1, Night = 2 }

	/// <summary>The qualification state an assignment was made under (trigger 118 <c>assignment.qualification_state</c>).</summary>
	public enum IncidentQualificationStates { Qualified = 0, Trainee = 1, Exception = 2, Waived = 3, NotRequired = 4 }

	#endregion

	#region Participants and resources (LI-A/LI-B, §9.3–§9.5)

	public enum IncidentPaymentClasses { Employee = 0, CasualAd = 1, Cooperator = 2, MutualAid = 3, Contractor = 4, Volunteer = 5, Donated = 6, NonReimbursable = 7 }

	/// <summary>Support status (plan §9.5): owned here, never merged with IC/Dispatch operational status.</summary>
	public enum IncidentSupportStatuses { Ordered = 0, Mobilizing = 1, EnRoute = 2, Arrived = 3, Supported = 4, PendingDemob = 5, Released = 6, Returned = 7, Closed = 8 }

	public enum IncidentEnrollmentSources { Desk = 0, Import = 1, EnrollmentQueue = 2, AutoDraft = 3, PreRegistration = 4, AdHocEnrichment = 5, TeamActivation = 6 }

	/// <summary>Department enrollment policy (setting 86, plan §9.5).</summary>
	public enum IncidentEnrollmentPolicies { Off = 0, Suggest = 1, AutoDraft = 2 }

	public enum IncidentQualificationSources { PersonnelCertification = 0, Import = 1, Keyed = 2 }

	public enum IncidentVerificationStates { Unverified = 0, Verified = 1, Expired = 2, Rejected = 3 }

	public enum IncidentOrganizationRoles { Host = 0, Responding = 1, Cooperator = 2, Vendor = 3, Other = 4 }

	public enum IncidentResourceKinds { Personnel = 0, Crew = 1, Team = 2, Equipment = 3, Aircraft = 4, Supply = 5, Service = 6, Facility = 7, MissionPackage = 8, Overhead = 9 }

	public enum IncidentSubjectTypes { Participant = 0, Resource = 1, Facility = 2, User = 3 }

	public enum IncidentMovementKinds { Mobilize = 0, Reassign = 1, Transfer = 2, Demobilize = 3 }

	public enum IncidentTimeoutBases { None = 0, RulePack = 1, Agreement = 2, OrderTerms = 3, TentativeRelease = 4 }

	public enum IncidentEstimateCategories { Personnel = 0, Equipment = 1, Travel = 2, Lodging = 3, Meals = 4, Commodities = 5, Maintenance = 6, Space = 7, Rotation = 8, Other = 9 }

	public enum IncidentCheckInSources { Desk = 0, Import = 1, PreRegistration = 2, Scan = 3, SelfService = 4, Enrollment = 5 }

	public enum IncidentDemobStatuses { Tentative = 0, Approved = 1, Released = 2, Departed = 3, Closed = 4, Cancelled = 5 }

	public enum IncidentDemobCategories { RoleHandoff = 0, Property = 1, Vehicle = 2, Time = 3, ReceiptClaim = 4, Lodging = 5, Travel = 6, Evaluation = 7, OpenTasks = 8, Documents = 9, Other = 10 }

	public enum IncidentGateItemStatuses { Open = 0, Complete = 1, Waived = 2 }

	#endregion

	#region Requests and fills (LI-B, §9.4)

	public enum IncidentRequestPriorities { Routine = 0, Priority = 1, Urgent = 2, Critical = 3 }

	/// <summary>The fixed request lifecycle (plan §9.4). Rejected and Cancelled are terminal branches with reason and authority.</summary>
	public enum IncidentRequestStatuses
	{
		Draft = 0, Submitted = 1, Approved = 2, Ordered = 3, PartiallyFilled = 4, Filled = 5, EnRoute = 6, Received = 7, Assigned = 8,
		Released = 9, Demobilized = 10, Closed = 11, Rejected = 12, Cancelled = 13
	}

	public enum IncidentSourceChannels { LocalInventory = 0, Department = 1, MutualAid = 2, Vendor = 3, Cache = 4, State = 5, Federal = 6, Emac = 7, Donation = 8, ManualExternal = 9 }

	public enum IncidentApprovalDecisions { Approved = 0, Rejected = 1, Returned = 2 }

	public enum IncidentEtaConfidences { Unknown = 0, Low = 1, Medium = 2, High = 3, Confirmed = 4 }

	public enum IncidentFillStatuses { Offered = 0, Accepted = 1, EnRoute = 2, Arrived = 3, Cancelled = 4 }

	public enum IncidentReceiptStatuses { Received = 0, Accepted = 1, Rejected = 2, Partial = 3 }

	public enum IncidentDiscrepancyKinds { None = 0, Shortage = 1, Overage = 2, Damage = 3, WrongItem = 4, Rejected = 5 }

	#endregion

	#region Facilities, lodging, feeding, water (LI-B, §9.6–§9.8)

	public enum IncidentFacilityTypes
	{
		Icp = 0, Base = 1, Camp = 2, SpikeCamp = 3, Helibase = 4, Helispot = 5, Airbase = 6, StagingArea = 7, Warehouse = 8, ReceivingPoint = 9,
		FeedingSite = 10, ShowerSite = 11, MedicalUnit = 12, VehicleYard = 13, Hotel = 14, Office = 15, Custom = 16
	}

	public enum IncidentFacilityStatuses { Planned = 0, Active = 1, Closed = 2 }

	public enum IncidentFacilityServiceKinds
	{
		People = 0, SleepingPositions = 1, Beds = 2, Rooms = 3, Parking = 4, ShowersPerHour = 5, Toilets = 6, Handwash = 7, MealsPerService = 8,
		PotableWater = 9, NonPotableWater = 10, Fuel = 11, Power = 12, Network = 13, RadioCoverage = 14, Refrigeration = 15, Waste = 16, Laundry = 17,
		OfficeSeats = 18, Printers = 19, ReceivingSpace = 20, AircraftParking = 21, RetardantLoading = 22, Custom = 23
	}

	public enum IncidentFacilityAgreementKinds { LandUse = 0, Lease = 1, DirectBill = 2, Other = 3 }

	public enum IncidentServiceReadiness { Planned = 0, Ready = 1, Degraded = 2, OutOfService = 3 }

	public enum IncidentLodgingBlockStatuses { Requested = 0, Held = 1, Confirmed = 2, Released = 3 }

	public enum IncidentLodgingUnitTypes { HotelRoom = 0, Tent = 1, Bunkhouse = 2, Cot = 3, Other = 4 }

	public enum IncidentLodgingSubjectTypes { Participant = 0, Resource = 1, Reserved = 2 }

	/// <summary>NoShow (3) and Cancelled (5) free their bed-nights; every other status holds them (plan §11.5).</summary>
	public enum IncidentLodgingAssignmentStatuses { Held = 0, Confirmed = 1, Occupied = 2, NoShow = 3, Departed = 4, Cancelled = 5 }

	public enum IncidentRoomingListStatuses { Draft = 0, Sent = 1, Confirmed = 2 }

	public enum IncidentMealTypes { Breakfast = 0, Lunch = 1, Dinner = 2, SackLunch = 3, Remote = 4, Mre = 5, Snack = 6, Custom = 7 }

	public enum IncidentMealDeliveryModes { Served = 0, Delivered = 1, Voucher = 2, Remote = 3 }

	public enum IncidentMealPlanStatuses { Planned = 0, Active = 1, Closed = 2, Cancelled = 3 }

	public enum IncidentOrderStatuses { Draft = 0, Placed = 1, Confirmed = 2, Delivered = 3, Cancelled = 4 }

	public enum IncidentWaterKinds { Bulk = 0, Bottled = 1, NonPotable = 2 }

	public enum IncidentCoefficientKinds { WaterDrinking = 0, WaterFoodPrep = 1, WaterMedical = 2, WaterSanitation = 3, WaterShower = 4, WaterContingency = 5, MealContingency = 6 }

	#endregion

	#region Services, ground support, communications (LI-B, §9.9–§9.11, §9.15)

	public enum IncidentServiceCategories
	{
		Radio = 0, PrintCopy = 1, It = 2, Facilities = 3, Sanitation = 4, Shower = 5, Laundry = 6, Waste = 7, Power = 8, Internet = 9, Fuel = 10,
		VehicleRepair = 11, Security = 12, Signage = 13, OfficeSupply = 14, Medical = 15, Custom = 16
	}

	public enum IncidentServiceRequestStatuses { Open = 0, Assigned = 1, InProgress = 2, OnHold = 3, Completed = 4, Cancelled = 5 }

	public enum IncidentServiceActivityKinds { Note = 0, StatusChange = 1, Labor = 2, Parts = 3, Cost = 4 }

	public enum IncidentServiceAssetKinds { Copier = 0, Printer = 1, Radio = 2, Generator = 3, Shower = 4, Toilet = 5, Network = 6, Vehicle = 7, Other = 8 }

	public enum IncidentTransportKinds { Shuttle = 0, CrewTransport = 1, Cargo = 2, Travel = 3, Fuel = 4 }

	public enum IncidentTransportStatuses { Requested = 0, Scheduled = 1, EnRoute = 2, Arrived = 3, Completed = 4, Cancelled = 5 }

	public enum IncidentTransportExceptions { None = 0, Delay = 1, Breakdown = 2, Accident = 3, Spill = 4, Discrepancy = 5, Other = 6 }

	public enum IncidentTravelDirections { Mobilize = 0, Demobilize = 1 }

	public enum IncidentFuelTypes { Diesel = 0, Gasoline = 1, JetA = 2, Avgas = 3, Propane = 4, Other = 5 }

	public enum IncidentFuelTransactionKinds { Delivery = 0, Issue = 1, Reconciliation = 2, Spill = 3 }

	public enum IncidentCommsItemKinds { Radio = 0, Kit = 1, Battery = 2, Charger = 3, Accessory = 4, Phone = 5, Satellite = 6, Other = 7 }

	public enum IncidentCustodyStatuses { Issued = 0, Returned = 1, Lost = 2, Damaged = 3, InRepair = 4 }

	#endregion

	#region Vendors and self-service (LI-B, §9.12, §9.28)

	public enum IncidentCatalogStatuses { PendingReview = 0, Active = 1, Suspended = 2, Expired = 3 }

	public enum IncidentSupportCategories
	{
		Lodging = 0, Feeding = 1, Water = 2, Ice = 3, Transport = 4, Fuel = 5, Sanitation = 6, Showers = 7, Laundry = 8, Waste = 9, Power = 10, It = 11,
		Radio = 12, Printing = 13, OfficeSupply = 14, Warehouse = 15, Medical = 16, Security = 17, Towing = 18, Facility = 19, Equipment = 20, Custom = 21
	}

	public enum IncidentVendorRanks { Primary = 0, Alternate = 1, Contingency = 2 }

	public enum IncidentAvailabilityKinds { Available = 0, Blackout = 1 }

	public enum IncidentComplianceDocumentKinds { Insurance = 0, License = 1, FoodSafety = 2, WaterSafety = 3, VehicleSafety = 4, TaxForm = 5, Agreement = 6, Other = 7 }

	/// <summary>Vendor engagement lifecycle (plan §9.12.3), distinct from organization-level eligibility.</summary>
	public enum IncidentEngagementStatuses { Available = 0, Contacted = 1, QuoteRequested = 2, Held = 3, Engaged = 4, Active = 5, AtCapacity = 6, Suspended = 7, Released = 8, Closed = 9 }

	public enum IncidentCommitmentKinds { Hold = 0, Commitment = 1 }

	public enum IncidentHoldStatuses { Active = 0, Released = 1, Expired = 2, Converted = 3, Consumed = 4 }

	public enum IncidentQuoteStatuses { Requested = 0, Received = 1, Accepted = 2, Declined = 3 }

	public enum IncidentSelfServiceSubjects { Participant = 0, VendorEngagement = 1 }

	/// <summary>Purpose flags of a self-service link (plan §9.28).</summary>
	[System.Flags]
	public enum IncidentSelfServicePurposes
	{
		None = 0, View = 1, ConfirmArrival = 2, Lodging = 4, Travel = 8, ServiceTicket = 16, EmergencyContact = 32, HoldResponse = 64, Upload = 128, Availability = 256
	}

	#endregion

	#region Procurement, budget, time, cost (LI-C, §9.12.2–§9.13, §9.23)

	public enum IncidentAgreementTypes
	{
		Preseason = 0, Bpa = 1, Eera = 2, Emergency = 3, LandUse = 4, HotelDirectBill = 5, Catering = 6, Transport = 7, PurchaseOrder = 8,
		PurchaseCard = 9, Reimbursement = 10, MutualAid = 11, DonatedInKind = 12, Other = 13
	}

	public enum IncidentPurchaseMethods { PurchaseOrder = 0, AgreementCall = 1, PurchaseCard = 2, Emergency = 3, MicroPurchase = 4, ConvenienceCheck = 5, Rental = 6, Unique = 7, ExistingAgreement = 8 }

	/// <summary>Requisition → approval → sourcing → commitment (plan §9.12.3).</summary>
	public enum IncidentRequisitionStatuses { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3, Sourcing = 4, Committed = 5, Cancelled = 6, Closed = 7 }

	public enum IncidentCommitmentStatuses { Open = 0, PartiallyReceived = 1, Received = 2, Closed = 3, Void = 4 }

	public enum IncidentInvoiceStatuses { Received = 0, Matched = 1, Exception = 2, Accepted = 3, Rejected = 4, Packaged = 5, Paid = 6 }

	public enum IncidentInvoiceExceptionKinds { None = 0, MatchVariance = 1, Duplicate = 2, Unmatched = 3, Aged = 4 }

	public enum IncidentPaymentPackageStatuses { Assembled = 0, Submitted = 1, Accepted = 2, Rejected = 3, Paid = 4 }

	public enum IncidentBudgetStatuses { Draft = 0, Authorized = 1, Closed = 2 }

	public enum IncidentBudgetLineKinds { Section = 0, Branch = 1, Facility = 2, Function = 3, Vendor = 4, CostGroup = 5, Period = 6, General = 7 }

	public enum IncidentFundingAuthorizationKinds { Initial = 0, Revision = 1, Transfer = 2 }

	public enum IncidentSpendingActions { Request = 0, Approve = 1, Commit = 2, Receive = 3, Accept = 4, Close = 5 }

	public enum IncidentFundingCodeKinds { FireCode = 0, FemaDisaster = 1, PCode = 2, Project = 3, Agency = 4, Other = 5 }

	public enum IncidentTimeSubjectTypes { Participant = 0, Resource = 1, Equipment = 2 }

	public enum IncidentTimeEntryTypes { Work = 0, Travel = 1, Standby = 2, Rest = 3, Hazard = 4, Premium = 5 }

	/// <summary>Time and equipment-use lifecycle (plan §9.13): Certified and Approved rows may only transition to Void.</summary>
	public enum IncidentTimeStatuses { Draft = 0, Submitted = 1, Certified = 2, Approved = 3, Adjusted = 4, Void = 5 }

	public enum IncidentExpenseCategories { Travel = 0, Lodging = 1, MealsPerDiem = 2, Fuel = 3, Supplies = 4, Repair = 5, Freight = 6, Laundry = 7, Communications = 8, Other = 9 }

	public enum IncidentExpenseStatuses { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3, Void = 4 }

	public enum IncidentRateSubjectKinds { Person = 0, Equipment = 1, Vendor = 2, Contract = 3, Lodging = 4, Meal = 5 }

	/// <summary>Financial measures (plan §13.4) — never blended into one number.</summary>
	public enum IncidentCostMeasures { Estimated = 0, Reserved = 1, Committed = 2, Accrued = 3, Actual = 4, Invoiced = 5, Posted = 6 }

	public enum IncidentCostCategories { Personnel = 0, Equipment = 1, Lodging = 2, Meals = 3, Water = 4, Transport = 5, Supply = 6, Services = 7, Contract = 8, Expense = 9, Aviation = 10, Other = 11 }

	public enum IncidentCostShareBases { Acreage = 0, ProtectionResponsibility = 1, ResourceUse = 2, NegotiatedFixedShare = 3, DailyNegotiated = 4 }

	public enum IncidentCostShareStatuses { Draft = 0, Active = 1, Superseded = 2 }

	public enum IncidentCostShareScopes { All = 0, CostCategory = 1, Section = 2, FundingCode = 3, Facility = 4, ResourceClass = 5, Period = 6 }

	public enum IncidentCostShareMethods { Percentage = 0, ActualUse = 1, PerCapita = 2, DirectCharge = 3, Excluded = 4 }

	public enum IncidentApportionmentRunKinds { Scheduled = 0, Reapportionment = 1 }

	#endregion

	#region Claims, closeout, recovery, intake (LI-C, §9.14, §9.16, §9.26, §9.27)

	public enum IncidentClaimKinds { Compensation = 0, Property = 1, Vehicle = 2, Equipment = 3 }

	public enum IncidentClaimStatuses { Open = 0, UnderReview = 1, Approved = 2, Denied = 3, Settled = 4, Closed = 5 }

	public enum IncidentCloseoutCategories { OpenCommitments = 0, Claims = 1, Time = 2, Property = 3, Records = 4, Lodging = 5, Invoices = 6, Other = 7 }

	public enum IncidentRecoveryPrograms { FemaPublicAssistance = 0, StateProgram = 1, Other = 2 }

	public enum IncidentDeclarationStatuses { Draft = 0, Open = 1, Closed = 2 }

	public enum IncidentRecoveryObligationKinds { RequestForAssistance = 0, DamageInventory = 1, ProjectCompletion = 2, ExtensionExpiry = 3, Other = 4 }

	public enum IncidentIntakeChannels { Email = 0, Upload = 1, VendorPortal = 2, SelfService = 3 }

	public enum IncidentIntakeSenderKinds { Unknown = 0, Participant = 1, Vendor = 2, Member = 3, DepartmentList = 4 }

	public enum IncidentIntakeStatuses { Unfiled = 0, Filed = 1, Rejected = 2, Quarantined = 3 }

	#endregion

	#region Intelligence and routing (LI-A, §9.17, §13.4, §13.8)

	public enum IncidentSnapshotKinds { Incremental = 0, Rebuild = 1 }

	public enum IncidentRoutingFallbacks { None = 0, SectionChief = 1, DepartmentAdministrators = 2, NoRecipient = 3 }

	#endregion

	#region Coordination (LI-D, §9.25)

	public enum CoordinationBoardScopes { Department = 0, Activation = 1 }

	public enum IncidentLifelines { SafetySecurity = 0, FoodHydrationShelter = 1, HealthMedical = 2, Energy = 3, Communications = 4, Transportation = 5, HazardousMaterials = 6, Water = 7 }

	public enum IncidentLifelineColors { Unknown = 0, Green = 1, Yellow = 2, Red = 3, Grey = 4 }

	public enum IncidentOpenActionStatuses { Open = 0, InProgress = 1, Done = 2, Cancelled = 3 }

	public enum IncidentDecisionStatuses { Open = 0, Decided = 1, Withdrawn = 2 }

	public enum IncidentSituationalFeatureTypes { RoadClosure = 0, Shelter = 1, Healthcare = 2, Infrastructure = 3, EvacuationZone = 4 }

	public enum IncidentSituationalStatuses { Unknown = 0, Open = 1, Closed = 2, Limited = 3, Full = 4, Cleared = 5 }

	#endregion
}
