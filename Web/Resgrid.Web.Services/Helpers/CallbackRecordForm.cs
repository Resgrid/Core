using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>Exposes the locked Callback's text fields to field clients that render Records schemas.</summary>
	internal static class CallbackRecordForm
	{
		private const string SectionKey = "details";
		private const string SectionLabel = "Details";

		private sealed record Field(string Key, string Label, RmsFieldType Type, Func<RmsOperationalRecordDetail, string> Read,
			Action<RmsOperationalRecordDetail, string> Write);

		private static readonly Field[] Fields =
		[
			new("initial-report", "Initial report", RmsFieldType.ShortText, d => d.InitialReport, (d, value) => d.InitialReport = value),
			new("cause", "Cause", RmsFieldType.ShortText, d => d.Cause, (d, value) => d.Cause = value),
			new("location", "Location", RmsFieldType.ShortText, d => d.Location, (d, value) => d.Location = value),
			new("other-agencies", "Other agencies", RmsFieldType.ShortText, d => d.OtherAgencies, (d, value) => d.OtherAgencies = value),
			new("other-units", "Other units", RmsFieldType.ShortText, d => d.OtherUnits, (d, value) => d.OtherUnits = value),
			new("other-personnel", "Other personnel", RmsFieldType.ShortText, d => d.OtherPersonnel, (d, value) => d.OtherPersonnel = value),
			new("narrative", "Narrative", RmsFieldType.LongText, d => d.Narrative, (d, value) => d.Narrative = value)
		];

		public static bool Matches(string key, int version)
			=> version == RmsDefinitionKeys.LockedDefinitionVersion && string.Equals(key, RmsDefinitionKeys.Callback, StringComparison.Ordinal);

		public static RecordDefinitionVersionData Version() => new()
		{
			DefinitionKey = RmsDefinitionKeys.Callback,
			Version = RmsDefinitionKeys.LockedDefinitionVersion,
			State = RmsDefinitionVersionState.Published.ToString(),
			LifecyclePreset = RmsDefinitionKeys.LockedDefaultPreset.ToString(),
			Cardinality = RmsDefinitionKeys.CardinalityFor(RmsDefinitionKeys.Callback).ToString(),
			MinimumClientCapability = RecordsApiContract.LockedDefinitionCapability,
			Schema = new RecordDefinitionSchema
			{
				Sections =
				[
					new RecordSectionSchema
					{
						Key = SectionKey,
						Label = SectionLabel,
						Fields = Fields.Select(field => new RecordFieldSchema
						{
							Key = field.Key, Label = field.Label, Type = field.Type, RequiredToFinalize = field.Key == "narrative"
						}).ToList()
					}
				]
			}
		};

		public static RecordValuesData Read(RmsOperationalRecordDetail details)
		{
			var row = new RecordValueRowData();
			var data = new RecordValuesData
			{
				DefinitionKey = RmsDefinitionKeys.Callback,
				DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion,
				Sections = [new RecordValueSectionData { SectionKey = SectionKey, Label = SectionLabel, Rows = [row] }]
			};
			foreach (var field in Fields)
			{
				var stored = details == null ? null : field.Read(details);
				var withheld = stored == ProtectedDataEnvelope.RedactionValue;
				var value = withheld ? null : DisplayValue(field, stored);
				row.Cells.Add(new RecordValueCellData
				{
					FieldKey = field.Key, Label = field.Label, Type = field.Type.ToString(),
					Classification = RmsFieldClassification.Standard.ToString(), Value = value, Display = value, Withheld = withheld
				});
				if (withheld)
					data.WithheldFieldKeys.Add(field.Key);
			}
			return data;
		}

		public static void Apply(RecordDraftInput draft, List<RecordValueInputData> values, RecordAggregate current)
		{
			var byField = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var value in values)
			{
				if (value == null || value.SectionKey != SectionKey || !string.IsNullOrEmpty(value.RowKey)
					|| !Fields.Any(field => field.Key == value.FieldKey) || value.Value == ProtectedDataEnvelope.RedactionValue
					|| !byField.TryAdd(value.FieldKey, value.Value))
					throw new ArgumentException("The Callback form contains an invalid or duplicate field.");
			}

			var stored = current?.Details;
			draft.Details = stored == null ? new RmsOperationalRecordDetail() : CopyDetails(stored);
			foreach (var field in Fields)
			{
				var prior = stored == null ? null : field.Read(stored);
				var hasValue = byField.TryGetValue(field.Key, out var value);
				if (prior == ProtectedDataEnvelope.RedactionValue)
				{
					if (hasValue)
						throw new UnauthorizedAccessException("Reveal the protected Callback field before changing it.");
					continue;
				}

				// Preserve a formatted narrative when its plain-text representation was left unchanged.
				field.Write(draft.Details, hasValue && value == DisplayValue(field, prior) ? prior : value);
			}

			if (current == null) return;
			// The linked Call is kept unless the request names one: a form-only save never clears it.
			draft.CallId ??= current.Record.CallId;
			draft.ExternalId = current.Record.ExternalId;
			draft.StartedOn = current.Record.StartedOn;
			draft.EndedOn = current.Record.EndedOn;
			draft.Participants = current.Participants.Select(p => new RecordParticipantInput { UserId = p.UserId, UnitId = p.UnitId, Role = p.Role }).ToList();
			draft.Units = current.Units.Select(u => new RecordUnitResponseInput
			{
				UnitId = u.UnitId, Dispatched = u.Dispatched, Enroute = u.Enroute, OnScene = u.OnScene,
				Released = u.Released, InQuarters = u.InQuarters
			}).ToList();
		}

		private static string DisplayValue(Field field, string value)
			=> field.Key == "narrative" && value != null ? RecordNarrativeFormatter.ToPlainText(value) : value;

		// The records service copies these working detail columns, so carry forward fields this form does not edit.
		private static RmsOperationalRecordDetail CopyDetails(RmsOperationalRecordDetail d) => new()
		{
			Narrative = d.Narrative, InitialReport = d.InitialReport, Type = d.Type, Course = d.Course, CourseCode = d.CourseCode,
			Instructors = d.Instructors, Cause = d.Cause, InvestigatedByUserId = d.InvestigatedByUserId, ContactName = d.ContactName,
			ContactNumber = d.ContactNumber, OtherPersonnel = d.OtherPersonnel, Location = d.Location, OtherAgencies = d.OtherAgencies,
			OtherUnits = d.OtherUnits, BodyLocation = d.BodyLocation, PronouncedDeceasedBy = d.PronouncedDeceasedBy,
			CaseNumber = d.CaseNumber, Destination = d.Destination, Facilitator = d.Facilitator, UnitId = d.UnitId, ActivityOn = d.ActivityOn
		};
	}
}
