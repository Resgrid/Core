using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 134 (ProtoBuf), Protocols → Settings: signature levels above the floors, the default review interval,
	/// and retention and legal hold for superseded and retired versions (R-P10). Missing = the floors, no default interval,
	/// keep everything.
	/// </summary>
	[ProtoContract]
	public class ProtocolLibrarySettings
	{
		public const int MaxReviewIntervalDays = 3650;

		public ProtocolLibrarySettings()
		{
			Retention = new List<ProtocolKindRetention>();
		}

		/// <summary>Approvals and review stamps must be signed with a passkey (a raise above the L2 floor; never lower).</summary>
		[ProtoMember(1)]
		public bool RequirePasskeyForApproval { get; set; }

		/// <summary>Read-and-sign acknowledgments need a step-up (L2) instead of the L1 floor (used from P2).</summary>
		[ProtoMember(2)]
		public bool ReadAndSignRequiresStepUp { get; set; }

		/// <summary>The review interval a new protocol starts with; 0 for none.</summary>
		[ProtoMember(3)]
		public int DefaultReviewIntervalDays { get; set; }

		/// <summary>A department-wide legal hold: no version is purged while it is on.</summary>
		[ProtoMember(4)]
		public bool LegalHold { get; set; }

		/// <summary>How long each kind keeps superseded and retired versions before worker 84 may purge them; none listed = keep.</summary>
		[ProtoMember(5)]
		public List<ProtocolKindRetention> Retention { get; set; }

		/// <summary>
		/// The language protocols are written in (P3 content translations, e.g. "en", "de"): the fallback a reader gets when a version
		/// has no approved translation in their language, and the language the block labels of a version without its own are written
		/// in. Blank = English.
		/// </summary>
		[ProtoMember(6)]
		public string DefaultContentLocale { get; set; }

		/// <summary>
		/// Protocols P4: put the determinant and the protocol's name of a call's case into the dispatch push, the dispatch text message and
		/// the voice dispatch prompt. Off by default: crews see the call as before until a department turns it on.
		/// </summary>
		[ProtoMember(7)]
		public bool ShowDeterminantInDispatch { get; set; }

		/// <summary>The content language a version is written in: its own, else the department's, else English.</summary>
		public string ContentLocaleFor(ProtocolVersion version) =>
			ProtocolTranslationRules.NormalizeLocale(version?.Locale) ?? ProtocolTranslationRules.NormalizeLocale(DefaultContentLocale) ?? "en";

		/// <summary>Days a kind keeps its superseded and retired versions; null to keep them.</summary>
		public int? KeepDaysFor(int kind)
		{
			var days = Retention?.FirstOrDefault(r => r != null && r.Kind == kind)?.KeepDays ?? 0;
			return days > 0 ? days : (int?)null;
		}

		public ProtocolLibrarySettings Normalize()
		{
			DefaultReviewIntervalDays = Math.Clamp(DefaultReviewIntervalDays, 0, MaxReviewIntervalDays);
			DefaultContentLocale = ProtocolTranslationRules.NormalizeLocale(DefaultContentLocale);
			Retention = (Retention ?? new List<ProtocolKindRetention>())
				.Where(r => r != null && ProtocolKindRules.IsKnown(r.Kind) && r.KeepDays > 0)
				.GroupBy(r => r.Kind)
				.Select(g => new ProtocolKindRetention { Kind = g.Key, KeepDays = Math.Min(g.Max(r => r.KeepDays), 36500) })
				.OrderBy(r => r.Kind)
				.ToList();
			return this;
		}
	}

	/// <summary>One kind's retention for superseded and retired versions.</summary>
	[ProtoContract]
	public class ProtocolKindRetention
	{
		/// <summary><see cref="ProtocolKinds"/>.</summary>
		[ProtoMember(1)]
		public int Kind { get; set; }

		/// <summary>Days after a version is superseded or retired before it may be purged; 0 keeps it.</summary>
		[ProtoMember(2)]
		public int KeepDays { get; set; }
	}

	/// <summary>
	/// The open protocol exchange format (P1 export and import on the library; documented shape, version 1). Import always
	/// lands protocols as drafts for one review-and-publish pass; nothing is published by an import.
	/// <code>
	/// { "format": "resgrid-protocols", "version": 1, "exportedOn": "2026-11-24T12:00:00Z",
	///   "protocols": [ { "code": "SOG-204", "name": "Mayday", "kind": "Sog", "number": "SOG-204", "category": "Fireground",
	///     "tags": "rit,mayday", "description": "…", "minimumWeight": 0, "requiresAcknowledgment": true, "reviewIntervalDays": 365,
	///     "excludeFromAi": false, "isLicensedContent": false, "versionLabel": "3.1", "content": { /* ContentJson, schema 1 */ },
	///     "triggers": [ { "type": 1, "priority": "High", "callType": "Structure Fire", "startsOn": null, "endsOn": null } ],
	///     "questions": [ { "question": "…", "answers": [ { "answer": "…", "weight": 1 } ] } ],
	///     "audience": [ { "kind": "Group", "ref": "Station 4" } ] } ] }
	/// </code>
	/// Priorities, call types and audience groups and roles travel by name and are matched to the importing department's by name;
	/// anything that does not match is left out and reported.
	/// </summary>
	public sealed class ProtocolExchangeDocument
	{
		public const string FormatName = "resgrid-protocols";
		public const int CurrentVersion = 1;
		public const int MaxProtocolsPerImport = 500;

		[JsonProperty("format")]
		public string Format { get; set; } = FormatName;

		[JsonProperty("version")]
		public int Version { get; set; } = CurrentVersion;

		[JsonProperty("exportedOn")]
		public DateTime ExportedOn { get; set; }

		[JsonProperty("protocols")]
		public List<ProtocolExchangeItem> Protocols { get; set; } = new List<ProtocolExchangeItem>();

		/// <summary>
		/// Reads a document in this format (the one serializer every importer uses: the library's Import page and the department
		/// import wizard). Null when the text is not JSON of this shape; the format name and version are checked by the importer.
		/// </summary>
		public static ProtocolExchangeDocument Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				return JsonConvert.DeserializeObject<ProtocolExchangeDocument>(json, new JsonSerializerSettings { MaxDepth = 64 });
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>The document as the library's export writes it.</summary>
		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
	}

	public sealed class ProtocolExchangeItem
	{
		[JsonProperty("code")] public string Code { get; set; }
		[JsonProperty("name")] public string Name { get; set; }
		[JsonProperty("kind")] public string Kind { get; set; }
		[JsonProperty("number")] public string Number { get; set; }
		[JsonProperty("category")] public string Category { get; set; }
		[JsonProperty("tags")] public string Tags { get; set; }
		[JsonProperty("description")] public string Description { get; set; }
		[JsonProperty("minimumWeight")] public int MinimumWeight { get; set; }
		[JsonProperty("requiresAcknowledgment")] public bool RequiresAcknowledgment { get; set; }
		[JsonProperty("reviewIntervalDays")] public int? ReviewIntervalDays { get; set; }
		[JsonProperty("excludeFromAi")] public bool ExcludeFromAi { get; set; }
		[JsonProperty("isLicensedContent")] public bool IsLicensedContent { get; set; }
		[JsonProperty("versionLabel")] public string VersionLabel { get; set; }
		[JsonProperty("content")] public ProtocolContentDocument Content { get; set; }
		[JsonProperty("triggers")] public List<ProtocolExchangeTrigger> Triggers { get; set; } = new List<ProtocolExchangeTrigger>();
		[JsonProperty("questions")] public List<ProtocolExchangeQuestion> Questions { get; set; } = new List<ProtocolExchangeQuestion>();
		[JsonProperty("audience")] public List<ProtocolExchangeAudience> Audience { get; set; } = new List<ProtocolExchangeAudience>();
	}

	public sealed class ProtocolExchangeTrigger
	{
		[JsonProperty("type")] public int Type { get; set; }
		[JsonProperty("priority")] public string Priority { get; set; }
		[JsonProperty("callType")] public string CallType { get; set; }
		[JsonProperty("startsOn")] public DateTime? StartsOn { get; set; }
		[JsonProperty("endsOn")] public DateTime? EndsOn { get; set; }
	}

	public sealed class ProtocolExchangeQuestion
	{
		[JsonProperty("question")] public string Question { get; set; }
		[JsonProperty("answers")] public List<ProtocolExchangeAnswer> Answers { get; set; } = new List<ProtocolExchangeAnswer>();
	}

	public sealed class ProtocolExchangeAnswer
	{
		[JsonProperty("answer")] public string Answer { get; set; }
		[JsonProperty("weight")] public int Weight { get; set; }
	}

	public sealed class ProtocolExchangeAudience
	{
		/// <summary><see cref="ContentAudienceKinds"/> name: Everyone, Group, Role, CertificationType, UnitType or Shift.</summary>
		[JsonProperty("kind")] public string Kind { get; set; }

		/// <summary>The group, role, certification type, unit type or shift name.</summary>
		[JsonProperty("ref")] public string Ref { get; set; }
	}

	/// <summary>What an import did: drafts created, and what could not be matched.</summary>
	public sealed class ProtocolImportResult
	{
		/// <summary>True for a preview (the department import wizard's dry run): nothing was written; <see cref="Created"/> is what would be.</summary>
		public bool DryRun { get; set; }

		/// <summary>The code or name of each protocol created (or, in a preview, that would be).</summary>
		public List<string> Names { get; set; } = new List<string>();

		public int Created { get; set; }
		public List<string> Skipped { get; set; } = new List<string>();
		public List<string> Warnings { get; set; } = new List<string>();
		public string Error { get; set; }
	}
}
