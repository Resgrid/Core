using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.CallEmailTemplates;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// CAD email call updates (cad-email-call-updates-plan.md): the text rules, the setting and its presets, and the engine that
	/// decides whether a dispatch email creates a call or updates, closes or is noted on the call carrying its CAD call ID.
	/// </summary>
	[TestFixture]
	public class EmailCallUpdateServiceTests
	{
		private const int DepartmentId = 1;

		private List<Call> _calls;
		private List<CallNote> _notes;
		private int _saves;
		private FakeCallEmailUpdatesRepository _ledger;
		private Mock<ICallsService> _callsService;
		private Mock<ICallBroadcastOutboxService> _outbox;
		private Mock<ICallClosureService> _closure;
		private Mock<IEventAggregator> _events;
		private Mock<IGeoLocationProvider> _geo;
		private Mock<ICallDispatchUnitRepository> _unitDispatches;
		private EmailCallUpdateConfig _config;
		private EmailCallUpdateService _service;
		private int _created;

		[SetUp]
		public void SetUp()
		{
			_calls = new List<Call>();
			_notes = new List<CallNote>();
			_saves = 0;
			_created = 0;
			_config = new EmailCallUpdateConfig { Enabled = true, IdSource = (int)CadCallIdSources.Label, IdLabel = "Call ID:" };
			_ledger = new FakeCallEmailUpdatesRepository { Calls = () => _calls };

			_callsService = new Mock<ICallsService>();
			_callsService.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => _calls.FirstOrDefault(c => c.CallId == id));
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken ct) =>
				{
					_saves++;
					if (call.CallId <= 0)
					{
						call.CallId = 500 + _calls.Count;
						_calls.Add(call);
					}

					return call;
				});
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallNote note, CancellationToken ct) => { _notes.Add(note); return note; });
			_callsService.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);
			_callsService.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(() => _calls.Where(c => c.State == (int)CallStates.Active).ToList());
			_callsService.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			_callsService.Setup(c => c.GetCallTypesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<CallType>());

			_outbox = new Mock<ICallBroadcastOutboxService>();
			_outbox.Setup(o => o.RecordAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.ReturnsAsync(new CallBroadcastOutboxEntry());
			_outbox.Setup(o => o.SendPendingForCallAsync(It.IsAny<Call>(), It.IsAny<CallBroadcastOutboxEntry>())).ReturnsAsync(1);
			_closure = new Mock<ICallClosureService>();
			_events = new Mock<IEventAggregator>();
			_geo = new Mock<IGeoLocationProvider>();
			_geo.Setup(g => g.GetLatLonFromAddress(It.IsAny<string>())).ReturnsAsync("40.1,-75.2");
			_unitDispatches = new Mock<ICallDispatchUnitRepository>();

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 7, Name = "Engine 2", DepartmentId = DepartmentId } });

			_service = EmailCallUpdateServices.Create(_callsService.Object, _ledger, () => _config, _outbox, _closure, _events, geo: _geo.Object,
				unitDispatches: _unitDispatches, units: units.Object);
		}

		private static CallEmail Email(string body, string subject = "Dispatch", DateTime? sentOn = null) =>
			new CallEmail { MessageId = Guid.NewGuid().ToString(), Subject = subject, Body = body, TextBody = body, SentOn = sentOn };

		private async Task<EmailCallUpdateResult> ProcessAsync(CallEmail email, CallEmailTypes format = CallEmailTypes.Generic, int priority = 1)
		{
			var parsed = await _service.ParseAsync((int)format, email, "manager", new List<Resgrid.Model.Identity.IdentityUser>(), new Department { DepartmentId = DepartmentId },
				new List<Unit> { new Unit { UnitId = 7, Name = "Engine 2" } }, priority, new List<DepartmentCallPriority>(), new List<CallType>());

			return await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId,
				Department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" },
				FormatType = (int)format,
				Email = email,
				Parsed = parsed,
				Source = CallEmailUpdateSources.DispatchAddress,
				Config = _config
			}, async call =>
			{
				_created++;
				call.DepartmentId = DepartmentId;
				return await _callsService.Object.SaveCallAsync(call, CancellationToken.None);
			});
		}

		private Call OpenCall(string id, string address = "12 Oak St", int state = (int)CallStates.Active)
		{
			var call = new Call
			{
				CallId = 42, DepartmentId = DepartmentId, State = state, Name = "Structure Fire", NatureOfCall = "Smoke showing", Notes = "first page",
				Address = address, GeoLocationData = "40.0,-75.0", IncidentNumber = id, ExternalIdentifier = id, Priority = 1, LoggedOn = DateTime.UtcNow.AddMinutes(-5)
			};
			_calls.Add(call);
			return call;
		}

		// ---- text rules ----

		[Test]
		public void A_label_reads_the_first_word_after_it_on_the_same_line()
		{
			CadEmailText.AfterLabel("Type: Fire\nCall ID: 2026-0042 Priority 1\n", "call id:").Should().Be("2026-0042");
			CadEmailText.AfterLabel("Incident #  77812, Main St", "Incident #").Should().Be("77812");
			CadEmailText.AfterLabel("Call ID:\nnext line", "Call ID:").Should().BeNull("the value must be on the label's line");
		}

		[Test]
		public void A_pattern_reads_its_capture_group_and_a_runaway_pattern_gives_up()
		{
			CadEmailText.FirstGroup("CFS#: 26-00123 / Fire", @"CFS#:\s*(\S+)").Should().Be("26-00123");
			CadEmailText.FirstGroup(new string('a', 30000) + "!", @"(a+)+$").Should().BeNull("a backtracking pattern times out instead of holding up the email");
		}

		[Test]
		public void Close_and_cancel_phrases_are_whole_phrases_and_cancel_wins()
		{
			var config = new EmailCallUpdateConfig { ClosePhrases = "CALL CLOSED\nclosed", CancelPhrases = "Cancelled" };
			CadEmailText.DetectClose(config, Email("Status: CALL CLOSED at 10:42")).Should().Be(CadEmailCloseKinds.Close);
			CadEmailText.DetectClose(config, Email("Nothing disclosed yet")).Should().Be(CadEmailCloseKinds.None, "\"closed\" inside \"disclosed\" is not the phrase");
			CadEmailText.DetectClose(config, Email("call closed - cancelled by caller")).Should().Be(CadEmailCloseKinds.Cancel);
			CadEmailText.DetectClose(new EmailCallUpdateConfig(), Email("CALL CLOSED")).Should().Be(CadEmailCloseKinds.None, "no phrases, never");
		}

		[Test]
		public void A_pattern_must_compile_and_have_one_capture_group()
		{
			new EmailCallUpdateConfig { Enabled = true, IdSource = 2, IdPattern = "Call (\\S+)" }.Validate().Should().BeNull();
			new EmailCallUpdateConfig { Enabled = true, IdSource = 2, IdPattern = "Call \\S+" }.Validate().Should().Be("CadUpdatesPatternOneGroup");
			new EmailCallUpdateConfig { Enabled = true, IdSource = 2, IdPattern = "Call (\\S+" }.Validate().Should().Be("CadUpdatesPatternInvalid");
			new EmailCallUpdateConfig { Enabled = true, IdSource = 1 }.Validate().Should().Be("CadUpdatesLabelRequired");
		}

		[Test]
		public void The_setting_survives_a_protobuf_round_trip_with_its_true_defaults_turned_off()
		{
			var config = new EmailCallUpdateConfig
			{
				Enabled = true, PageAddedResources = false, DispatcherEditsWin = false, NotifyOnClose = false, ClosedMatchMinutes = 0,
				NotesPolicy = (int)CadTextPolicies.Replace, RepageWhen = (int)CadRepageRules.Never, ClosePhrases = "CLOSED"
			};

			var read = ObjectSerialization.Deserialize<EmailCallUpdateConfig>(ObjectSerialization.Serialize(config));

			read.PageAddedResources.Should().BeFalse();
			read.DispatcherEditsWin.Should().BeFalse();
			read.NotifyOnClose.Should().BeFalse();
			read.ClosedMatchMinutes.Should().Be(0);
			read.GetNotesPolicy().Should().Be(CadTextPolicies.Replace);
			read.GetRepageWhen().Should().Be(CadRepageRules.Never);
			read.GetClosePhrases().Should().Equal("CLOSED");
		}

		[Test]
		public void Only_the_four_formats_that_matched_before_have_a_preset()
		{
			EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.Active911).Should().NotBeNull();
			EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.RandR).IsBuiltInPreset.Should().BeTrue();
			EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.OttawaKingstonToronto).GetNotesPolicy().Should().Be(CadTextPolicies.Replace);
			var parkland = EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.ParklandCounty2);
			parkland.GetPriorityPolicy().Should().Be(CadFieldPolicies.Update);
			parkland.GetRepageWhen().Should().Be(CadRepageRules.EveryUpdate);
			EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.Generic).Should().BeNull();
			EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.HancockCounty).Should().BeNull();
		}

		// ---- engine ----

		[Test]
		public async Task With_updates_off_every_email_creates_a_call()
		{
			_config = new EmailCallUpdateConfig();
			OpenCall("2026-0042");

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nFire"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.NotApplicable);
			_created.Should().Be(1);
			_ledger.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task An_email_without_a_call_id_creates_a_call_as_before()
		{
			OpenCall("2026-0042");

			var result = await ProcessAsync(Email("Fire at 12 Oak St"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.NotApplicable);
			_created.Should().Be(1);
		}

		[Test]
		public async Task A_first_email_creates_a_call_that_carries_the_id()
		{
			var result = await ProcessAsync(Email("Call ID: 2026-0042\nFire"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Created);
			result.Call.ExternalIdentifier.Should().Be("2026-0042");
			result.Call.IncidentNumber.Should().Be("2026-0042");
			_ledger.Rows.Should().ContainSingle(r => r.Outcome == (int)CallEmailUpdateOutcomes.Created && r.CallId == result.Call.CallId && r.FieldHashes != null);
		}

		[Test]
		public async Task A_corrected_address_updates_the_call_places_it_on_the_map_and_pages_again()
		{
			_config.LocationPolicy = (int)CadFieldPolicies.Update;
			var call = OpenCall("2026-0042");
			var parsedAddress = new CallEmailParseResult
			{
				Call = new Call { Name = "Structure Fire", Address = "12 Oak Ave", Priority = 1 },
				ParsedFields = CallEmailParsedFields.Address | CallEmailParsedFields.Name,
				MatchKey = "2026-0042"
			};
			_config.IdSource = (int)CadCallIdSources.FromFormat;

			var result = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Parsed = parsedAddress, Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" }
			}, c => { _created++; return Task.FromResult(c); });

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			_created.Should().Be(0);
			call.Address.Should().Be("12 Oak Ave");
			call.GeoLocationData.Should().Be("40.1,-75.2", "the new address is geocoded");
			result.Repaged.Should().BeTrue("the address changed (default rule)");
			_outbox.Verify(o => o.RecordAsync(It.Is<Call>(c => c.CallId == 42), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), "manager"), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(u => u.Call.CallId == 42)), Times.Once);
			_notes.Should().ContainSingle(n => n.Source == (int)CallNoteSources.CadEmail && n.Note.Contains("12 Oak St") && n.Note.Contains("12 Oak Ave"));
		}

		[Test]
		public async Task Note_only_keeps_the_value_and_writes_the_cad_value_in_the_note()
		{
			_config.LocationPolicy = (int)CadFieldPolicies.NoteOnly;
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var call = OpenCall("2026-0042");

			var result = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Parsed = new CallEmailParseResult { Call = new Call { Address = "12 Oak Ave" }, ParsedFields = CallEmailParsedFields.Address, MatchKey = "2026-0042" }
			}, c => Task.FromResult(c));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);
			call.Address.Should().Be("12 Oak St");
			_saves.Should().Be(0, "nothing was applied, so the call is not written");
			_notes.Should().ContainSingle(n => n.Note.Contains("12 Oak Ave") && n.Note.Contains("department setting"));
			_outbox.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_value_changed_in_resgrid_is_kept_when_the_cad_sends_another()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Parsed = new CallEmailParseResult { Call = new Call { Name = "Fire", Address = "12 Oak St" }, ParsedFields = CallEmailParsedFields.Address, MatchKey = "2026-0042" }
			}, async c => { c.DepartmentId = DepartmentId; return await _callsService.Object.SaveCallAsync(c, CancellationToken.None); });
			created.Outcome.Should().Be(CallEmailUpdateOutcomes.Created);

			// A dispatcher corrects the address in Resgrid.
			created.Call.Address = "14 Oak St";

			var result = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Parsed = new CallEmailParseResult { Call = new Call { Address = "12 Oak Ave" }, ParsedFields = CallEmailParsedFields.Address, MatchKey = "2026-0042" }
			}, c => Task.FromResult(c));

			created.Call.Address.Should().Be("14 Oak St");
			result.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupLocation && !c.Applied && c.Reason == EmailCallUpdateService.ReasonEdited);
			_notes.Should().Contain(n => n.Note.Contains("kept (changed in Resgrid)"));
		}

		[Test]
		public async Task An_email_older_than_one_already_applied_is_only_noted()
		{
			var call = OpenCall("2026-0042");
			_ledger.Rows.Add(new CallEmailUpdate { DepartmentId = DepartmentId, CallId = 42, CadCallId = "2026-0042", Outcome = (int)CallEmailUpdateOutcomes.Updated,
				SentOn = new DateTime(2026, 11, 23, 10, 5, 0, DateTimeKind.Utc), ReceivedOn = DateTime.UtcNow });

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nOld nature", sentOn: new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc)));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Stale);
			call.NatureOfCall.Should().Be("Smoke showing");
			_saves.Should().Be(0);
			_notes.Should().ContainSingle(n => n.Note.Contains("after a newer one"));
		}

		[Test]
		public async Task Appended_notes_keep_the_earlier_text()
		{
			var call = OpenCall("2026-0042");
			_config.NotesPolicy = (int)CadTextPolicies.Append;

			await ProcessAsync(Email("Call ID: 2026-0042\nSecond page"));

			call.Notes.Should().StartWith("first page").And.Contain("— CAD update").And.Contain("Second page");
		}

		[Test]
		public async Task Protected_notes_are_never_appended_to()
		{
			var call = OpenCall("2026-0042");
			call.Notes = ProtectedDataEnvelope.Format(1, "c2VhbGVk");
			_config.NotesPolicy = (int)CadTextPolicies.Append;

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nSecond page"));

			call.Notes.Should().Be(ProtectedDataEnvelope.Format(1, "c2VhbGVk"));
			result.Changes.Should().Contain(c => c.Group == EmailCallUpdateService.GroupNotes && c.Reason == EmailCallUpdateService.ReasonProtected);
		}

		[Test]
		public async Task A_pending_call_is_updated_but_never_paged()
		{
			var call = OpenCall("2026-0042", state: (int)CallStates.Pending);
			_config.RepageWhen = (int)CadRepageRules.EveryUpdate;

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nNew nature"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			call.NatureOfCall.Should().Contain("New nature");
			result.Repaged.Should().BeFalse();
			_outbox.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_unit_the_cad_adds_is_put_on_the_call_and_paged_alone()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();

			var result = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Parsed = new CallEmailParseResult
				{
					Call = new Call { UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
					ParsedFields = CallEmailParsedFields.Resources, MatchKey = "2026-0042"
				}
			}, c => Task.FromResult(c));

			result.AddedResources.Should().Be(1);
			_unitDispatches.Verify(r => r.InsertAsync(It.Is<CallDispatchUnit>(d => d.CallId == 42 && d.UnitId == 7), It.IsAny<CancellationToken>(), true), Times.Once);
			_outbox.Verify(o => o.RecordAsync(It.IsAny<Call>(), It.Is<IEnumerable<string>>(u => !u.Any()), It.IsAny<IEnumerable<int>>(),
				It.Is<IEnumerable<int>>(u => u.SequenceEqual(new[] { 7 })), It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Once);
			_notes.Should().Contain(n => n.Note.Contains("Added: Engine 2"));
		}

		[Test]
		public async Task An_email_for_a_recently_closed_call_is_noted_on_it_and_creates_nothing()
		{
			var call = OpenCall("2026-0042");
			call.State = (int)CallStates.Closed;
			call.ClosedOn = DateTime.UtcNow.AddMinutes(-10);

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nMore info"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.ClosedNote);
			_created.Should().Be(0);
			_notes.Should().ContainSingle(n => n.CallId == 42 && n.Note.Contains("already closed"));
		}

		[Test]
		public async Task A_close_email_closes_the_call_and_sends_the_close_notice()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nCALL CLOSED"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);
			call.State.Should().Be((int)CallStates.Closed);
			call.ClosedByUserId.Should().Be("manager");
			call.CompletedNotes.Should().Be("Closed from CAD email.");
			_closure.Verify(c => c.NotifyCallClosedAsync(It.Is<Call>(x => x.CallId == 42), "manager", It.IsAny<CancellationToken>()), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<CallClosedEvent>(x => x.Call.CallId == 42)), Times.Once);
		}

		[Test]
		public async Task A_cancel_email_cancels_the_call()
		{
			var call = OpenCall("2026-0042");
			_config.CancelPhrases = "CANCELLED";

			(await ProcessAsync(Email("Call ID: 2026-0042\nCANCELLED"))).Outcome.Should().Be(CallEmailUpdateOutcomes.Cancelled);
			call.State.Should().Be((int)CallStates.Cancelled);
		}

		[Test]
		public async Task Incident_command_keeps_a_call_open_against_a_close_email()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			_closure.Setup(c => c.GetBlockingIncidentCommandAsync(DepartmentId, 42)).ReturnsAsync(new IncidentCommand());

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nCALL CLOSED"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.CloseBlocked);
			call.State.Should().Be((int)CallStates.Active);
			_saves.Should().Be(0);
			_notes.Should().ContainSingle(n => n.Note.Contains("incident command"));
		}

		[Test]
		public async Task A_close_email_for_a_call_resgrid_never_had_creates_nothing()
		{
			_config.ClosePhrases = "CALL CLOSED";

			var result = await ProcessAsync(Email("Call ID: 2026-0999\nCALL CLOSED"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.CloseUnmatched);
			_created.Should().Be(0);
			_ledger.Rows.Should().ContainSingle(r => r.Outcome == (int)CallEmailUpdateOutcomes.CloseUnmatched && r.CallId == null);
		}

		[Test]
		public async Task An_email_that_cannot_take_the_lock_does_nothing_and_says_busy()
		{
			_ledger.LockBusy = true;

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nFire"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Busy);
			_created.Should().Be(0);
		}

		[Test]
		public async Task An_original_and_its_correction_handled_together_make_one_call()
		{
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Returns(async (Call call, CancellationToken ct) =>
				{
					await Task.Delay(50);
					lock (_calls)
					{
						if (call.CallId <= 0)
						{
							call.CallId = 500 + _calls.Count;
							_calls.Add(call);
						}
					}

					return call;
				});

			var results = await Task.WhenAll(ProcessAsync(Email("Call ID: 2026-0042\nFire at 12 Oak St")), ProcessAsync(Email("Call ID: 2026-0042\nFire at 12 Oak Ave")));

			_created.Should().Be(1);
			results.Select(r => r.Outcome).Should().BeEquivalentTo(new[] { CallEmailUpdateOutcomes.Created, CallEmailUpdateOutcomes.Updated });
		}

		[Test]
		public async Task The_active911_preset_replaces_the_notes_and_leaves_the_address_as_before()
		{
			_config = EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.Active911);
			var call = OpenCall("2026-000777-FIRE", "100 OAK ST");

			var result = await ProcessAsync(Email("CALL: Structure Fire\nADDR1: 100 ELM ST\nID: 2026-000777-FIRE\nNARR: CORRECTED", "ACTIVE 9-1-1"), CallEmailTypes.Active911);

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			call.Address.Should().Be("100 OAK ST", "the built-in behaviour never changed the address");
			call.Notes.Should().Contain("100 ELM ST", "the notes are replaced with the new email, as before");
			result.Repaged.Should().BeFalse();
			_notes.Should().Contain(n => n.Note.Contains("100 ELM ST") && n.Note.Contains("department setting"));
		}

		[Test]
		public async Task Parkland_county_2_finds_its_open_call_by_name_and_time_and_pages_the_next_alarm()
		{
			_config = EmailCallUpdatePresets.ForFormat((int)CallEmailTypes.ParklandCounty2);
			var loggedOn = new DateTime(2018, 5, 14, 16, 53, 16, DateTimeKind.Utc);
			var call = new Call { CallId = 42, DepartmentId = DepartmentId, State = (int)CallStates.Active, Name = "72C01 - Water Rescue", LoggedOn = loggedOn, Priority = (int)CallPriority.High };
			_calls.Add(call);

			var parsed = new CallEmailParseResult
			{
				Call = new Call { Name = "72C01 - Water Rescue", LoggedOn = loggedOn, Priority = (int)CallPriority.Emergency, Notes = "WCT2ndPage" },
				ParsedFields = CallEmailParsedFields.Priority | CallEmailParsedFields.Name | CallEmailParsedFields.Notes,
				MatchKey = "72C01 - Water Rescue|20180514165316",
				MatchKeyIsComposite = true,
				LegacyMatch = existing => existing.Name == "72C01 - Water Rescue" && existing.LoggedOn == loggedOn
			};

			var result = await _service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = Email("x"), Config = _config, Parsed = parsed, Source = CallEmailUpdateSources.DispatchAddress, FormatType = (int)CallEmailTypes.ParklandCounty2
			}, c => { _created++; return Task.FromResult(c); });

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			_created.Should().Be(0);
			call.Priority.Should().Be((int)CallPriority.Emergency);
			call.DispatchCount.Should().Be(1);
			result.Repaged.Should().BeTrue();
			_ledger.Rows.Should().Contain(r => r.CallId == 42 && r.CadCallId == "72C01 - Water Rescue|20180514165316", "the next page finds the call through the ledger");
		}

		// ---- labeled fields ----

		[Test]
		public async Task The_labeled_fields_format_reads_the_departments_labels()
		{
			var labels = new EmailLabeledFieldsConfig
			{
				CallId = "Call ID|Incident #", Address = "Address", City = "City", Type = "Type", Priority = "Priority", Units = "Units",
				Coordinates = "Lat/Lon", CrossStreets = "X-St", Nature = "Narrative"
			};
			var types = new List<CallType> { new CallType { CallTypeId = 3, Type = "Structure Fire" } };
			var priorities = new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 9, Name = "Emergency" } };
			var units = new List<Unit> { new Unit { UnitId = 7, Name = "Engine 2" }, new Unit { UnitId = 8, Name = "Ladder 1" } };
			var email = Email("Incident #: 26-1234   Type: structure fire\nAddress: 12 Oak Ave  City: Springfield\nX-St: Main / Elm\nLat/Lon: 40.123456, -75.654321\n" +
				"Priority: Emergency\nUnits: Engine 2, Ladder 1, Rescue 9\nNarrative: Smoke from the roof", "Dispatch 26-1234");

			var parsed = await new CallEmailFactory().ParseCallEmailAsync(CallEmailTypes.LabeledFields, email, "manager", new List<Resgrid.Model.Identity.IdentityUser>(),
				new Department { DepartmentId = DepartmentId }, units, 1, priorities, types, Mock.Of<IGeoLocationProvider>(), labels);

			parsed.MatchKey.Should().Be("26-1234");
			parsed.Call.ExternalIdentifier.Should().Be("26-1234");
			parsed.Call.Address.Should().Be("12 Oak Ave, Springfield");
			parsed.Call.Type.Should().Be("Structure Fire");
			parsed.Call.CallTypeId.Should().Be(3);
			parsed.Call.Priority.Should().Be(9);
			parsed.Call.GeoLocationData.Should().Be("40.123456,-75.654321");
			parsed.Call.NatureOfCall.Should().Be("Smoke from the roof");
			parsed.Call.Notes.Should().Contain("Cross streets: Main / Elm");
			parsed.Call.UnitDispatches.Select(u => u.UnitId).Should().BeEquivalentTo(new[] { 7, 8 }, "units are matched by name; unknown ones are skipped");
			parsed.Call.Name.Should().Be("Dispatch 26-1234", "no name label: the subject");
			parsed.Has(CallEmailParsedFields.Priority).Should().BeTrue();
			parsed.Has(CallEmailParsedFields.Address).Should().BeTrue();
		}

		[Test]
		public void A_label_word_inside_prose_is_not_a_label()
		{
			// Found in the 2026-11-23 walk: "corrected address (" in the narrative was read as the Address label and cut the nature.
			var labels = new EmailLabeledFieldsConfig { CallId = "Call ID", Address = "Address", Type = "Type", Nature = "Narrative" };

			var values = LabeledFieldsTemplate.ReadLabeledValues(labels, null,
				"Call ID: 26-77001   Type: Structure Fire\nAddress: 14 Oak Ave\nNarrative: corrected address (caller gave the wrong type of building)");

			values[LabeledEmailFields.Nature].Should().Be("corrected address (caller gave the wrong type of building)");
			values[LabeledEmailFields.Address].Should().Be("14 Oak Ave");
			values[LabeledEmailFields.Type].Should().Be("Structure Fire");
			values[LabeledEmailFields.CallId].Should().Be("26-77001");
		}

		[Test]
		public async Task Without_labels_the_labeled_fields_format_reads_like_generic()
		{
			var email = Email("Fire at 12 Oak St", "Dispatch");

			var parsed = await new CallEmailFactory().ParseCallEmailAsync(CallEmailTypes.LabeledFields, email, "manager", new List<Resgrid.Model.Identity.IdentityUser>(),
				new Department(), new List<Unit>(), 1, new List<DepartmentCallPriority>(), new List<CallType>(), Mock.Of<IGeoLocationProvider>(), null);

			parsed.Call.Name.Should().Be("Dispatch");
			parsed.Call.NatureOfCall.Should().Be("Fire at 12 Oak St");
			parsed.MatchKey.Should().BeNull();
		}

		[Test]
		public async Task A_format_without_its_own_parser_is_described_from_the_call_it_builds()
		{
			var email = Email("CALL: Structure Fire\nADDR1: 100 OAK ST\nID: 2026-000777-FIRE\nNARR: SMOKE", "ACTIVE 9-1-1");

			var parsed = await new CallEmailFactory().ParseCallEmailAsync(CallEmailTypes.Active911, email, "manager", new List<Resgrid.Model.Identity.IdentityUser>(),
				new Department { DepartmentId = 5 }, new List<Unit>(), 1, new List<DepartmentCallPriority>(), new List<CallType>(), Mock.Of<IGeoLocationProvider>(), null);

			parsed.MatchKey.Should().Be("2026-000777-FIRE");
			parsed.Has(CallEmailParsedFields.Address).Should().BeTrue();
			parsed.Has(CallEmailParsedFields.Priority).Should().BeFalse("the template kept the department default");
			parsed.Has(CallEmailParsedFields.Resources).Should().BeFalse("the roster on Dispatches is not something the email said");
			parsed.Call.CallId.Should().Be(0);
		}
	}
}
