using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>Where the CAD call ID of an inbound dispatch email is read from (<see cref="EmailCallUpdateConfig.IdSource"/>). Persisted; append only.</summary>
	public enum CadCallIdSources
	{
		/// <summary>The key the department's email format reads (its incident number or external identifier).</summary>
		FromFormat = 0,

		/// <summary>The first word after a label such as "Call ID:" on the same line.</summary>
		Label = 1,

		/// <summary>The first capture group of a regular expression.</summary>
		Pattern = 2
	}

	/// <summary>Which part of the email a label or pattern is searched in. Persisted; append only.</summary>
	public enum CadCallIdSearchAreas
	{
		SubjectThenBody = 0,
		Subject = 1,
		Body = 2
	}

	/// <summary>What a CAD update may do to a field group. Persisted; append only.</summary>
	public enum CadFieldPolicies
	{
		/// <summary>The call takes the CAD's new value.</summary>
		Update = 0,

		/// <summary>The call keeps its value; the CAD's new value is recorded in the update note.</summary>
		NoteOnly = 1
	}

	/// <summary>What a CAD update does to the nature or notes text. Persisted; append only.</summary>
	public enum CadTextPolicies
	{
		Replace = 0,
		Append = 1,
		NoteOnly = 2
	}

	/// <summary>When a CAD update pages everyone on the call again. Persisted; append only.</summary>
	public enum CadRepageRules
	{
		Never = 0,
		LocationOrPriorityChanged = 1,
		AnyChange = 2,

		/// <summary>Every matched email pages again as the call's next alarm (its dispatch count goes up), changed or not.</summary>
		EveryUpdate = 3
	}

	/// <summary>
	/// ProtoBuf-serialized <see cref="DepartmentSettingTypes.EmailCallUpdates"/> (Department → Call Settings, "Updates from your CAD"):
	/// whether an inbound dispatch email that carries the CAD call ID of a call Resgrid already has updates (or closes) that call
	/// instead of creating a new one, and what an update may change. A missing row means off, except for the email formats that
	/// matched calls before this setting existed, which get <see cref="EmailCallUpdatePresets"/> (plan cad-email-call-updates-plan.md).
	/// </summary>
	[ProtoContract]
	public class EmailCallUpdateConfig
	{
		public const int MaxLabelLength = 50;
		public const int MaxPatternLength = 200;
		public const int MaxPhrasesLength = 500;
		public const int DefaultClosedMatchMinutes = 60;
		public const int MaxClosedMatchMinutes = 1440;

		/// <summary>Off: every email makes a new call.</summary>
		[ProtoMember(1)]
		public bool Enabled { get; set; }

		/// <summary><see cref="CadCallIdSources"/>.</summary>
		[ProtoMember(2)]
		public int IdSource { get; set; }

		/// <summary>The label for <see cref="CadCallIdSources.Label"/>, e.g. "Call ID:". Case-insensitive.</summary>
		[ProtoMember(3)]
		public string IdLabel { get; set; }

		/// <summary>The regular expression for <see cref="CadCallIdSources.Pattern"/>; exactly one capture group.</summary>
		[ProtoMember(4)]
		public string IdPattern { get; set; }

		/// <summary><see cref="CadCallIdSearchAreas"/>.</summary>
		[ProtoMember(5)]
		public int IdSearchIn { get; set; }

		/// <summary><see cref="CadFieldPolicies"/> for the address, coordinates and What3Words.</summary>
		[ProtoMember(6)]
		public int LocationPolicy { get; set; }

		/// <summary><see cref="CadFieldPolicies"/>.</summary>
		[ProtoMember(7)]
		public int PriorityPolicy { get; set; }

		/// <summary><see cref="CadFieldPolicies"/> for the call type.</summary>
		[ProtoMember(8)]
		public int TypePolicy { get; set; }

		/// <summary><see cref="CadFieldPolicies"/> for the call name.</summary>
		[ProtoMember(9)]
		public int NamePolicy { get; set; }

		/// <summary><see cref="CadTextPolicies"/> for the nature of call.</summary>
		[ProtoMember(10)]
		public int NaturePolicy { get; set; }

		/// <summary><see cref="CadTextPolicies"/> for the call notes. Append by default.</summary>
		[ProtoMember(11)]
		[DefaultValue((int)CadTextPolicies.Append)]
		public int NotesPolicy { get; set; } = (int)CadTextPolicies.Append;

		/// <summary><see cref="CadRepageRules"/>. Location or priority by default.</summary>
		[ProtoMember(12)]
		[DefaultValue((int)CadRepageRules.LocationOrPriorityChanged)]
		public int RepageWhen { get; set; } = (int)CadRepageRules.LocationOrPriorityChanged;

		/// <summary>Units, groups, roles or people an update adds to the call are paged (they were never told).</summary>
		[ProtoMember(13)]
		[DefaultValue(true)]
		public bool PageAddedResources { get; set; } = true;

		/// <summary>A field a person changed in Resgrid keeps their value; the CAD value goes in the note.</summary>
		[ProtoMember(14)]
		[DefaultValue(true)]
		public bool DispatcherEditsWin { get; set; } = true;

		/// <summary>A call closed within this many minutes still matches, as a note only. 0 = closed calls never match.</summary>
		[ProtoMember(15)]
		[DefaultValue(DefaultClosedMatchMinutes)]
		public int ClosedMatchMinutes { get; set; } = DefaultClosedMatchMinutes;

		/// <summary>Phrases (one per line) that mark an email as the CAD closing the call. None = never.</summary>
		[ProtoMember(16)]
		public string ClosePhrases { get; set; }

		/// <summary>Phrases (one per line) that mark an email as the CAD cancelling the call. None = never.</summary>
		[ProtoMember(17)]
		public string CancelPhrases { get; set; }

		/// <summary>A call the CAD closes or cancels sends the normal close notice.</summary>
		[ProtoMember(18)]
		[DefaultValue(true)]
		public bool NotifyOnClose { get; set; } = true;

		/// <summary>Not stored: this config is the built-in preset of the department's email format (no row saved).</summary>
		[ProtoIgnore]
		public bool IsBuiltInPreset { get; set; }

		public CadCallIdSources GetIdSource() => Known<CadCallIdSources>(IdSource, CadCallIdSources.FromFormat);
		public CadCallIdSearchAreas GetIdSearchIn() => Known<CadCallIdSearchAreas>(IdSearchIn, CadCallIdSearchAreas.SubjectThenBody);
		public CadFieldPolicies GetLocationPolicy() => Known<CadFieldPolicies>(LocationPolicy, CadFieldPolicies.Update);
		public CadFieldPolicies GetPriorityPolicy() => Known<CadFieldPolicies>(PriorityPolicy, CadFieldPolicies.Update);
		public CadFieldPolicies GetTypePolicy() => Known<CadFieldPolicies>(TypePolicy, CadFieldPolicies.Update);
		public CadFieldPolicies GetNamePolicy() => Known<CadFieldPolicies>(NamePolicy, CadFieldPolicies.Update);
		public CadTextPolicies GetNaturePolicy() => Known<CadTextPolicies>(NaturePolicy, CadTextPolicies.Replace);
		public CadTextPolicies GetNotesPolicy() => Known<CadTextPolicies>(NotesPolicy, CadTextPolicies.Append);
		public CadRepageRules GetRepageWhen() => Known<CadRepageRules>(RepageWhen, CadRepageRules.LocationOrPriorityChanged);
		public int GetClosedMatchMinutes() => Math.Clamp(ClosedMatchMinutes, 0, MaxClosedMatchMinutes);

		public IReadOnlyList<string> GetClosePhrases() => Phrases(ClosePhrases);
		public IReadOnlyList<string> GetCancelPhrases() => Phrases(CancelPhrases);

		/// <summary>
		/// A copy with every value limited to what Resgrid stores: known enum values, trimmed and length-capped text, the window
		/// clamped. Used before saving and by the settings page.
		/// </summary>
		public EmailCallUpdateConfig Normalized()
		{
			return new EmailCallUpdateConfig
			{
				Enabled = Enabled,
				IdSource = (int)GetIdSource(),
				IdLabel = Cap(IdLabel, MaxLabelLength),
				IdPattern = Cap(IdPattern, MaxPatternLength),
				IdSearchIn = (int)GetIdSearchIn(),
				LocationPolicy = (int)GetLocationPolicy(),
				PriorityPolicy = (int)GetPriorityPolicy(),
				TypePolicy = (int)GetTypePolicy(),
				NamePolicy = (int)GetNamePolicy(),
				NaturePolicy = (int)GetNaturePolicy(),
				NotesPolicy = (int)GetNotesPolicy(),
				RepageWhen = (int)GetRepageWhen(),
				PageAddedResources = PageAddedResources,
				DispatcherEditsWin = DispatcherEditsWin,
				ClosedMatchMinutes = GetClosedMatchMinutes(),
				ClosePhrases = JoinPhrases(GetClosePhrases()),
				CancelPhrases = JoinPhrases(GetCancelPhrases()),
				NotifyOnClose = NotifyOnClose,
				IsBuiltInPreset = IsBuiltInPreset
			};
		}

		/// <summary>
		/// Why this config cannot be saved, as a key the settings page localizes, or null when it can: a label or pattern source
		/// without one, a pattern that does not compile or does not have exactly one capture group.
		/// </summary>
		public string Validate()
		{
			if (!Enabled)
				return null;

			switch (GetIdSource())
			{
				case CadCallIdSources.Label:
					if (string.IsNullOrWhiteSpace(IdLabel))
						return "CadUpdatesLabelRequired";
					break;
				case CadCallIdSources.Pattern:
					if (string.IsNullOrWhiteSpace(IdPattern))
						return "CadUpdatesPatternRequired";
					try
					{
						var regex = new Regex(IdPattern.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
						if (regex.GetGroupNumbers().Length != 2)
							return "CadUpdatesPatternOneGroup";
					}
					catch (ArgumentException)
					{
						return "CadUpdatesPatternInvalid";
					}
					break;
			}

			return null;
		}

		private static IReadOnlyList<string> Phrases(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return Array.Empty<string>();

			return value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
				.Select(x => x.Trim())
				.Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static string JoinPhrases(IReadOnlyList<string> phrases)
		{
			if (phrases == null || phrases.Count == 0)
				return null;

			var joined = string.Join("\n", phrases);
			return joined.Length > MaxPhrasesLength ? joined.Substring(0, MaxPhrasesLength) : joined;
		}

		private static string Cap(string value, int length)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim();
			return trimmed.Length > length ? trimmed.Substring(0, length) : trimmed;
		}

		private static T Known<T>(int value, T fallback) where T : struct, Enum
		{
			return Enum.IsDefined(typeof(T), value) ? (T)(object)value : fallback;
		}
	}

	/// <summary>
	/// The built-in CAD update behaviour of the four email formats that matched follow-up emails to open calls before
	/// <see cref="DepartmentSettingTypes.EmailCallUpdates"/> existed (plan D1): it reproduces what those formats did, so a
	/// department that never opens the setting keeps today's behaviour on the shared engine. A saved row replaces it.
	/// </summary>
	public static class EmailCallUpdatePresets
	{
		/// <summary>
		/// True for the formats that read a call ID of their own (an incident number, external identifier or, for Parkland County 2,
		/// name and call time), so <see cref="CadCallIdSources.FromFormat"/> finds one. Labeled Fields reads one when a Call ID label is set.
		/// </summary>
		public static bool FormatReadsCallId(int formatType, EmailLabeledFieldsConfig labeledFields = null)
		{
			switch ((CallEmailTypes)formatType)
			{
				case CallEmailTypes.Active911:
				case CallEmailTypes.Brockport:
				case CallEmailTypes.CalFireECC:
				case CallEmailTypes.CalFireSCU:
				case CallEmailTypes.HancockCounty:
				case CallEmailTypes.OttawaCounty:
				case CallEmailTypes.OttawaKingstonToronto:
				case CallEmailTypes.ParklandCounty2:
				case CallEmailTypes.RandR:
				case CallEmailTypes.Resgrid:
					return true;
				case CallEmailTypes.LabeledFields:
					return labeledFields != null && labeledFields.GetLabels(LabeledEmailFields.CallId).Count > 0;
				default:
					return false;
			}
		}

		/// <summary>True for the formats with a built-in preset.</summary>
		public static bool HasPreset(int formatType)
		{
			return formatType == (int)CallEmailTypes.Active911 || formatType == (int)CallEmailTypes.RandR ||
				formatType == (int)CallEmailTypes.OttawaKingstonToronto || formatType == (int)CallEmailTypes.ParklandCounty2;
		}

		/// <summary>The preset for <paramref name="formatType"/>, or null when the format has none.</summary>
		public static EmailCallUpdateConfig ForFormat(int formatType)
		{
			if (!HasPreset(formatType))
				return null;

			var parkland = formatType == (int)CallEmailTypes.ParklandCounty2;

			return new EmailCallUpdateConfig
			{
				Enabled = true,
				IdSource = (int)CadCallIdSources.FromFormat,
				IdSearchIn = (int)CadCallIdSearchAreas.SubjectThenBody,
				LocationPolicy = (int)CadFieldPolicies.NoteOnly,
				PriorityPolicy = (int)(parkland ? CadFieldPolicies.Update : CadFieldPolicies.NoteOnly),
				TypePolicy = (int)CadFieldPolicies.NoteOnly,
				NamePolicy = (int)CadFieldPolicies.NoteOnly,
				NaturePolicy = (int)CadTextPolicies.NoteOnly,
				NotesPolicy = (int)CadTextPolicies.Replace,
				RepageWhen = (int)(parkland ? CadRepageRules.EveryUpdate : CadRepageRules.Never),
				PageAddedResources = false,
				DispatcherEditsWin = false,
				ClosedMatchMinutes = 0,
				NotifyOnClose = true,
				IsBuiltInPreset = true
			};
		}
	}
}
