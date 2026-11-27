using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>What one step of a protocol run on a call is (P3 step-through). Persisted in the step log as the number: append only.</summary>
	public enum ProtocolStepStates
	{
		Pending = 0,
		Done = 1,
		Skipped = 2,
		NotApplicable = 3
	}

	/// <summary>One step a protocol version can be stepped through with: a steps-block item, or a "contact medical control" gate.</summary>
	public sealed class ProtocolStepDefinition
	{
		/// <summary>Stable within the version: <c>{blockKey}-{itemKey}</c> for a step, the gate's block key, or h1, h2... for P1 html lists.</summary>
		public string Id { get; set; }

		public string Text { get; set; }

		/// <summary>"step" or "gate".</summary>
		public string Kind { get; set; }

		/// <summary>The steps block title or the variant (Adult, Pediatric) the step is in, when there is one.</summary>
		public string Group { get; set; }

		/// <summary>The anchor in the version's HTML (b-...), null for P1 html steps.</summary>
		public string Anchor { get; set; }

		public int Order { get; set; }
	}

	/// <summary>
	/// The steps of a protocol version (P3): a block version's steps items and gates, in reading order, variants included; a P1 html
	/// version's ordered-list items. Versions are immutable once out of draft, so the ids a call's log records keep meaning the same step.
	/// </summary>
	public static class ProtocolSteps
	{
		public const string StepKind = "step";
		public const string GateKind = "gate";

		private static readonly Regex OrderedList = new Regex(@"<ol(\s[^>]*)?>([\s\S]*?)</ol\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
		private static readonly Regex ListItem = new Regex(@"<li(\s[^>]*)?>([\s\S]*?)</li\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

		public static List<ProtocolStepDefinition> Extract(string contentJson, string contentHtml)
		{
			var steps = new List<ProtocolStepDefinition>();
			var document = ProtocolContentDocument.Parse(contentJson, contentHtml);
			if (document.IsBlockModel)
			{
				foreach (var (block, variant) in ProtocolBlocks.Flatten(document))
				{
					if (block.Type == ProtocolBlockTypes.Steps)
					{
						var group = string.Join(" · ", new[] { variant?.Label, block.Title }.Where(x => !string.IsNullOrWhiteSpace(x)));
						foreach (var item in block.Items ?? new List<ProtocolStepItem>())
						{
							if (item == null || !ProtocolBlocks.IsValidKey(item.Key))
								continue;
							var id = block.Key + "-" + item.Key;
							steps.Add(new ProtocolStepDefinition
							{
								Id = id,
								Text = StringHelpers.HtmlToPlainText(item.Html),
								Kind = StepKind,
								Group = group.Length == 0 ? null : group,
								Anchor = ProtocolBlocks.Anchor(id),
								Order = steps.Count + 1
							});
						}
					}
					else if (block.Type == ProtocolBlockTypes.Gate && ProtocolBlocks.IsValidKey(block.Key))
					{
						var text = StringHelpers.HtmlToPlainText(block.Html);
						if (!string.IsNullOrWhiteSpace(block.Contact))
							text = (text.Length > 0 ? text + " — " : string.Empty) + block.Contact;
						steps.Add(new ProtocolStepDefinition
						{
							Id = block.Key,
							Text = text,
							Kind = GateKind,
							Group = variant?.Label,
							Anchor = ProtocolBlocks.Anchor(block.Key),
							Order = steps.Count + 1
						});
					}
				}

				return steps;
			}

			try
			{
				foreach (Match list in OrderedList.Matches(document.Render() ?? string.Empty))
				{
					foreach (Match item in ListItem.Matches(list.Groups[2].Value))
					{
						var text = StringHelpers.HtmlToPlainText(item.Groups[2].Value);
						if (string.IsNullOrWhiteSpace(text))
							continue;
						steps.Add(new ProtocolStepDefinition
						{
							Id = "h" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture),
							Text = text,
							Kind = StepKind,
							Order = steps.Count + 1
						});
					}
				}
			}
			catch (RegexMatchTimeoutException)
			{
				steps.Clear();
			}

			return steps;
		}
	}

	/// <summary>One timestamped event of a step-through log.</summary>
	public sealed class ProtocolStepLogEntry
	{
		public const int NoteMaxLength = 500;
		public const int ClientStepIdMaxLength = 64;

		[JsonProperty("step")]
		public string StepId { get; set; }

		/// <summary><see cref="ProtocolStepStates"/>.</summary>
		[JsonProperty("state")]
		public int State { get; set; }

		/// <summary>When the server recorded it (UTC).</summary>
		[JsonProperty("at")]
		public DateTime At { get; set; }

		/// <summary>When the device says it happened (UTC), for a step recorded offline and sent later; null when online.</summary>
		[JsonProperty("deviceAt", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? DeviceAt { get; set; }

		[JsonProperty("by")]
		public string UserId { get; set; }

		/// <summary>
		/// The note as written: only on entries recorded before M0403, and, on a read, the note row's text as the reader may see it
		/// (REDACTED without a grant under ADP). Never written into the stored log any more (<see cref="NoteId"/>).
		/// </summary>
		[JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
		public string Note { get; set; }

		/// <summary>ADP catalog 39 (M0403): the <see cref="CallProtocolStepNote"/> holding this event's note; the log keeps only the pointer.</summary>
		[JsonProperty("noteId", NullValueHandling = NullValueHandling.Ignore)]
		public string NoteId { get; set; }

		/// <summary>
		/// The device's own id for this event (round 5F): an offline replay that sends the same id again is ignored, so a step recorded once
		/// on the device is in the log once however often the queue is replayed.
		/// </summary>
		[JsonProperty("cid", NullValueHandling = NullValueHandling.Ignore)]
		public string ClientStepId { get; set; }
	}

	/// <summary>
	/// A protocol stepped through on a call (P3), stored in CallProtocols.StepLogJson: the version it ran, who started and finished it,
	/// and every step event in order. Append-only: a step's state is its last event, so an undo is a later event, never an edit.
	/// </summary>
	public sealed class ProtocolStepLog
	{
		public const int MaxEntries = 2000;

		[JsonProperty("schema")]
		public int Schema { get; set; } = 1;

		[JsonProperty("versionId", NullValueHandling = NullValueHandling.Ignore)]
		public int? ProtocolVersionId { get; set; }

		[JsonProperty("startedOn")]
		public DateTime StartedOn { get; set; }

		[JsonProperty("startedBy")]
		public string StartedByUserId { get; set; }

		[JsonProperty("completedOn", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? CompletedOn { get; set; }

		[JsonProperty("completedBy", NullValueHandling = NullValueHandling.Ignore)]
		public string CompletedByUserId { get; set; }

		[JsonProperty("entries")]
		public List<ProtocolStepLogEntry> Entries { get; set; } = new List<ProtocolStepLogEntry>();

		[JsonIgnore]
		public bool IsComplete => CompletedOn.HasValue;

		public static ProtocolStepLog Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				var log = JsonConvert.DeserializeObject<ProtocolStepLog>(json);
				if (log != null)
					log.Entries ??= new List<ProtocolStepLogEntry>();
				return log;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

		/// <summary>The step's current state: its last event, Pending when it has none.</summary>
		public ProtocolStepStates StateOf(string stepId)
		{
			var last = Entries?.LastOrDefault(e => string.Equals(e.StepId, stepId, StringComparison.Ordinal));
			return last == null ? ProtocolStepStates.Pending : (ProtocolStepStates)last.State;
		}

		public ProtocolStepLogEntry LastFor(string stepId) => Entries?.LastOrDefault(e => string.Equals(e.StepId, stepId, StringComparison.Ordinal));

		/// <summary>Whether the log already holds the event a device sent with this id (an offline replay).</summary>
		public bool HasClientStep(string clientStepId) =>
			!string.IsNullOrEmpty(clientStepId) && (Entries ?? new List<ProtocolStepLogEntry>()).Any(e => string.Equals(e.ClientStepId, clientStepId, StringComparison.Ordinal));

		/// <summary>A device's event id as stored: trimmed, at most <see cref="ProtocolStepLogEntry.ClientStepIdMaxLength"/> characters, null when blank.</summary>
		public static string CleanClientStepId(string clientStepId)
		{
			var clean = clientStepId?.Trim();
			if (string.IsNullOrEmpty(clean))
				return null;
			return clean.Length > ProtocolStepLogEntry.ClientStepIdMaxLength ? clean.Substring(0, ProtocolStepLogEntry.ClientStepIdMaxLength) : clean;
		}
	}

	/// <summary>One step as a call shows it: the definition, its current state and its last event.</summary>
	public sealed class ProtocolStepView
	{
		public ProtocolStepDefinition Step { get; set; }
		public ProtocolStepStates State { get; set; }
		public ProtocolStepLogEntry Last { get; set; }
	}

	/// <summary>A protocol on a call as a checklist (P3): the row, the version it cites, its steps and the log.</summary>
	public sealed class ProtocolCallChecklist
	{
		public CallProtocol Row { get; set; }
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public ProtocolStepLog Log { get; set; }
		public List<ProtocolStepView> Steps { get; set; } = new List<ProtocolStepView>();

		public int DoneCount => Steps.Count(s => s.State != ProtocolStepStates.Pending);

		/// <summary>True for a Procedure-kind protocol (incident SOP checklists: MAYDAY, RIT, hazmat entry).</summary>
		public bool IsIncidentSop => Protocol != null && Protocol.Kind == (int)ProtocolKinds.Procedure;
	}

	/// <summary>Why a step-through action was refused. Not persisted.</summary>
	public enum ProtocolStepErrors
	{
		None = 0,
		NotFound,
		NotAuthorized,
		/// <summary>The protocol is not Procedure or Dispatch kind, or its version has no steps.</summary>
		NoSteps,
		UnknownStep,
		/// <summary>The log was finished; reopen is not offered.</summary>
		Completed,
		/// <summary>Someone else wrote the log at the same moment, three times running.</summary>
		Conflict,
		/// <summary>The protocol could not be attached to the call (deleted, disabled or another department's).</summary>
		NotAttachable
	}

	public sealed class ProtocolStepResult
	{
		public ProtocolStepErrors Error { get; set; }
		public ProtocolCallChecklist Checklist { get; set; }
		public bool Succeeded => Error == ProtocolStepErrors.None;

		public static ProtocolStepResult Fail(ProtocolStepErrors error) => new ProtocolStepResult { Error = error };
	}

	public static class ProtocolStepRules
	{
		/// <summary>Step-through applies to Procedure and Dispatch protocols (P3).</summary>
		public static bool Applies(int kind) => kind == (int)ProtocolKinds.Procedure || kind == (int)ProtocolKinds.Dispatch;
	}
}
