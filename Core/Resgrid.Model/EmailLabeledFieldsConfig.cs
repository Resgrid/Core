using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>The call fields a department can map labels to in the Labeled Fields email format.</summary>
	public enum LabeledEmailFields
	{
		CallId = 1,
		Name = 2,
		Nature = 3,
		Address = 4,
		City = 5,
		CrossStreets = 6,
		Coordinates = 7,
		Latitude = 8,
		Longitude = 9,
		Type = 10,
		Priority = 11,
		Units = 12,
		Notes = 13
	}

	/// <summary>
	/// ProtoBuf-serialized <see cref="DepartmentSettingTypes.EmailLabeledFields"/>: the labels a department's CAD writes in front of
	/// each value ("Address:", "CALL TYPE", "Incident #") for the <see cref="CallEmailTypes.LabeledFields"/> format. Each member
	/// holds one or more labels separated by "|"; matching ignores case. A value runs to the end of its line or to the next known
	/// label. A missing row means no labels: the format then reads the email like the Generic format.
	/// </summary>
	[ProtoContract]
	public class EmailLabeledFieldsConfig
	{
		public const int MaxLabelsLength = 200;
		public const char Separator = '|';

		[ProtoMember(1)] public string CallId { get; set; }
		[ProtoMember(2)] public string Name { get; set; }
		[ProtoMember(3)] public string Nature { get; set; }
		[ProtoMember(4)] public string Address { get; set; }
		[ProtoMember(5)] public string City { get; set; }
		[ProtoMember(6)] public string CrossStreets { get; set; }
		[ProtoMember(7)] public string Coordinates { get; set; }
		[ProtoMember(8)] public string Latitude { get; set; }
		[ProtoMember(9)] public string Longitude { get; set; }
		[ProtoMember(10)] public string Type { get; set; }
		[ProtoMember(11)] public string Priority { get; set; }
		[ProtoMember(12)] public string Units { get; set; }
		[ProtoMember(13)] public string Notes { get; set; }

		/// <summary>The raw label text for a field.</summary>
		public string Get(LabeledEmailFields field)
		{
			switch (field)
			{
				case LabeledEmailFields.CallId: return CallId;
				case LabeledEmailFields.Name: return Name;
				case LabeledEmailFields.Nature: return Nature;
				case LabeledEmailFields.Address: return Address;
				case LabeledEmailFields.City: return City;
				case LabeledEmailFields.CrossStreets: return CrossStreets;
				case LabeledEmailFields.Coordinates: return Coordinates;
				case LabeledEmailFields.Latitude: return Latitude;
				case LabeledEmailFields.Longitude: return Longitude;
				case LabeledEmailFields.Type: return Type;
				case LabeledEmailFields.Priority: return Priority;
				case LabeledEmailFields.Units: return Units;
				case LabeledEmailFields.Notes: return Notes;
				default: return null;
			}
		}

		/// <summary>Sets the raw label text for a field.</summary>
		public void Set(LabeledEmailFields field, string value)
		{
			switch (field)
			{
				case LabeledEmailFields.CallId: CallId = value; break;
				case LabeledEmailFields.Name: Name = value; break;
				case LabeledEmailFields.Nature: Nature = value; break;
				case LabeledEmailFields.Address: Address = value; break;
				case LabeledEmailFields.City: City = value; break;
				case LabeledEmailFields.CrossStreets: CrossStreets = value; break;
				case LabeledEmailFields.Coordinates: Coordinates = value; break;
				case LabeledEmailFields.Latitude: Latitude = value; break;
				case LabeledEmailFields.Longitude: Longitude = value; break;
				case LabeledEmailFields.Type: Type = value; break;
				case LabeledEmailFields.Priority: Priority = value; break;
				case LabeledEmailFields.Units: Units = value; break;
				case LabeledEmailFields.Notes: Notes = value; break;
			}
		}

		/// <summary>The labels configured for a field, trimmed, without empties or repeats.</summary>
		public IReadOnlyList<string> GetLabels(LabeledEmailFields field)
		{
			var raw = Get(field);
			if (string.IsNullOrWhiteSpace(raw))
				return Array.Empty<string>();

			return raw.Split(Separator)
				.Select(x => x.Trim())
				.Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>True when at least one field has a label.</summary>
		public bool HasAnyLabel()
		{
			return Enum.GetValues(typeof(LabeledEmailFields)).Cast<LabeledEmailFields>().Any(f => GetLabels(f).Count > 0);
		}

		/// <summary>A copy with each field's labels trimmed, de-duplicated and capped at <see cref="MaxLabelsLength"/>.</summary>
		public EmailLabeledFieldsConfig Normalized()
		{
			var copy = new EmailLabeledFieldsConfig();
			foreach (LabeledEmailFields field in Enum.GetValues(typeof(LabeledEmailFields)))
			{
				var labels = GetLabels(field);
				if (labels.Count == 0)
					continue;

				var joined = string.Join(" " + Separator + " ", labels);
				copy.Set(field, joined.Length > MaxLabelsLength ? joined.Substring(0, MaxLabelsLength) : joined);
			}

			return copy;
		}
	}
}
