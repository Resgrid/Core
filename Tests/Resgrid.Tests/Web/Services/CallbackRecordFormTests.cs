using System;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class CallbackRecordFormTests
	{
		[Test]
		public void Field_form_round_trips_into_the_locked_record_details()
		{
			var input = new SaveRecordDraftInput
			{
				DefinitionKey = RmsDefinitionKeys.Callback,
				CallId = 12,
				Values = [Cell("initial-report", "Station inspection"), Cell("narrative", "Reported back to command.")]
			};

			var draft = RecordsApiMapper.ToDraftInput(input, RmsOriginClient.Dispatch);
			var record = RecordsApiMapper.ToRecord(new RecordAggregate
			{
				Record = new RmsOperationalRecord
				{
					DefinitionKey = RmsDefinitionKeys.Callback, DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion
				},
				Details = draft.Details
			}, false);

			Assert.That(draft.CallId, Is.EqualTo(12));
			Assert.That(draft.Details.InitialReport, Is.EqualTo("Station inspection"));
			Assert.That(draft.Values, Is.Null);
			Assert.That(record.Values.Sections[0].Rows[0].Cells,
				Has.Some.Matches<RecordValueCellData>(cell => cell.FieldKey == "narrative" && cell.Value == "Reported back to command."));
		}

		[Test]
		public void Editing_a_callback_preserves_unedited_relationships_and_protected_fields()
		{
			var current = new RecordAggregate
			{
				Record = new RmsOperationalRecord
				{
					DefinitionKey = RmsDefinitionKeys.Callback, DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion,
					ExternalId = "original"
				},
				Details = new RmsOperationalRecordDetail
				{
					Narrative = "<p>Inspection <strong>complete</strong>.</p>",
					Location = ProtectedDataEnvelope.RedactionValue,
					InvestigatedByUserId = "investigator"
				},
				Participants = [new RmsRecordParticipant { UserId = "member" }],
				Units = [new RmsRecordUnitResponse { UnitId = 4 }]
			};
			var input = new SaveRecordDraftInput { Values = [Cell("narrative", "Inspection complete.")] };

			var draft = RecordsApiMapper.ToDraftInput(input, RmsOriginClient.Dispatch, current);

			Assert.That(draft.Details.Narrative, Is.EqualTo(current.Details.Narrative));
			Assert.That(draft.Details.Location, Is.EqualTo(ProtectedDataEnvelope.RedactionValue));
			Assert.That(draft.Details.InvestigatedByUserId, Is.EqualTo("investigator"));
			Assert.That(draft.ExternalId, Is.EqualTo("original"));
			Assert.That(draft.Participants[0].UserId, Is.EqualTo("member"));
			Assert.That(draft.Units[0].UnitId, Is.EqualTo(4));

			input.Values.Add(Cell("location", "new location"));
			Assert.Throws<UnauthorizedAccessException>(() => RecordsApiMapper.ToDraftInput(input, RmsOriginClient.Dispatch, current));
		}

		[Test]
		public void A_values_only_save_keeps_the_linked_call_unless_the_request_names_one()
		{
			var current = new RecordAggregate
			{
				Record = new RmsOperationalRecord
				{
					DefinitionKey = RmsDefinitionKeys.Callback, DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion, CallId = 41
				}
			};

			var kept = RecordsApiMapper.ToDraftInput(new SaveRecordDraftInput { Values = [Cell("narrative", "Back in quarters.")] }, RmsOriginClient.Dispatch, current);
			var moved = RecordsApiMapper.ToDraftInput(new SaveRecordDraftInput { CallId = 42, Values = [Cell("narrative", "Back in quarters.")] }, RmsOriginClient.Dispatch, current);

			Assert.That(kept.CallId, Is.EqualTo(41));
			Assert.That(moved.CallId, Is.EqualTo(42));
		}

		[Test]
		public void A_callback_revision_snapshot_carries_its_form_values()
		{
			var snapshot = new RecordSnapshot
			{
				DefinitionKey = RmsDefinitionKeys.Callback, DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion,
				Details = new RmsOperationalRecordDetail { InitialReport = "Station inspection", Narrative = "Reported back." }
			};

			var record = RecordsApiMapper.ToRecord(snapshot, false);

			Assert.That(record.Values, Is.Not.Null);
			Assert.That(record.Values.Sections[0].Rows[0].Cells,
				Has.Some.Matches<RecordValueCellData>(cell => cell.FieldKey == "initial-report" && cell.Value == "Station inspection"));
			Assert.That(RecordsApiMapper.ToRecord(new RecordSnapshot { DefinitionKey = RmsDefinitionKeys.Run, DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion }, false).Values, Is.Null);
		}

		[Test]
		public void The_existing_details_input_keeps_its_contract()
		{
			var input = new SaveRecordDraftInput
			{
				DefinitionKey = RmsDefinitionKeys.Callback,
				Details = new RecordDetailsInput { Narrative = "Typed input" },
				Values = [Cell("narrative", "Generic form")]
			};

			Assert.That(RecordsApiMapper.ToDraftInput(input, RmsOriginClient.Dispatch).Details.Narrative, Is.EqualTo("Typed input"));
		}

		private static RecordValueInputData Cell(string field, string value)
			=> new() { SectionKey = "details", FieldKey = field, Value = value };
	}
}
