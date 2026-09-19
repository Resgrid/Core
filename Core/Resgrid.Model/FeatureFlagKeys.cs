namespace Resgrid.Model
{
	/// <summary>
	/// Well-known feature flag keys consumed by application code. Keys are matched case-insensitively by
	/// <see cref="Services.IFeatureToggleService"/>. When adding a key here, seed a matching flag row via a
	/// migration (see M0072) so the flag is immediately manageable from the admin UI/API.
	/// </summary>
	public static class FeatureFlagKeys
	{
		/// <summary>Free checklists rollout gate. Independent of paid plans and Maintenance.WorkOrders. Seeded off by M0189.</summary>
		public const string ChecklistsSystem = "Checklists.System";

		/// <summary>Maintenance and work orders rollout gate. Also requires a Readiness Pro entitlement. Seeded off by M0189.</summary>
		public const string MaintenanceWorkOrders = "Maintenance.WorkOrders";

		/// <summary>
		/// Routes inbound Twilio SMS through the new chatbot ingress pipeline. When off (globally or for a
		/// specific department) the original text-command handling in TwilioController is used instead.
		/// </summary>
		public const string ChatbotTwilioTextIntegration = "Chatbot.TwilioTextIntegration";

		/// <summary>
		/// Gates the realtime chat system (channels, DMs, incident chat, chatbot conversation) across the
		/// API, web UI and mobile apps. Free for all plans; used for staged rollout only. Seeded by M0108.
		/// </summary>
		public const string ChatSystem = "Chat.System";

		/// <summary>
		/// Gates the run card dispatch system (run cards, station-based dispatching, closest unit
		/// response, move-up recommendations) across the web UI and API. Seeded by M0116.
		/// </summary>
		public const string DispatchRunCards = "Dispatch.RunCards";

		/// <summary>
		/// Global admission gate for Advanced Data Protection (ADP) enrollment. When true, any department
		/// with an active paid ADP addon may enroll via the Enrollment Wizard; when false, no new enrollment
		/// commits anywhere (operator platform-wide pause). This flag gates NEW enrollment only — it is never
		/// consulted for runtime encrypt/decrypt behavior, grant issuance, rotation, or opt-out of a
		/// department whose durable DepartmentDataProtectionPolicies.State is already active. Operator-managed,
		/// seeded off and permanent by M0126; ordinary department administrators cannot see or change it.
		/// </summary>
		public const string DepartmentProtectedDataEnrollment = "Security.DepartmentProtectedDataEnrollment";

		/// <summary>
		/// Gates the Records module (RMS), the feature-flagged successor to Logs. Flag off leaves the Logs
		/// module unchanged; flag on shows Records in the Logs sidebar position (never both). Department
		/// activation is a separate, append-only cutover fact on RmsDepartmentCutover; this flag alone never
		/// blocks a legacy Log write. Seeded off by M0152. RMS plan section 4.1.
		/// </summary>
		public const string RecordsSystem = "Records.System";

		/// <summary>Field Records surface in the Responder app. Depends on Records.System. Seeded off by M0152.</summary>
		public const string RecordsFieldResponder = "Records.Field.Responder";

		/// <summary>Field Records surface in the Unit app. Depends on Records.System. Seeded off by M0152.</summary>
		public const string RecordsFieldUnit = "Records.Field.Unit";

		/// <summary>Field Records surface in the IC app. Depends on Records.System. Seeded off by M0152.</summary>
		public const string RecordsFieldIncidentCommand = "Records.Field.IncidentCommand";

		/// <summary>Field Records surface in the Dispatch app. Depends on Records.System. Seeded off by M0152.</summary>
		public const string RecordsFieldDispatch = "Records.Field.Dispatch";

		/// <summary>RMS-5 occupancy/property master and the Contacts pre-plan crosswalk. Depends on Records.System. Seeded off by M0186.</summary>
		public const string RecordsPreventionOccupancy = "Records.Prevention.Occupancy";

		/// <summary>RMS-5 inspection programs, code sets, inspections and violations. Depends on Records.Prevention.Occupancy. Seeded off by M0186.</summary>
		public const string RecordsPreventionInspections = "Records.Prevention.Inspections";

		/// <summary>RMS-5 hydrants and water sources. Depends on Records.System. Seeded off by M0186.</summary>
		public const string RecordsPreventionHydrants = "Records.Prevention.Hydrants";

		/// <summary>RMS-5 permits and plan review. Depends on Records.Prevention.Occupancy. Seeded off by M0186.</summary>
		public const string RecordsPreventionPermits = "Records.Prevention.Permits";

		/// <summary>RMS-5 community risk reduction activities. Depends on Records.System. Seeded off by M0186.</summary>
		public const string RecordsPreventionCrr = "Records.Prevention.Crr";

		/// <summary>RMS-5 investigation cases (restricted, case-level authorization). Depends on Records.System. Seeded off by M0186.</summary>
		public const string RecordsInvestigations = "Records.Investigations";

		/// <summary>RMS-4 optional post-finalization quality review. Depends on Records.System. Seeded off by M0186.</summary>
		public const string RecordsQualityReview = "Records.QualityReview";

		/// <summary>RMS-6 records analytics: response-performance, workload, executive, accreditation and community-risk dashboards over finalized Records. Depends on Records.System. Seeded off by M0187.</summary>
		public const string RecordsAnalytics = "Records.Analytics";

		/// <summary>Unified Search: cross-entity search over the global Lucene index plus the system-functionality command palette. Requires SearchConfig.Enabled in every process. Seeded off by M0208 (registry §4F).</summary>
		public const string SearchUnified = "Search.Unified";

		/// <summary>
		/// Operator-only, per-cluster availability switch for department-connected Stripe payment collection on
		/// invoices (Workforce &amp; Business Operations plan, Phase B2). Prerequisite of Invoicing.OnlinePayments.
		/// Only its global state counts — a department override never enables it — and ordinary department
		/// administrators cannot see or change it (the Security.DepartmentProtectedDataEnrollment pattern). On in the
		/// US cluster at launch, off in the EU cluster. Seeded off and permanent by M0212 (pending); until that
		/// migration lands the flag row does not exist and the switch evaluates off everywhere.
		/// </summary>
		public const string PaymentsStripeConnect = "Payments.StripeConnect";

		/// <summary>
		/// Operator master toggle for the paid Business Operations add-on surfaces (Workforce &amp; Business Operations
		/// plan, decision 11): the FeatureFlagPrerequisite of every paid flag below. Seeded off by M0211. The customer's
		/// purchase is the PlanAddonTypes.BusinessOperations entitlement; this flag is only the rollout/kill switch.
		/// </summary>
		public const string BusinessOperations = "Business.Operations";

		/// <summary>Phase B customer invoicing (billing profiles, rate cards, invoices, payments, aging). Child of Business.Operations. Seeded off by M0211.</summary>
		public const string CustomerInvoicing = "Invoicing.CustomerInvoicing";

		/// <summary>Phase B2 online payment collection through a department's own Stripe account. Child of Invoicing.CustomerInvoicing and Payments.StripeConnect. Seeded off by M0212.</summary>
		public const string OnlinePayments = "Invoicing.OnlinePayments";

		/// <summary>Phase C deployment core (deployments, roster, daily time reports, expenses, attachments, external-order link). Free: no Business.Operations prerequisite. Seeded off by M0219.</summary>
		public const string Deployments = "Operations.Deployments";

		/// <summary>Phase C contractor billing (rate schedules, contracts, bids, charge runs, invoice generation). Child of Business.Operations. Seeded off by M0219.</summary>
		public const string ContractorBilling = "Invoicing.ContractorBilling";

		/// <summary>Phase C Cal OES MARS cost recovery. Child of Business.Operations. Seeded off by M0219.</summary>
		public const string CalOesMars = "CostRecovery.CalOesMars";
	}
}
