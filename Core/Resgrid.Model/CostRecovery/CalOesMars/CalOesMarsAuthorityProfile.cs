using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CostRecovery.CalOesMars
{
	/// <summary>
	/// A reviewed, versioned description of the official Cal OES MARS / CFAA material (plan decision 38): sources by
	/// URL, publication date and checksum, the F-42 field rules, request-number prefixes, external status vocabulary
	/// and the administrative de-minimis option. It is a code/data contract, not a table: a changed form, rate letter,
	/// CFAA term or portal status creates a new profile with a new code, and an earlier dispatch keeps the profile it
	/// was prepared against. Rates are never here — they are department data on <see cref="CalOesMarsRateProfile"/>.
	/// </summary>
	public sealed class CalOesMarsAuthorityProfile
	{
		public string Code { get; }
		public DateTime ReviewedOn { get; }
		public DateTime EffectiveOn { get; }
		public DateTime? SupersededOn { get; }
		/// <summary>Official material this profile pins. Checksums are recorded by the reviewer at review time.</summary>
		public IReadOnlyList<CalOesMarsAuthoritySource> Sources { get; }
		/// <summary>F-42 boxes in official order with the local validation rule each carries.</summary>
		public IReadOnlyList<CalOesMarsF42Box> F42Boxes { get; }
		/// <summary>Request-number prefixes the ordering system issues (E equipment, O overhead, C crew, S supply, A aircraft).</summary>
		public IReadOnlyList<string> RequestPrefixes { get; }
		/// <summary>External status vocabulary → local mirror state, for F-42 / expense records.</summary>
		public IReadOnlyDictionary<string, CalOesMarsLocalStates> RecordStatusMap { get; }
		/// <summary>External status vocabulary → local mirror state, for generated invoices.</summary>
		public IReadOnlyDictionary<string, CalOesMarsLocalStates> InvoiceStatusMap { get; }
		/// <summary>The administrative-rate de-minimis option the CFAA instructions offered on the review date (percent).</summary>
		public decimal DeMinimisAdministrativePercent { get; }
		/// <summary>Pinned MARS / F-5 resource type vocabulary.</summary>
		public IReadOnlyList<string> ResourceTypes { get; }
		/// <summary>Personnel classification codes the Salary Survey expects.</summary>
		public IReadOnlyList<string> SalaryClassifications { get; }

		public bool IsReviewed => Sources.Count > 0 && F42Boxes.Count > 0;
		public bool IsCurrent(DateTime asOf) => asOf.Date >= EffectiveOn.Date && (!SupersededOn.HasValue || asOf.Date < SupersededOn.Value.Date);

		private CalOesMarsAuthorityProfile(string code, DateTime reviewedOn, DateTime effectiveOn, DateTime? supersededOn, IReadOnlyList<CalOesMarsAuthoritySource> sources,
			IReadOnlyList<CalOesMarsF42Box> boxes, IReadOnlyList<string> prefixes, IReadOnlyDictionary<string, CalOesMarsLocalStates> recordStatuses,
			IReadOnlyDictionary<string, CalOesMarsLocalStates> invoiceStatuses, decimal deMinimis, IReadOnlyList<string> resourceTypes, IReadOnlyList<string> classifications)
		{
			Code = code; ReviewedOn = reviewedOn; EffectiveOn = effectiveOn; SupersededOn = supersededOn; Sources = sources; F42Boxes = boxes; RequestPrefixes = prefixes;
			RecordStatusMap = recordStatuses; InvoiceStatusMap = invoiceStatuses; DeMinimisAdministrativePercent = deMinimis; ResourceTypes = resourceTypes; SalaryClassifications = classifications;
		}

		/// <summary>The first implementation profile: the official material current on 2026-08-21 (plan decision 38).</summary>
		public const string CurrentCode = "CFAA-2026-08-21";

		public static readonly IReadOnlyList<CalOesMarsAuthorityProfile> All = new[] { Build20260821() };

		public static CalOesMarsAuthorityProfile Current => All.Last();

		public static CalOesMarsAuthorityProfile Get(string code) => All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

		/// <summary>The profile effective on a dispatch date, or null when none covers it (fail closed for new submissions).</summary>
		public static CalOesMarsAuthorityProfile ForDispatch(DateTime dispatchOn) => All.Where(p => p.IsCurrent(dispatchOn)).OrderByDescending(p => p.EffectiveOn).FirstOrDefault();

		public CalOesMarsLocalStates? MapRecordStatus(string externalStatus) => Map(RecordStatusMap, externalStatus);
		public CalOesMarsLocalStates? MapInvoiceStatus(string externalStatus) => Map(InvoiceStatusMap, externalStatus);

		private static CalOesMarsLocalStates? Map(IReadOnlyDictionary<string, CalOesMarsLocalStates> map, string externalStatus)
		{
			if (string.IsNullOrWhiteSpace(externalStatus)) return null;
			var key = map.Keys.FirstOrDefault(k => string.Equals(k, externalStatus.Trim(), StringComparison.OrdinalIgnoreCase));
			return key == null ? null : map[key];
		}

		private static CalOesMarsAuthorityProfile Build20260821()
		{
			var sources = new[]
			{
				new CalOesMarsAuthoritySource("cfaa", "California Fire Assistance Agreement (CFAA) 2020-2025 and addenda", "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/", new DateTime(2020, 1, 1)),
				new CalOesMarsAuthoritySource("mars-help", "Cal OES MARS user help, F-42 checklist and FAQ", "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/", new DateTime(2026, 8, 21)),
				new CalOesMarsAuthoritySource("salary-survey", "CFAA Salary Survey and Administrative Rate instructions", "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/", new DateTime(2026, 8, 21)),
				new CalOesMarsAuthoritySource("rate-letter", "Cal OES Rate Letter (apparatus, support vehicle, POV mileage, per diem)", "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/", new DateTime(2026, 8, 21)),
				new CalOesMarsAuthoritySource("f5", "Cal OES Form F-5 resource inventory instructions", "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/", new DateTime(2026, 8, 21))
			};
			var boxes = new[]
			{
				new CalOesMarsF42Box("agency", "Responding agency / MACS designator", true, "AgencyProfile"),
				new CalOesMarsF42Box("incident", "Incident name and number", true, "Deployment"),
				new CalOesMarsF42Box("order", "Incident order number", true, "ExternalOrder"),
				new CalOesMarsF42Box("request", "Request number (prefixed)", true, "ExternalOrderFill"),
				new CalOesMarsF42Box("resource", "Resource type / strike team / task force", true, "ExternalOrderFill"),
				new CalOesMarsF42Box("dispatch", "Dispatch (commitment) date and time", true, "ExternalOrderFill"),
				new CalOesMarsF42Box("return", "Return or redispatch date and time", true, "ExternalOrderFill"),
				new CalOesMarsF42Box("apparatus", "Apparatus / support vehicle identifiers", false, "Roster"),
				new CalOesMarsF42Box("personnel", "Personnel, rank and commitment or actual hours", true, "Roster"),
				new CalOesMarsF42Box("rotation", "Crew rotations (approval evidence)", false, "Attachments"),
				new CalOesMarsF42Box("comments", "Comments / loss / damage / supply numbers", false, "Snapshot"),
				new CalOesMarsF42Box("responding-signature", "Responding agency representative signature", true, "Snapshot"),
				new CalOesMarsF42Box("incident-signature", "Incident / AREP authorization", true, "Snapshot"),
				new CalOesMarsF42Box("attachments", "Supporting attachments (paper F-42 or signed copy)", true, "Attachments")
			};
			var recordStatuses = new Dictionary<string, CalOesMarsLocalStates>(StringComparer.OrdinalIgnoreCase)
			{
				["Cal OES Review"] = CalOesMarsLocalStates.SubmittedExternal,
				["Agency Review"] = CalOesMarsLocalStates.ReturnedForAgencyReview,
				["Approved"] = CalOesMarsLocalStates.Approved,
				["Documentation Only"] = CalOesMarsLocalStates.DocumentationOnly
			};
			var invoiceStatuses = new Dictionary<string, CalOesMarsLocalStates>(StringComparer.OrdinalIgnoreCase)
			{
				["Pending Local Agency Approval"] = CalOesMarsLocalStates.PendingLocalAgencyApproval,
				["Local Agency Rejected"] = CalOesMarsLocalStates.LocalAgencyRejected,
				["Pending Paying Entity Approval"] = CalOesMarsLocalStates.PendingPayingEntityApproval,
				["Paid"] = CalOesMarsLocalStates.Paid,
				["Documentation Only"] = CalOesMarsLocalStates.DocumentationOnly
			};
			var resourceTypes = new[]
			{
				"Type 1 Engine", "Type 2 Engine", "Type 3 Engine", "Type 4 Engine", "Type 5 Engine", "Type 6 Engine", "Type 7 Engine",
				"Water Tender", "Type 1 Dozer", "Type 2 Dozer", "Hand Crew", "Overhead", "Support Vehicle", "Command Vehicle", "Ambulance", "Rescue", "Other"
			};
			var classifications = new[] { "Fire Chief", "Deputy Chief", "Division Chief", "Battalion Chief", "Captain", "Lieutenant", "Engineer", "Firefighter", "Firefighter/Paramedic", "Non-suppression" };
			return new CalOesMarsAuthorityProfile(CurrentCode, new DateTime(2026, 8, 21), new DateTime(2020, 1, 1), null, sources, boxes, new[] { "E", "O", "C", "S", "A" },
				recordStatuses, invoiceStatuses, 10m, resourceTypes, classifications);
		}
	}

	public sealed class CalOesMarsAuthoritySource
	{
		public string Key { get; }
		public string Title { get; }
		public string Url { get; }
		public DateTime PublishedOn { get; }
		/// <summary>Recorded by the reviewer; null until the artifact is checksummed.</summary>
		public string Checksum { get; }

		public CalOesMarsAuthoritySource(string key, string title, string url, DateTime publishedOn, string checksum = null)
		{
			Key = key; Title = title; Url = url; PublishedOn = publishedOn; Checksum = checksum;
		}
	}

	public sealed class CalOesMarsF42Box
	{
		public string Id { get; }
		public string Label { get; }
		public bool Required { get; }
		/// <summary>Where the value is sourced from (AgencyProfile, Deployment, ExternalOrder, ExternalOrderFill, Roster, Attachments, Snapshot).</summary>
		public string Source { get; }

		public CalOesMarsF42Box(string id, string label, bool required, string source)
		{
			Id = id; Label = label; Required = required; Source = source;
		}
	}
}
