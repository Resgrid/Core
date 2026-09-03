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
	}
}
