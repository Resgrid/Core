using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// A call's dispatch notes, completion notes and call note rows are in its full text (schema 3), under the same
	/// protection rule as the nature and address: only where Advanced Data Protection is not enforced, never an envelope.
	/// </summary>
	[TestFixture]
	public class CallNotesProjectionTests
	{
		private Mock<ICallNotesRepository> _callNotes;
		private Mock<IDepartmentDataProtectionService> _protection;
		private SearchProjectionService _service;
		private bool _enforced;
		private List<CallNote> _rows;

		[SetUp]
		public void SetUp()
		{
			_enforced = false;
			_rows = new List<CallNote>
			{
				new CallNote { CallNoteId = 2, CallId = 42, Note = "<p>Keyholder on scene, panel shows <b>trouble</b> zone 3</p>", Timestamp = new DateTime(2026, 5, 1, 10, 5, 0) },
				new CallNote { CallNoteId = 1, CallId = 42, Note = "Alarm company: trouble alarm @ Bldg 4, room 12", Timestamp = new DateTime(2026, 5, 1, 10, 0, 0) },
				new CallNote { CallNoteId = 3, CallId = 42, Note = "Removed by a moderator", IsDeleted = true, Timestamp = new DateTime(2026, 5, 1, 10, 6, 0) },
				new CallNote { CallNoteId = 4, CallId = 42, Note = ProtectedDataEnvelope.Prefix + "AAAA", Timestamp = new DateTime(2026, 5, 1, 10, 7, 0) }
			};
			_callNotes = new Mock<ICallNotesRepository>();
			_callNotes.Setup(r => r.GetCallNotesByCallIdAsync(42)).ReturnsAsync(() => _rows);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.IsProtectionEnforcedAsync(It.IsAny<int>())).ReturnsAsync(() => _enforced);
			_service = new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), _protection.Object, null, _callNotes.Object);
		}

		private static Call Call() => new Call
		{
			CallId = 42, DepartmentId = 7, Number = "2026-000042", Name = "Commercial Alarm", NatureOfCall = "Alarm activation", Type = "Alarm",
			Address = "1 Industrial Way", Notes = "ACME Monitoring account 5521. <br/>Called in by operator 7.", CompletedNotes = "Reset by keyholder.",
			State = (int)CallStates.Closed, LoggedOn = new DateTime(2026, 5, 1, 9, 58, 0)
		};

		[Test]
		public async Task Notes_completed_notes_and_live_call_notes_are_in_the_full_text_oldest_first()
		{
			var p = await _service.BuildCallAsync(Call());

			p.SearchText.Should().Contain("ACME Monitoring account 5521. Called in by operator 7.", "markup is stripped");
			p.SearchText.Should().Contain("Reset by keyholder.");
			p.SearchText.Should().Contain("Alarm company: trouble alarm @ Bldg 4, room 12");
			p.SearchText.Should().Contain("Keyholder on scene, panel shows trouble zone 3");
			p.SearchText.IndexOf("trouble alarm", StringComparison.Ordinal).Should().BeLessThan(p.SearchText.IndexOf("panel shows", StringComparison.Ordinal));
			p.SearchText.Should().NotContain("Removed by a moderator", "deleted notes leave the projection");
			p.SearchText.Should().NotContain(ProtectedDataEnvelope.Prefix, "an envelope is never projected");
		}

		[Test]
		public async Task The_repository_wins_over_a_partial_list_carried_on_the_call()
		{
			var call = Call();
			call.CallNotes = new List<CallNote> { new CallNote { CallId = 42, Note = "Only the note this request posted" } };

			var p = await _service.BuildCallAsync(call);

			p.SearchText.Should().Contain("trouble alarm @ Bldg 4").And.NotContain("Only the note this request posted");
		}

		[Test]
		public async Task Enforced_protection_keeps_every_note_out()
		{
			_enforced = true;

			var p = await _service.BuildCallAsync(Call());

			p.SearchText.Should().BeNull();
			_callNotes.Verify(r => r.GetCallNotesByCallIdAsync(It.IsAny<int>()), Times.Never, "the note rows are not even read when nothing can be projected");
		}

		[Test]
		public async Task Without_the_repository_the_notes_loaded_on_the_call_are_used()
		{
			var service = new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), _protection.Object);
			var call = Call();
			call.CallNotes = _rows;

			var p = await service.BuildCallAsync(call);

			p.SearchText.Should().Contain("trouble alarm @ Bldg 4");
		}

		[Test]
		public void Schema_three_rebuilds_every_department_for_the_new_full_text()
		{
			GlobalSearchGeneration.SchemaVersion.Should().BeGreaterThanOrEqualTo(3);
			GlobalSearchGeneration.Compute(0, 0).Should().StartWith(GlobalSearchGeneration.SchemaVersion + ".");
		}
	}
}
