using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Lifecycle of a department's dedicated number (persisted).</summary>
	public enum DepartmentPhoneNumberStatuses
	{
		/// <summary>Purchase requested at the carrier; not yet confirmed.</summary>
		Provisioning = 1,

		/// <summary>Owned and routing SMS and voice to Resgrid.</summary>
		Active = 2,

		/// <summary>The add-on lapsed or was cancelled; the number is held until <see cref="DepartmentPhoneNumber.ReleaseAfter"/>.</summary>
		Lapsed = 3,

		/// <summary>Returned to the carrier.</summary>
		Released = 4,

		/// <summary>The purchase or release failed; <see cref="DepartmentPhoneNumber.LastError"/> says why.</summary>
		Failed = 5
	}

	/// <summary>
	/// Release warnings sent to a department's admins once it stops paying (persisted flags, M0325; plan §8): at the time payment
	/// stopped (cancellation or non-payment), then 15, 5 and 1 day(s) before the number is released to the carrier.
	/// </summary>
	[Flags]
	public enum DedicatedNumberReleaseWarnings
	{
		None = 0,
		Stopped = 1,
		FifteenDays = 2,
		FiveDays = 4,
		OneDay = 8
	}

	/// <summary>
	/// A department's dedicated phone number (M0323, Dedicated Number add-on, PlanAddonTypes 7): bought from Twilio for the
	/// department, routing inbound SMS to intake and inbound voice to Enhanced AI voice intake (and the member IVR). Inbound
	/// only for SMS (no per-department A2P 10DLC). At most one Active number per department. The legacy TextToCallNumber
	/// setting (DepartmentSettingTypes 10) is kept in step with the Active number so existing readers keep working.
	/// </summary>
	[Table("DepartmentPhoneNumbers")]
	public class DepartmentPhoneNumber : IEntity
	{
		[Key]
		[Required]
		public string DepartmentPhoneNumberId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>E.164 ("+17755550100").</summary>
		[MaxLength(32)]
		public string PhoneNumber { get; set; }

		/// <summary>"Twilio".</summary>
		[MaxLength(20)]
		public string Provider { get; set; }

		/// <summary>The carrier's id for the number (Twilio IncomingPhoneNumber SID "PN…").</summary>
		[MaxLength(64)]
		public string ProviderNumberId { get; set; }

		/// <summary>ISO 3166-1 alpha-2 country of the number.</summary>
		[MaxLength(2)]
		public string CountryCode { get; set; }

		public bool SmsCapable { get; set; }

		public bool VoiceCapable { get; set; }

		/// <summary>A <see cref="DepartmentPhoneNumberStatuses"/> value.</summary>
		public int Status { get; set; }

		/// <summary>Inbound texts create pending calls (through the number's SMS intake channel).</summary>
		public bool SmsIntakeEnabled { get; set; } = true;

		/// <summary>Callers who are not members reach Enhanced AI voice intake (needs Enhanced AI + Ai.VoiceIntake).</summary>
		public bool VoiceIntakeEnabled { get; set; }

		public string ProvisionedByUserId { get; set; }

		public DateTime? ProvisionedOn { get; set; }

		/// <summary>When a Lapsed number is released; null while Active.</summary>
		public DateTime? ReleaseAfter { get; set; }

		public DateTime? ReleasedOn { get; set; }

		[MaxLength(1000)]
		public string LastError { get; set; }

		/// <summary>
		/// When worker 80 first saw an Active number's department without a paid add-on interval; null while the add-on is current.
		/// The number keeps routing for <c>DedicatedNumberAddonConfig.LapseDelayHours</c> after this (the renewal invoice is paid a little
		/// after the period ends, and card retries take days) before it is marked Lapsed.
		/// </summary>
		public DateTime? AddonInactiveSince { get; set; }

		/// <summary>
		/// <see cref="DedicatedNumberReleaseWarnings"/> flags of the release warnings the department's admins already got for the current
		/// unpaid period (M0325); cleared when payment resumes.
		/// </summary>
		public int ReleaseWarningsSent { get; set; }

		public DateTime? LastReleaseWarningOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		public bool IsHeld => Status == (int)DepartmentPhoneNumberStatuses.Provisioning || Status == (int)DepartmentPhoneNumberStatuses.Active ||
			Status == (int)DepartmentPhoneNumberStatuses.Lapsed;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentPhoneNumberId; }
			set { DepartmentPhoneNumberId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentPhoneNumbers";

		[NotMapped]
		public string IdName => "DepartmentPhoneNumberId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsHeld" };
	}

	/// <summary>A number offered by the carrier for purchase.</summary>
	public class AvailablePhoneNumber
	{
		public string PhoneNumber { get; set; }

		public string FriendlyName { get; set; }

		/// <summary>City or town (the carrier's rate center locality).</summary>
		public string Locality { get; set; }

		/// <summary>State, province or region; a two-letter code in the United States and Canada.</summary>
		public string Region { get; set; }

		public string PostalCode { get; set; }

		public string CountryCode { get; set; }

		/// <summary>Local (geographic) or mobile; dedicated numbers are bought as one or the other.</summary>
		public DedicatedNumberTypes NumberType { get; set; } = DedicatedNumberTypes.Local;

		public bool SmsCapable { get; set; }

		public bool VoiceCapable { get; set; }
	}

	/// <summary>The kind of number a dedicated number search returned (not persisted).</summary>
	public enum DedicatedNumberTypes
	{
		Local = 1,
		Mobile = 2
	}

	/// <summary>Whether a department may buy, or holds, the Dedicated Number add-on.</summary>
	public class DedicatedNumberAccess
	{
		/// <summary>The department has an active paid entity plan (a billing relationship).</summary>
		public bool HasBillingRelationship { get; set; }

		/// <summary>The add-on is offered (Intake.DedicatedNumber on and billing configured).</summary>
		public bool IsOffered { get; set; }

		/// <summary>The department holds an active add-on period.</summary>
		public bool HasActiveAddon { get; set; }

		/// <summary>"Stripe" (US/CA) or "Paddle" (EU); null when no billing relationship.</summary>
		public string BillingProvider { get; set; }

		/// <summary>Countries numbers may be searched in for this department.</summary>
		public IReadOnlyList<string> AllowedCountries { get; set; }

		public bool CanPurchase => IsOffered && HasBillingRelationship && !HasActiveAddon;

		public bool CanProvision => HasActiveAddon;

		/// <summary>The first entity-pack plan id (LimitsService: entity plans are PlanId 36 and up).</summary>
		public const int FirstEntityPlanId = 36;

		/// <summary>
		/// True for an entity-pack plan id. Free and restricted tiers (1, 6, 7, 8) are all below it, but callers still pair this with their
		/// own IsPlanRestrictedOrFree check so a future free entity tier cannot open the add-on. Shared by Core and the Billing API.
		/// </summary>
		public static bool IsEntityPlan(int planId) => planId >= FirstEntityPlanId;
	}

	/// <summary>Why a dedicated number request was refused (shown to the department admin; never a fault).</summary>
	public enum DedicatedNumberErrors
	{
		/// <summary>The department holds no active Dedicated Number add-on.</summary>
		AddonRequired = 1,

		/// <summary>The department already has a number (provisioning, active or held after lapsing).</summary>
		AlreadyHasNumber = 2,

		/// <summary>Numbers may not be bought in that country for this department.</summary>
		CountryNotAllowed = 3,

		/// <summary>The number is not a valid E.164 number, or the search input is invalid.</summary>
		InvalidNumber = 4,

		/// <summary>Another department holds the number.</summary>
		NumberUnavailable = 5,

		/// <summary>The department has no Active number to change.</summary>
		NoActiveNumber = 6,

		/// <summary>The carrier could not be reached or refused the request.</summary>
		CarrierUnavailable = 7,

		/// <summary>A search near the department's address, but neither the department's map center nor its address could be located.</summary>
		DepartmentLocationUnknown = 8,

		/// <summary>The department's plan is known not to be an active paid entity plan, which the add-on needs (plan §8 D10).</summary>
		PlanRequired = 9
	}

	/// <summary>A refused dedicated number request; the admin sees <see cref="Error"/> as a localized message, so it never reaches Sentry.</summary>
	public sealed class DedicatedNumberException : InvalidOperationException, Resgrid.Framework.IUserFacingException
	{
		public DedicatedNumberException(DedicatedNumberErrors error) : base("Dedicated number request refused: " + error) { Error = error; }

		public DedicatedNumberErrors Error { get; }
	}
}
