namespace Resgrid.Model
{
	public enum DepartmentSettingTypes
	{
		BigBoardMapZoomLevel = 1,
		BigBoardPageRefresh = 2,
		BigBoardMapCenterAddress = 3,
		BigBoardHideUnavailable = 4,
		BigBoardMapCenterGpsCoordinates = 5,
		RssFeedKeyForActiveCalls = 6,
		StripeCustomerId = 7,
		TestEnabled = 8,
		DisabledAutoAvailable = 9,
		TextToCallNumber = 10,
		TextToCallImportFormat = 11,
		TextToCallSourceNumbers = 12,
		EnableTextToCall = 13,
		EnableTextCommand = 14,
		InternalDispatchEmail = 15,
		UpdateTimestamp = 16,
		BrainTreeCustomerId = 17,
		PersonnelSortOrder = 18,
		UnitsSortOrder = 19,
		CallsSortOrder = 20,
		PersonnelListStatusSortOrder = 21,
		DispatchShiftInsteadOfGroup = 22,
		AutoSetStatusForShiftDispatchPersonnel = 23,
		ShiftCallDispatchPersonnelStatusToSet = 24,
		ShiftCallReleasePersonnelStatusToSet = 25,
		AllowSignupsForMultipleShiftGroups = 26,
		StaffingSuppressStaffingLevels = 27,
		MappingPersonnelLocationTTL = 28,
		MappingUnitLocationTTL = 29,
		MappingPersonnelAllowStatusWithNoLocationToOverwrite = 30,
		MappingUnitAllowStatusWithNoLocationToOverwrite = 31,
		ModuleSettings = 32,
		UnitDispatchAlsoDispatchToAssignedPersonnel = 33,
		UnitDispatchAlsoDispatchToGroup = 34,
		PersonnelOnUnitSetUnitStatus = 35,
		Require2FAForAdmins = 36,
		PaddleCustomerId = 37,
		CheckInTimersAutoEnableForNewCalls = 38,
		WeatherAlertsEnabled = 39,
		WeatherAlertMinimumSeverity = 40,
		WeatherAlertAutoMessageSeverity = 41,
		WeatherAlertCallIntegration = 42,
		WeatherAlertCacheMinutes = 43,
		WeatherAlertAutoMessageSchedule = 44,
		WeatherAlertExcludedEvents = 45,
		MappingUseMapboxOverride = 46,
		MappingMapboxStyleUrl = 47,
		MappingMapboxAccessToken = 48,
		TtsLanguage = 49,
		UnitCallDispatchStatusToSet = 50,
		UnitCallReleaseStatusToSet = 51,
		UnitCallStatusOverridesByUnitType = 52,
		EnableModernNotifications = 53,
		ForceChatbotSecurityPin = 54,
		HardwareTrackingStaleAfterSeconds = 55,
		HardwareTrackingMobileFallbackEnabled = 56,
		HardwareTrackingLocationRetentionDays = 57,
		DispatchRecommendationMode = 58,
		DispatchRecommendationAutoDispatch = 59,
		DispatchRecommendationConfig = 60,

		/// <summary>
		/// ProtoBuf-serialized <see cref="NewCallFieldPolicy"/>: which built-in new-call fields a
		/// department shows, and which it requires before a call can be created.
		/// </summary>
		NewCallFieldPolicy = 61,

		/// <summary>
		/// ProtoBuf-serialized <see cref="UnitStatusThresholds"/>: how long a unit may sit in a status
		/// before the board highlights it.
		/// </summary>
		UnitStatusThresholds = 62,

		/// <summary>
		/// When enabled, department and group administrators cannot choose a member's new password.
		/// Their reset action sends the member the hardened, single-use password recovery link instead.
		/// </summary>
		RequirePasswordResetViaEmail = 63,

		// -- Records (RMS) block 70-77 -- Identifier Allocation Registry section 3.4. 64-69 is the
		// cross-plan buffer and must not be taken here. All are edited from the Records Settings screen
		// (RMS plan section 4.9); RecordsActivatedOn is deliberately NOT a setting (RmsDepartmentCutover).

		/// <summary>Cached scalar: default <see cref="RmsLifecyclePreset"/> for new department-owned definitions (locked definitions keep their own).</summary>
		RecordsDefaultLifecyclePreset = 70,

