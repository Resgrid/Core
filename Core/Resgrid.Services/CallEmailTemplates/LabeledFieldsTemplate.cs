using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	/// <summary>
	/// A format a CAD can bend to rather than the other way round: the department says which label its CAD writes in front of each
	/// value ("Address:", "CALL TYPE", "Incident #"), and the format reads those (<see cref="EmailLabeledFieldsConfig"/>,
	/// cad-email-call-updates-plan.md Phase 2). A value runs to the end of its line or to the next known label on the same line.
	/// Without labels it reads an email exactly like <see cref="GenericTemplate"/>.
	/// </summary>
	public class LabeledFieldsTemplate : ICallEmailTemplate, ICallEmailParser
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls,
			List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			var parsed = await ParseAsync(email, managingUser, users, department, units, priority, activePriorities, callTypes, geolocationProvider, null);
			return parsed?.Call;
		}

		public Task<CallEmailParseResult> ParseAsync(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider,
			EmailLabeledFieldsConfig labeledFields)
		{
			var result = new CallEmailParseResult();
			var body = CadEmailText.BodyText(email);
			if (email == null || (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(email.Subject)))
				return Task.FromResult<CallEmailParseResult>(null);

			var c = new Call
			{
				LoggedOn = DateTime.UtcNow,
				Priority = priority,
				ReportingUserId = managingUser,
				Dispatches = new Collection<CallDispatch>(),
				UnitDispatches = new Collection<CallDispatchUnit>(),
				CallSource = (int)CallSources.EmailImport,
				SourceIdentifier = email.MessageId
			};
			result.Call = c;

			var values = ReadLabeledValues(labeledFields, email.Subject, body);
			var fields = CallEmailParsedFields.None;

			// The CAD call ID: what the CAD update engine matches follow-up emails on.
			if (values.TryGetValue(LabeledEmailFields.CallId, out var callId))
			{
				var id = CadEmailText.NormalizeId(callId);
				if (id != null)
				{
					c.ExternalIdentifier = id;
					c.IncidentNumber = id;
					result.MatchKey = id;
				}
			}

			// Nature: the mapped value, else the whole body (as Generic).
			if (values.TryGetValue(LabeledEmailFields.Nature, out var nature))
				c.NatureOfCall = nature;
			else
				c.NatureOfCall = body;
			if (!string.IsNullOrWhiteSpace(c.NatureOfCall))
				fields |= CallEmailParsedFields.Nature;

			// Name: the mapped value, else the subject.
			if (values.TryGetValue(LabeledEmailFields.Name, out var name))
				c.Name = name;
			else if (!string.IsNullOrWhiteSpace(email.Subject))
				c.Name = email.Subject.Trim();
			if (!string.IsNullOrWhiteSpace(c.Name))
				fields |= CallEmailParsedFields.Name;

			if (values.TryGetValue(LabeledEmailFields.Address, out var address))
			{
				c.Address = values.TryGetValue(LabeledEmailFields.City, out var city) && !address.Contains(city, StringComparison.OrdinalIgnoreCase)
					? $"{address}, {city}"
					: address;
				fields |= CallEmailParsedFields.Address;
			}

			var coordinates = ReadCoordinates(values);
			if (coordinates != null)
			{
				c.GeoLocationData = coordinates;
				fields |= CallEmailParsedFields.Coordinates;
			}

			if (values.TryGetValue(LabeledEmailFields.Type, out var typeName))
			{
				var callType = CallTypeLink.FindByName(callTypes, typeName);
				c.Type = callType?.Type ?? typeName;
				c.CallTypeId = callType?.CallTypeId;
				fields |= CallEmailParsedFields.Type;
			}

			if (values.TryGetValue(LabeledEmailFields.Priority, out var priorityText))
			{
				var match = CallPriorityMatch.Find(activePriorities, priorityText, priorityText);
				if (match != null)
				{
					c.Priority = match.DepartmentCallPriorityId;
					fields |= CallEmailParsedFields.Priority;
				}
			}

			// Notes: the mapped value, with the cross streets when the CAD sends them. Unmapped, the notes are the email as Generic keeps them.
			var notes = new List<string>();
			if (values.TryGetValue(LabeledEmailFields.Notes, out var notesValue))
				notes.Add(notesValue);
			if (values.TryGetValue(LabeledEmailFields.CrossStreets, out var crossStreets))
				notes.Add("Cross streets: " + crossStreets);
			if (notes.Count > 0)
				c.Notes = string.Join("\n", notes);
			else if (labeledFields == null || !labeledFields.HasAnyLabel())
				c.Notes = email.Subject + " " + body;
			if (!string.IsNullOrWhiteSpace(c.Notes))
				fields |= CallEmailParsedFields.Notes;

			// Units the CAD names, matched to the department's units by name.
			if (values.TryGetValue(LabeledEmailFields.Units, out var unitText) && units != null)
			{
				foreach (var unitName in unitText.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
				{
					var unit = units.FirstOrDefault(u => u != null && !string.IsNullOrWhiteSpace(u.Name) && string.Equals(u.Name.Trim(), unitName, StringComparison.OrdinalIgnoreCase));
					if (unit != null && c.UnitDispatches.All(x => x.UnitId != unit.UnitId))
						c.UnitDispatches.Add(new CallDispatchUnit { UnitId = unit.UnitId, LastDispatchedOn = DateTime.UtcNow });
				}

				if (c.UnitDispatches.Any())
					fields |= CallEmailParsedFields.Resources;
			}

			if (email.DispatchAudio != null)
			{
				c.Attachments = new Collection<CallAttachment>
				{
					new CallAttachment { FileName = email.DispatchAudioFileName, Data = email.DispatchAudio, CallAttachmentType = (int)CallAttachmentTypes.DispatchAudio }
				};
			}

			foreach (var u in users ?? new List<IdentityUser>())
				c.Dispatches.Add(new CallDispatch { UserId = u.UserId });

			result.ParsedFields = fields;
			return Task.FromResult(result);
		}

		/// <summary>
		/// Each mapped field's first non-empty value, read from the body and then the subject. On a line, a value runs from the end of
		/// its label to the next known label (or the line end); separators such as ":" or "#" after a label are skipped. Labels are
		/// whole words: "Address" does not match inside "MailingAddress".
		/// </summary>
		public static Dictionary<LabeledEmailFields, string> ReadLabeledValues(EmailLabeledFieldsConfig config, string subject, string body)
		{
			var values = new Dictionary<LabeledEmailFields, string>();
			if (config == null)
				return values;

			var labels = Enum.GetValues(typeof(LabeledEmailFields)).Cast<LabeledEmailFields>()
				.SelectMany(field => config.GetLabels(field).Select(label => (Field: field, Label: label)))
				.OrderByDescending(x => x.Label.Length)
				.ToList();
			if (labels.Count == 0)
				return values;

			foreach (var text in new[] { body, subject })
			{
				if (string.IsNullOrWhiteSpace(text))
					continue;

				foreach (var line in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
				{
					var hits = FindLabels(line, labels);
					for (var i = 0; i < hits.Count; i++)
					{
						var start = hits[i].Index + hits[i].Length;
						var end = i + 1 < hits.Count ? hits[i + 1].Index : line.Length;
						var value = line.Substring(start, Math.Max(0, end - start)).Trim().TrimStart(':', '#', '-', '=', '.').Trim();
						if (value.Length == 0 || values.ContainsKey(hits[i].Field))
							continue;

						values[hits[i].Field] = value.Length > 4000 ? value.Substring(0, 4000) : value;
					}
				}
			}

			return values;
		}

		private static List<(int Index, int Length, LabeledEmailFields Field)> FindLabels(string line, List<(LabeledEmailFields Field, string Label)> labels)
		{
			var hits = new List<(int Index, int Length, LabeledEmailFields Field)>();
			if (string.IsNullOrEmpty(line))
				return hits;

			foreach (var (field, label) in labels)
			{
				var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
				while (index >= 0)
				{
					var end = index + label.Length;
					var startsWord = index == 0 || !char.IsLetterOrDigit(line[index - 1]) || !char.IsLetterOrDigit(label[0]);
					var endsWord = end >= line.Length || !char.IsLetterOrDigit(line[end]) || !char.IsLetterOrDigit(label[label.Length - 1]);
					var overlaps = hits.Any(h => index < h.Index + h.Length && h.Index < end);
					if (startsWord && endsWord && !overlaps && IsLabelPosition(line, index, end, label))
						hits.Add((index, label.Length, field));

					index = line.IndexOf(label, end, StringComparison.OrdinalIgnoreCase);
				}
			}

			return hits.OrderBy(h => h.Index).ToList();
		}

		private const string Separators = ":#=";

		/// <summary>
		/// The word is a label here, not prose: it starts its line, or it ends with a separator (a label typed as "Incident #"), or a
		/// separator follows it ("Address: …"). "corrected address (" inside a narrative is not the Address label.
		/// </summary>
		private static bool IsLabelPosition(string line, int index, int end, string label)
		{
			if (line.Substring(0, index).Trim().Length == 0)
				return true;

			if (Separators.IndexOf(label[label.Length - 1]) >= 0)
				return true;

			var rest = line.Substring(end).TrimStart(' ', '\t');
			return rest.Length > 0 && Separators.IndexOf(rest[0]) >= 0;
		}

		/// <summary>"lat,lon" from a coordinates label, or from separate latitude and longitude labels; null when not readable.</summary>
		private static string ReadCoordinates(Dictionary<LabeledEmailFields, string> values)
		{
			double lat, lon;
			if (values.TryGetValue(LabeledEmailFields.Coordinates, out var both))
			{
				var parts = both.Split(new[] { ',', ' ', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2 && TryCoordinate(parts[0], 90, out lat) && TryCoordinate(parts[1], 180, out lon))
					return Format(lat, lon);
			}

			if (values.TryGetValue(LabeledEmailFields.Latitude, out var latText) && values.TryGetValue(LabeledEmailFields.Longitude, out var lonText) &&
				TryCoordinate(latText.Split(' ')[0], 90, out lat) && TryCoordinate(lonText.Split(' ')[0], 180, out lon))
				return Format(lat, lon);

			return null;
		}

		private static bool TryCoordinate(string text, double limit, out double value)
		{
			return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && Math.Abs(value) <= limit && value != 0;
		}

		private static string Format(double lat, double lon)
		{
			return lat.ToString("0.######", CultureInfo.InvariantCulture) + "," + lon.ToString("0.######", CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// A format with no incident number of its own (Parkland County 2): it names the key its follow-up pages share, and the
	/// comparison it used to find its open calls before the CAD update ledger existed.
	/// </summary>
	public interface ICallEmailMatchKeyTemplate
	{
		string GetMatchKey(Call parsed);

		bool IsSameIncident(Call parsed, Call existing);
	}
}