		/// <summary>Cached scalar: review-due target in hours; a per-definition override wins.</summary>
		RecordsReviewDueHours = 71,

		/// <summary>ProtoBuf-serialized <see cref="RecordsNumberingConfig"/>: department-wide numbering defaults applied when a definition declares none.</summary>
		RecordsNumberingConfig = 72,

		/// <summary>ProtoBuf-serialized <see cref="RecordsSearchConfig"/>: index scope and the protected degrade mode.</summary>
		RecordsSearchConfig = 73,

		/// <summary>ProtoBuf-serialized <see cref="RecordsRetentionPolicy"/>: department default years plus per-definition overrides (0 = permanent).</summary>
		RecordsRetentionPolicy = 74,

		/// <summary>Cached scalar <see cref="RecordsGroupVisibilityMode"/>: DepartmentWide (0, default) or GroupScoped (1). v1 is on/off only.</summary>
		RecordsGroupVisibilityMode = 75,

		/// <summary>ProtoBuf: RESERVED, unused in v1. Becomes the per-anchor group-scope toggle later without a new value.</summary>
		RecordsGroupScopeConfig = 76,

		/// <summary>ProtoBuf-serialized <see cref="RecordsDisclosureConfig"/>: public-records statutory clock, default redaction profile, release approver. RMS-3.</summary>
		RecordsDisclosureConfig = 77,

		/// <summary>
		/// ProtoBuf-serialized <see cref="GroupDispatchScopeConfig"/>: whether dispatch views are scoped to the
		/// user's group subtree, and which personnel roles stay department-wide while it is on.
		/// </summary>
		GroupDispatchScopeConfig = 78,

		/// <summary>Declared, reviewed administrative operating profile; registry section 4G.</summary>
		DepartmentOperatingProfile = 110,

		/// <summary>
		/// Cached scalar, Records Settings screen: "false" turns off this department's NERIS workflows (guided NERIS
		/// sections, NERIS validation, submission and the NERIS setup screens). A missing row means on. The rest of
		/// Records is untouched, and the NERIS profile, crosswalks and submission history are kept for re-enabling.
		/// </summary>
		RecordsNerisWorkflowsEnabled = 111,

		/// <summary>
		/// Cached scalar <see cref="MapStyleTypes"/>: the Mapbox base map every map surface (website and all
		/// apps) shows in a light theme. Missing = Automatic (Streets). Edited on the Mapping Settings screen.
		/// </summary>
		MappingMapStyle = 112,

		/// <summary>
		/// Cached scalar <see cref="MapStyleTypes"/>: the base map the apps show in a dark theme. Missing =
		/// Automatic, which pairs with <see cref="MappingMapStyle"/> (see <see cref="MapStylePresets.ResolveNightStyle"/>).
		/// </summary>
		MappingMapStyleNight = 113,

		/// <summary>
		/// Cached scalar, Dispatch Settings screen: "true" makes the Unit and Responder apps set a status with a
		/// two-second press and hold instead of a tap followed by Next/Submit. Missing = false (tap).
		/// </summary>
		StatusHoldToConfirm = 114,

		/// <summary>
		/// ProtoBuf-serialized <see cref="CallNumberingConfig"/>: the department's call number pattern and sequence digits,
		/// edited on the Call Settings screen. Missing = the legacy "26-153" numbers. The sequences themselves are counted
		/// in CallNumberSequences.
		/// </summary>
		CallNumberingConfig = 115,

		/// <summary>
		/// Cached scalar, Dispatch Settings screen: "true" closes an active call when the last unit dispatched to it reports
		/// back in service or out of service (status base type of the Available or Unavailable class). Only units count; a
		/// call with no unit dispatched, a call under an active incident command, or a unit that has not reported since its
		/// dispatch keeps the call open. Missing = false (dispatchers close calls).
		/// </summary>
		CloseCallWhenUnitsClear = 116,

		/// <summary>
		/// ProtoBuf-serialized <see cref="DocumentNumberingConfig"/>: the department's own number patterns for work orders,
		/// invoices, bids and daily time reports, and the day their numbering year starts (Department -> Document Numbering).
		/// Missing = the built-in numbers. Custom sequences are counted in DocumentNumberSequences.
		/// </summary>
		DocumentNumberingConfig = 117,
	}
}
