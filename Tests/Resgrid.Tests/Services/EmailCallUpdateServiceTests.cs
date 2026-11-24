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
		private Mock<ICallDispatchStatusService> _dispatchStatus;
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
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.ReturnsAsync(new CallBroadcastOutboxEntry { Persisted = true });
			_outbox.Setup(o => o.SendPendingForCallAsync(It.IsAny<Call>(), It.IsAny<CallBroadcastOutboxEntry>())).ReturnsAsync(1);
			_closure = new Mock<ICallClosureService>();
			_events = new Mock<IEventAggregator>();
			_geo = new Mock<IGeoLocationProvider>();
			_geo.Setup(g => g.GetLatLonFromAddress(It.IsAny<string>())).ReturnsAsync("40.1,-75.2");
			_unitDispatches = new Mock<ICallDispatchUnitRepository>();

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 7, Name = "Engine 2", DepartmentId = DepartmentId } });

			_dispatchStatus = new Mock<ICallDispatchStatusService>();

			_service = EmailCallUpdateServices.Create(_callsService.Object, _ledger, () => _config, _outbox, _closure, _events, geo: _geo.Object,
				unitDispatches: _unitDispatches, units: units.Object, dispatchStatus: _dispatchStatus);
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
			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(),It.Is<Call>(c => c.CallId == 42), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
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
			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.Is<IEnumerable<string>>(u => !u.Any()), It.IsAny<IEnumerable<int>>(),
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

		// ---- redeliveries, the write-ahead ledger row, baselines and pages (PR #591 review) ----

		private Task<EmailCallUpdateResult> ApplyAsync(CallEmail email, CallEmailParseResult parsed) =>
			_service.ProcessAsync(new EmailCallUpdateRequest
			{
				DepartmentId = DepartmentId, Email = email, Parsed = parsed, Config = _config, Source = CallEmailUpdateSources.DispatchAddress,
				Department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" }
			}, async c =>
			{
				_created++;
				c.DepartmentId = DepartmentId;
				return await _callsService.Object.SaveCallAsync(c, CancellationToken.None);
			});

		private static CallEmailParseResult ParsedWith(Call call, CallEmailParsedFields fields) =>
			new CallEmailParseResult { Call = call, ParsedFields = fields, MatchKey = "2026-0042" };

		private void PopulateWith(Func<Call, Task<Call>> populate)
		{
			_callsService.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.Returns((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => populate(call));
		}

		[Test]
		public async Task A_redelivered_update_changes_notes_and_pages_nothing_again()
		{
			var call = OpenCall("2026-0042");
			_config.NaturePolicy = (int)CadTextPolicies.Append;
			_config.RepageWhen = (int)CadRepageRules.EveryUpdate;
			var email = Email("Call ID: 2026-0042\nSecond page");

			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			var nature = call.NatureOfCall;
			var notes = _notes.Count;
			var dispatchCount = call.DispatchCount;

			// The caller failed after the update went through, so the provider delivers the same email again.
			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);
			again.CreatedNewCall.Should().BeFalse();
			again.Call.CallId.Should().Be(42);
			call.NatureOfCall.Should().Be(nature, "the text is not appended a second time");
			call.DispatchCount.Should().Be(dispatchCount, "the call is not paged as its next alarm again");
			_notes.Should().HaveCount(notes);
			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Once);
			_ledger.Rows.Should().ContainSingle(r => r.MessageId == email.MessageId).Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Updated);
		}

		[Test]
		public async Task Two_different_emails_sent_at_the_same_time_both_apply()
		{
			var call = OpenCall("2026-0042");
			var sent = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

			(await ProcessAsync(Email("Call ID: 2026-0042\nFirst nature", sentOn: sent))).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			(await ProcessAsync(Email("Call ID: 2026-0042\nSecond nature", sentOn: sent))).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);

			call.NatureOfCall.Should().Contain("Second nature");
		}

		[Test]
		public async Task A_redelivered_note_only_email_writes_no_second_note()
		{
			_config.LocationPolicy = (int)CadFieldPolicies.NoteOnly;
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			OpenCall("2026-0042");
			var email = Email("x");

			(await ApplyAsync(email, ParsedWith(new Call { Address = "12 Oak Ave" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);
			(await ApplyAsync(email, ParsedWith(new Call { Address = "12 Oak Ave" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);

			_notes.Should().ContainSingle();
		}

		[Test]
		public async Task A_redelivered_first_email_creates_no_second_call()
		{
			var email = Email("Call ID: 2026-0042\nFire");
			email.MessageId = new string('m', 600);

			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.Created);
			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);
			_created.Should().Be(1);
			_ledger.Rows.Should().ContainSingle().Which.MessageId.Should().HaveLength(500, "the column holds 500 characters and nothing is added to a clipped id");
		}

		[Test]
		public async Task An_update_whose_ledger_row_cannot_be_written_changes_nothing()
		{
			OpenCall("2026-0042");
			_ledger.FailInserts = true;

			Func<Task> act = () => ProcessAsync(Email("Call ID: 2026-0042\nNew nature"));

			await act.Should().ThrowAsync<InvalidOperationException>("the caller retries an email nothing was done for");
			_saves.Should().Be(0);
			_notes.Should().BeEmpty();
			_outbox.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_redelivery_of_a_change_whose_steps_were_never_recorded_settles_it_as_the_cads_value()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St" }, CallEmailParsedFields.Address));

			// The CAD's correction saves, but the ledger cannot record that it did: its row stays Applying, which proves nothing.
			_ledger.FailOutcomeMoves = true;
			var correction = Email("x");
			(await ApplyAsync(correction, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			_ledger.Rows.Should().Contain(r => r.Outcome == (int)CallEmailUpdateOutcomes.Applying);
			_ledger.FailOutcomeMoves = false;

			// Its redelivery finds the call already holding 14 Oak St: no change, and the CAD's value from now on.
			(await ApplyAsync(correction, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);
			var next = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));

			next.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupLocation && c.Applied);
			created.Call.Address.Should().Be("16 Oak St");
		}

		[Test]
		public async Task A_value_a_dispatcher_typed_is_not_taken_as_the_cads_from_an_update_whose_save_failed()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St" }, CallEmailParsedFields.Address));

			// The CAD's change to 14 Oak St writes its row, then the save fails.
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			Func<Task> failed = () => ApplyAsync(Email("x"), ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));
			await failed.Should().ThrowAsync<TimeoutException>();
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call call, CancellationToken ct) => call);

			// A dispatcher then types the same address.
			created.Call.Address = "14 Oak St";
			var next = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));

			created.Call.Address.Should().Be("14 Oak St", "the dispatcher's edit wins; the failed update's row proves nothing");
			next.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupLocation && c.Reason == EmailCallUpdateService.ReasonEdited);
		}

		[Test]
		public async Task A_dispatcher_edit_after_an_unrecorded_change_is_still_kept()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St" }, CallEmailParsedFields.Address));
			_ledger.FailOutcomeMoves = true;
			await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));
			_ledger.FailOutcomeMoves = false;

			created.Call.Address = "99 Elm St";
			var next = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));

			created.Call.Address.Should().Be("99 Elm St");
			next.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupLocation && c.Reason == EmailCallUpdateService.ReasonEdited);
		}

		[Test]
		public async Task A_redelivery_whose_steps_were_never_recorded_runs_again_without_repeating_its_text()
		{
			var call = OpenCall("2026-0042");
			var email = Email("Call ID: 2026-0042\nNew nature");
			_ledger.FailOutcomeMoves = true;
			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			_ledger.FailOutcomeMoves = false;
			var (nature, notes) = (call.NatureOfCall, call.Notes);

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange, "its row never said whether the save went through, so the email ran again and found it had");
			call.NatureOfCall.Should().Be(nature);
			call.Notes.Should().Be(notes, "appended text the call already ends with is not appended again");
			_notes.Should().ContainSingle();
			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);
		}

		[Test]
		public async Task An_email_whose_save_failed_is_handled_again_on_redelivery()
		{
			var stored = OpenCall("2026-0042");
			Call Copy(Call c) => new Call
			{
				CallId = c.CallId, DepartmentId = c.DepartmentId, State = c.State, Name = c.Name, NatureOfCall = c.NatureOfCall, Notes = c.Notes, Address = c.Address,
				GeoLocationData = c.GeoLocationData, IncidentNumber = c.IncidentNumber, ExternalIdentifier = c.ExternalIdentifier, Priority = c.Priority, LoggedOn = c.LoggedOn
			};
			var fail = true;
			_callsService.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => id == stored.CallId ? Copy(stored) : null);
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Returns((Call call, CancellationToken ct) =>
				{
					if (fail)
						throw new TimeoutException();
					_saves++;
					(stored.Name, stored.NatureOfCall, stored.Notes) = (call.Name, call.NatureOfCall, call.Notes);
					return Task.FromResult(call);
				});
			var email = Email("Call ID: 2026-0042\nNew nature");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.Rows.Should().ContainSingle(r => r.Outcome == (int)CallEmailUpdateOutcomes.Applying, "the row went in before the save that failed");
			fail = false;

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated, "the first attempt never changed the call");
			stored.NatureOfCall.Should().Contain("New nature");
			_saves.Should().Be(1);
		}

		[Test]
		public async Task A_pin_a_dispatcher_moved_is_kept_when_the_cad_corrects_its_own()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St", GeoLocationData = "40.0,-75.0" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));
			created.Call.GeoLocationData = "40.5,-75.5";

			var result = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "12 Oak St", GeoLocationData = "40.01,-75.01" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));

			created.Call.GeoLocationData.Should().Be("40.5,-75.5");
			result.Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);
		}

		[Test]
		public async Task The_cad_corrects_a_pin_nobody_moved()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St", GeoLocationData = "40.0,-75.0" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));

			var result = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "12 Oak St", GeoLocationData = "40.01,-75.01" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));

			created.Call.GeoLocationData.Should().Be("40.01,-75.01");
			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
		}

		[Test]
		public async Task Notes_a_dispatcher_edited_before_the_first_update_are_kept()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.NotesPolicy = (int)CadTextPolicies.Replace;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Notes = "CAD notes" }, CallEmailParsedFields.Notes));
			created.Call.Notes = "Dispatcher: gate code 1234";

			var result = await ApplyAsync(Email("x"), ParsedWith(new Call { Notes = "CAD notes, corrected" }, CallEmailParsedFields.Notes));

			created.Call.Notes.Should().Be("Dispatcher: gate code 1234");
			result.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupNotes && c.Reason == EmailCallUpdateService.ReasonEdited);
		}

		[Test]
		public async Task Notes_nobody_edited_take_the_cads_replacement()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.NotesPolicy = (int)CadTextPolicies.Replace;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Notes = "CAD notes" }, CallEmailParsedFields.Notes));

			await ApplyAsync(Email("x"), ParsedWith(new Call { Notes = "CAD notes, corrected" }, CallEmailParsedFields.Notes));

			created.Call.Notes.Should().Be("CAD notes, corrected");
		}

		[Test]
		public async Task The_page_for_an_added_unit_is_recorded_before_its_row_and_survives_a_failed_reload()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var order = new List<string>();
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.Callback(() => order.Add("outbox"))
				.ReturnsAsync(new CallBroadcastOutboxEntry { Persisted = true });
			_unitDispatches.Setup(r => r.InsertAsync(It.IsAny<CallDispatchUnit>(), It.IsAny<CancellationToken>(), true))
				.Callback(() => order.Add("unit"))
				.ReturnsAsync((CallDispatchUnit row, CancellationToken ct, bool first) => row);

			// The first load (the call's resources) works; the reload to send the page times out.
			var loads = 0;
			PopulateWith(c => ++loads > 1 ? Task.FromException<Call>(new TimeoutException()) : Task.FromResult(c));

			var result = await ApplyAsync(Email("x"), new CallEmailParseResult
			{
				Call = new Call { UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			});

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			result.AddedResources.Should().Be(1);
			order.Should().Equal(new[] { "outbox", "unit" });
			_outbox.Verify(o => o.SendPendingForCallAsync(It.IsAny<Call>(), It.IsAny<CallBroadcastOutboxEntry>()), Times.Never(),
				"the reload failed, so worker 12 sends the page the outbox already holds");
		}

		[Test]
		public async Task A_re_page_goes_to_everyone_on_the_call_and_what_the_email_adds()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.EveryUpdate;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = 42, UnitId = 3 } };
			call.GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = 42, DepartmentGroupId = 9 } };

			await ApplyAsync(Email("x"), new CallEmailParseResult
			{
				Call = new Call { UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 3 }, new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			});

			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(),It.Is<Call>(c => c.CallId == 42), It.IsAny<IEnumerable<string>>(), It.Is<IEnumerable<int>>(g => g.SequenceEqual(new[] { 9 })),
				It.Is<IEnumerable<int>>(u => u.OrderBy(x => x).SequenceEqual(new[] { 3, 7 })), It.IsAny<IEnumerable<int>>(), "manager"), Times.Once);
		}

		[Test]
		public async Task A_release_that_fails_still_sends_the_close_notice_and_is_retried_alone_on_redelivery()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			PopulateWith(c =>
			{
				c.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = c.CallId, UnitId = 7 } };
				return Task.FromResult(c);
			});
			var releases = 0;
			_dispatchStatus.Setup(s => s.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
				.Returns(() => ++releases == 1 ? Task.FromException(new TimeoutException()) : Task.CompletedTask);
			var email = Email("Call ID: 2026-0042\nCALL CLOSED");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>("the release is owed, so the delivery must not be acknowledged");
			call.State.Should().Be((int)CallStates.Closed, "a failed release never reopens the call");
			_closure.Verify(c => c.NotifyCallClosedAsync(It.Is<Call>(x => x.CallId == 42), "manager", It.IsAny<CancellationToken>()), Times.Once, "the failed release does not cost the notice");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Notified);
			_notes.Should().BeEmpty("the note waits for the release");

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);
			releases.Should().Be(2, "only the release is retried");
			_closure.Verify(c => c.NotifyCallClosedAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once, "the notice is not sent again");
			_notes.Should().ContainSingle();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
		}

		[Test]
		public async Task A_close_notice_that_fails_is_retried_on_redelivery_without_releasing_again()
		{
			OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			ReleaseUnitSeven();
			var notices = 0;
			_closure.Setup(c => c.NotifyCallClosedAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns(() => ++notices == 1 ? Task.FromException<int>(new TimeoutException()) : Task.FromResult(1));
			var email = Email("Call ID: 2026-0042\nCALL CLOSED");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.FollowedUp, "the release is done; the notice is owed");

			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);

			notices.Should().Be(2);
			_dispatchStatus.Verify(s => s.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Once);
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
		}

		[Test]
		public async Task A_redelivered_close_releases_nothing_on_a_call_that_was_reopened()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			ReleaseUnitSeven();
			ThrowOnFirstCloseEvent();
			var email = Email("Call ID: 2026-0042\nCALL CLOSED");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved);

			// A dispatcher reopens the call; Engine 2 is still working it.
			(call.State, call.ClosedOn, call.CompletedNotes) = ((int)CallStates.Active, null, null);

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);
			call.State.Should().Be((int)CallStates.Active, "the redelivery never closes it again");
			_dispatchStatus.Verify(s => s.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Never);
			_closure.Verify(c => c.NotifyCallClosedAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
		}

		// ---- the steps a redelivery finishes (PR #591 review, round 2) ----

		private void ReleaseUnitSeven() => PopulateWith(c =>
		{
			c.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = c.CallId, UnitId = 7 } };
			return Task.FromResult(c);
		});

		private void ThrowOnFirstCloseEvent()
		{
			var thrown = false;
			_events.Setup(e => e.SendMessage(It.IsAny<CallClosedEvent>())).Callback(() =>
			{
				if (thrown)
					return;
				thrown = true;
				throw new TimeoutException("the process stopped");
			});
		}

		private void VerifyReleasedAndNotifiedOnce()
		{
			_dispatchStatus.Verify(s => s.ApplyReleaseStatusesAsync(It.Is<Call>(c => c.CallId == 42), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Once);
			_closure.Verify(c => c.NotifyCallClosedAsync(It.Is<Call>(x => x.CallId == 42), "manager", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_close_interrupted_after_its_save_releases_and_notifies_on_redelivery()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			ReleaseUnitSeven();
			ThrowOnFirstCloseEvent();
			var email = Email("Call ID: 2026-0042\nCALL CLOSED");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<TimeoutException>();
			call.State.Should().Be((int)CallStates.Closed);
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved, "the close is saved; its release and notice are not");

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);
			VerifyReleasedAndNotifiedOnce();
			_notes.Should().ContainSingle();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);
			VerifyReleasedAndNotifiedOnce();
		}

		[Test]
		public async Task A_close_saved_before_its_row_could_say_so_is_finished_from_the_close_it_wrote()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			ReleaseUnitSeven();
			ThrowOnFirstCloseEvent();
			_ledger.FailOutcomeMoves = true;
			var email = Email("Call ID: 2026-0042\nCALL CLOSED");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.FailOutcomeMoves = false;
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Applying);

			var again = await ProcessAsync(email);

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed, "the call carries the close this engine writes, so the save went through");
			call.State.Should().Be((int)CallStates.Closed);
			VerifyReleasedAndNotifiedOnce();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
		}

		[Test]
		public async Task An_update_interrupted_after_its_save_records_its_page_and_adds_its_unit_on_redelivery()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.Returns(() => ++records == 1 ? Task.FromException<CallBroadcastOutboxEntry>(new TimeoutException("the process stopped")) : Task.FromResult(new CallBroadcastOutboxEntry { Persisted = true }));
			var inserted = 0;
			_unitDispatches.Setup(r => r.InsertAsync(It.IsAny<CallDispatchUnit>(), It.IsAny<CancellationToken>(), true))
				.Callback(() => inserted++)
				.ReturnsAsync((CallDispatchUnit row, CancellationToken ct, bool first) => row);
			CallEmailParseResult Parsed() => new CallEmailParseResult
			{
				Call = new Call { Address = "14 Oak St", UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Address | CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			};
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Parsed());
			await first.Should().ThrowAsync<TimeoutException>();
			call.Address.Should().Be("14 Oak St");
			inserted.Should().Be(0);
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved);

			var again = await ApplyAsync(email, Parsed());

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			again.AddedResources.Should().Be(1);
			inserted.Should().Be(1);
			records.Should().Be(2, "the page the interrupted delivery never recorded is recorded now");
			_saves.Should().Be(1, "the address is the call's already and is not saved again");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Updated);
		}

		[Test]
		public async Task An_update_that_stopped_before_its_row_said_it_saved_still_sends_the_page_it_owed()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var call = OpenCall("2026-0042");
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.Returns(() => ++records == 1 ? Task.FromException<CallBroadcastOutboxEntry>(new TimeoutException("the process stopped")) : Task.FromResult(new CallBroadcastOutboxEntry { Persisted = true }));

			// The address change saves, the row cannot say so, and the process stops before the page is recorded.
			_ledger.FailMovesTo.Add((int)CallEmailUpdateOutcomes.Saved);
			var email = Email("x");
			Func<Task> first = () => ApplyAsync(email, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.FailMovesTo.Clear();
			call.Address.Should().Be("14 Oak St");

			var again = await ApplyAsync(email, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			again.Repaged.Should().BeTrue("the location change its earlier run made still pages under the location rule");
			records.Should().Be(2);
		}

		[Test]
		public async Task A_redelivery_after_its_page_was_recorded_pages_nothing_again_even_after_a_dispatcher_edit()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.EveryUpdate;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var attempts = 0;
			_unitDispatches.Setup(r => r.InsertAsync(It.IsAny<CallDispatchUnit>(), It.IsAny<CancellationToken>(), true))
				.Returns((CallDispatchUnit row, CancellationToken ct, bool first) =>
					++attempts == 1 ? Task.FromException<CallDispatchUnit>(new TimeoutException("the process stopped")) : Task.FromResult(row));
			CallEmailParseResult Parsed() => new CallEmailParseResult
			{
				Call = new Call { NatureOfCall = "Working fire", UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Nature | CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			};
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Parsed());
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.FollowedUp);
			var dispatchCount = call.DispatchCount;
			call.NatureOfCall = "Dispatcher: second alarm requested";

			var again = await ApplyAsync(email, Parsed());

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Once, "its page is in the outbox already");
			call.DispatchCount.Should().Be(dispatchCount, "the alarm level is not raised again");
			call.NatureOfCall.Should().Be("Dispatcher: second alarm requested", "the change is not applied again");
			attempts.Should().Be(2);
		}

		[Test]
		public async Task A_pin_correction_whose_save_failed_is_applied_on_redelivery()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St", GeoLocationData = "40.0,-75.0" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));
			var fail = true;
			_callsService.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Returns((Call call, CancellationToken ct) => fail ? Task.FromException<Call>(new TimeoutException()) : Task.FromResult(call));
			CallEmailParseResult Pin() => ParsedWith(new Call { Address = "12 Oak St", GeoLocationData = "40.01,-75.01" }, CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates);
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Pin());
			await first.Should().ThrowAsync<TimeoutException>();
			fail = false;
			created.Call.GeoLocationData = "40.0,-75.0";

			var again = await ApplyAsync(email, Pin());

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated, "the unchanged address is no evidence the pin was saved");
			created.Call.GeoLocationData.Should().Be("40.01,-75.01");
		}

		[Test]
		public async Task A_close_that_incident_command_blocked_does_not_make_an_earlier_correction_stale()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.ClosePhrases = "CALL CLOSED";
			var call = OpenCall("2026-0042");
			var ten = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
			(await ApplyAsync(Email("x", sentOn: ten), ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);

			_closure.Setup(c => c.GetBlockingIncidentCommandAsync(DepartmentId, 42)).ReturnsAsync(new IncidentCommand());
			(await ApplyAsync(Email("CALL CLOSED", sentOn: ten.AddMinutes(20)), ParsedWith(new Call(), CallEmailParsedFields.None))).Outcome
				.Should().Be(CallEmailUpdateOutcomes.CloseBlocked);
			_closure.Setup(c => c.GetBlockingIncidentCommandAsync(DepartmentId, 42)).ReturnsAsync((IncidentCommand)null);

			var correction = await ApplyAsync(Email("x", sentOn: ten.AddMinutes(10)), ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));

			correction.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated, "the blocked close changed nothing, so it sets no newer state");
			call.Address.Should().Be("16 Oak St");
		}

		[Test]
		public async Task A_new_address_replaces_a_pin_a_dispatcher_moved_and_says_so()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St", GeoLocationData = "40.0,-75.0" },
				CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));
			created.Call.GeoLocationData = "40.5,-75.5";

			await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "99 Elm St", GeoLocationData = "41.0,-76.0" }, CallEmailParsedFields.Address | CallEmailParsedFields.Coordinates));

			created.Call.Address.Should().Be("99 Elm St");
			created.Call.GeoLocationData.Should().Be("41.0,-76.0", "a new address is a new place");
			_notes.Should().ContainSingle().Which.Note.Should().Contain("map pin a dispatcher had moved was replaced");
		}

		[Test]
		public async Task A_stale_email_whose_note_could_not_be_written_writes_it_on_redelivery()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			OpenCall("2026-0042");
			var ten = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
			await ApplyAsync(Email("x", sentOn: ten.AddMinutes(20)), ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));
			var notesBefore = _notes.Count;

			var failNote = true;
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote note, CancellationToken ct) =>
				{
					if (failNote)
					{
						failNote = false;
						return Task.FromException<CallNote>(new TimeoutException());
					}

					_notes.Add(note);
					return Task.FromResult(note);
				});
			var late = Email("x", sentOn: ten.AddMinutes(10));

			Func<Task> first = () => ApplyAsync(late, ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>("the note is owed, so the delivery must not be acknowledged");
			_ledger.Rows.Should().ContainSingle(r => r.MessageId == late.MessageId).Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Noting);

			(await ApplyAsync(late, ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.Stale);

			_notes.Should().HaveCount(notesBefore + 1);
			_notes.Last().Note.Should().Contain("arrived after a newer one");
			(await ApplyAsync(late, ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.AlreadyHandled);
		}

		// ---- PR #591 review, round 3 ----

		[Test]
		public async Task A_note_whose_insert_went_through_counts_as_written()
		{
			OpenCall("2026-0042");
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote note, CancellationToken ct) =>
				{
					note.CallNoteId = 9;
					_notes.Add(note);
					return Task.FromException<CallNote>(new TimeoutException("the search projection failed"));
				});

			(await ProcessAsync(Email("Call ID: 2026-0042\nNew nature"))).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated, "the note is on the call; writing it again would duplicate it");
			_notes.Should().ContainSingle();
		}

		[Test]
		public async Task An_update_whose_note_could_not_be_written_is_not_acknowledged_and_its_redelivery_writes_it()
		{
			var call = OpenCall("2026-0042");
			var failNote = true;
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote note, CancellationToken ct) =>
				{
					if (failNote)
						return Task.FromException<CallNote>(new TimeoutException());
					_notes.Add(note);
					return Task.FromResult(note);
				});
			var email = Email("Call ID: 2026-0042\nNew nature");

			Func<Task> first = () => ProcessAsync(email);
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>();
			call.NatureOfCall.Should().Contain("New nature");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved);
			failNote = false;

			(await ProcessAsync(email)).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);

			_saves.Should().Be(1, "the change is not applied again");
			_notes.Should().ContainSingle();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Updated);
			_events.Verify(e => e.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once,
				"the refresh (and the workflows and bus messages behind it) is raised once, by the delivery that finished the update");
		}

		[Test]
		public async Task A_page_the_outbox_could_not_record_stops_the_update_until_its_redelivery_records_it()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.LocationOrPriorityChanged;
			var call = OpenCall("2026-0042");
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(), It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.ReturnsAsync(() => new CallBroadcastOutboxEntry { Persisted = ++records > 1 });
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>("a page nobody holds is never sent from here nor marked done");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved);
			_outbox.Verify(o => o.SendPendingForCallAsync(It.IsAny<Call>(), It.IsAny<CallBroadcastOutboxEntry>()), Times.Never);

			var again = await ApplyAsync(email, ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address));

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			again.Repaged.Should().BeTrue();
			_outbox.Verify(o => o.SendPendingForCallAsync(It.Is<Call>(c => c.CallId == 42), It.IsAny<CallBroadcastOutboxEntry>()), Times.Once);
			call.Address.Should().Be("14 Oak St");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Updated);
		}

		[Test]
		public async Task A_resumed_update_sends_an_added_units_page_at_once()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.Returns(() => ++records == 1 ? Task.FromException<CallBroadcastOutboxEntry>(new TimeoutException("the process stopped")) : Task.FromResult(new CallBroadcastOutboxEntry { Persisted = true }));
			CallEmailParseResult Parsed() => new CallEmailParseResult
			{
				Call = new Call { Address = "14 Oak St", UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Address | CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			};
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Parsed());
			await first.Should().ThrowAsync<TimeoutException>();

			(await ApplyAsync(email, Parsed())).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);

			_outbox.Verify(o => o.SendPendingForCallAsync(It.Is<Call>(c => c.CallId == 42), It.IsAny<CallBroadcastOutboxEntry>()), Times.Once,
				"Engine 2's page goes out now, not only when worker 12 next runs");
		}

		[Test]
		public async Task An_added_unit_whose_page_the_outbox_could_not_record_is_not_put_on_the_call_until_its_redelivery_records_it()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(), It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.ReturnsAsync(() => new CallBroadcastOutboxEntry { Persisted = ++records > 1 });
			CallEmailParseResult Parsed() => new CallEmailParseResult
			{
				Call = new Call { UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			};
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Parsed());
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>("Engine 2's page is held by nobody");
			_unitDispatches.Verify(r => r.InsertAsync(It.IsAny<CallDispatchUnit>(), It.IsAny<CancellationToken>(), true), Times.Never,
				"written now, Engine 2 would no longer be an addition and its redelivery would page nobody");
			_outbox.Verify(o => o.SendPendingForCallAsync(It.IsAny<Call>(), It.IsAny<CallBroadcastOutboxEntry>()), Times.Never,
				"sent before its row, the outbox would leave Engine 2 out (it pages only resources the call carries)");

			var again = await ApplyAsync(email, Parsed());

			again.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			again.AddedResources.Should().Be(1);
			_unitDispatches.Verify(r => r.InsertAsync(It.Is<CallDispatchUnit>(d => d.CallId == 42 && d.UnitId == 7), It.IsAny<CancellationToken>(), true), Times.Once);
			_outbox.Verify(o => o.RecordOnceAsync(It.IsAny<string>(), It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(),
				It.Is<IEnumerable<int>>(u => u.SequenceEqual(new[] { 7 })), It.IsAny<IEnumerable<int>>(), It.IsAny<string>()), Times.Exactly(2));
			_outbox.Verify(o => o.SendPendingForCallAsync(It.Is<Call>(c => c.CallId == 42), It.IsAny<CallBroadcastOutboxEntry>()), Times.Once,
				"the recorded page goes out after Engine 2's row is in");
		}

		[Test]
		public async Task A_redelivery_records_the_page_under_the_id_the_first_delivery_used()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.Never;
			var call = OpenCall("2026-0042");
			call.UnitDispatches = new List<CallDispatchUnit>();
			var ids = new List<string>();
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(), It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.ReturnsAsync((string id, Call c, IEnumerable<string> u, IEnumerable<int> g, IEnumerable<int> un, IEnumerable<int> r, string by) =>
				{
					ids.Add(id);
					return new CallBroadcastOutboxEntry { CallBroadcastOutboxId = id, Persisted = true };
				});
			// The FollowedUp move fails and so does the note: the row stays Saved, the delivery is retried.
			_ledger.FailMovesTo.Add((int)CallEmailUpdateOutcomes.FollowedUp);
			var failNote = true;
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote note, CancellationToken ct) =>
				{
					if (failNote)
						return Task.FromException<CallNote>(new TimeoutException());
					_notes.Add(note);
					return Task.FromResult(note);
				});
			CallEmailParseResult Parsed() => new CallEmailParseResult
			{
				Call = new Call { Address = "14 Oak St", UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 7 } } },
				ParsedFields = CallEmailParsedFields.Address | CallEmailParsedFields.Resources, MatchKey = "2026-0042"
			};
			var email = Email("x");

			Func<Task> first = () => ApplyAsync(email, Parsed());
			await first.Should().ThrowAsync<EmailCallUpdateIncompleteException>();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Saved);
			_ledger.FailMovesTo.Clear();
			failNote = false;
			// Engine 2's row went in on the first delivery, so the resume only re-records a page if the additions still name it.
			call.UnitDispatches = new List<CallDispatchUnit>();

			(await ApplyAsync(email, Parsed())).Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);

			ids.Should().HaveCount(2);
			ids.Distinct().Should().ContainSingle("the outbox finds the first delivery's page by its id instead of recording a second one");
			Guid.TryParse(ids[0], out _).Should().BeTrue("the id fits the outbox's 36-character key");
		}

		[Test]
		public async Task A_close_whose_saved_step_could_not_be_recorded_still_releases_and_notifies()
		{
			var call = OpenCall("2026-0042");
			_config.ClosePhrases = "CALL CLOSED";
			ReleaseUnitSeven();
			_ledger.FailMovesTo.Add((int)CallEmailUpdateOutcomes.Saved);

			var result = await ProcessAsync(Email("Call ID: 2026-0042\nCALL CLOSED"));

			result.Outcome.Should().Be(CallEmailUpdateOutcomes.Closed);
			call.State.Should().Be((int)CallStates.Closed);
			VerifyReleasedAndNotifiedOnce();
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Closed);
		}

		[Test]
		public async Task A_saved_close_waiting_for_its_note_still_makes_an_older_close_stale()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.ClosePhrases = "CALL CLOSED";
			var call = OpenCall("2026-0042");
			var ten = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
			var failNote = true;
			_callsService.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote note, CancellationToken ct) =>
				{
					if (failNote)
						return Task.FromException<CallNote>(new TimeoutException());
					_notes.Add(note);
					return Task.FromResult(note);
				});

			Func<Task> close = () => ApplyAsync(Email("CALL CLOSED", sentOn: ten), ParsedWith(new Call(), CallEmailParsedFields.None));
			await close.Should().ThrowAsync<EmailCallUpdateIncompleteException>("only the close's note is owed");
			_ledger.Rows.Should().ContainSingle().Which.Outcome.Should().Be((int)CallEmailUpdateOutcomes.Noting);
			failNote = false;
			call.State = (int)CallStates.Active;

			var older = await ApplyAsync(Email("CALL CLOSED", sentOn: ten.AddMinutes(-30)), ParsedWith(new Call(), CallEmailParsedFields.None));

			older.Outcome.Should().Be(CallEmailUpdateOutcomes.Stale, "the 10:00 close changed the call even though its note is still owed");
			call.State.Should().Be((int)CallStates.Active, "a person reopened it; the delayed 09:30 close does not close it again");
		}

		[Test]
		public async Task A_re_page_a_recovered_change_decided_survives_a_second_interruption()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.RepageWhen = (int)CadRepageRules.LocationOrPriorityChanged;
			var call = OpenCall("2026-0042");
			var records = 0;
			_outbox.Setup(o => o.RecordOnceAsync(It.IsAny<string>(),It.IsAny<Call>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(),
					It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
				.Returns(() => ++records <= 2 ? Task.FromException<CallBroadcastOutboxEntry>(new TimeoutException("the process stopped")) : Task.FromResult(new CallBroadcastOutboxEntry { Persisted = true }));
			var email = Email("x");
			CallEmailParseResult Move() => ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address);

			// The move saves, its row cannot say so, and the process stops before the page.
			_ledger.FailMovesTo.Add((int)CallEmailUpdateOutcomes.Saved);
			Func<Task> first = () => ApplyAsync(email, Move());
			await first.Should().ThrowAsync<TimeoutException>();
			_ledger.FailMovesTo.Clear();

			// The redelivery finds the address already moved (no change of its own), still owes the page, saves, and stops again.
			Func<Task> second = () => ApplyAsync(email, Move());
			await second.Should().ThrowAsync<TimeoutException>();

			var third = await ApplyAsync(email, Move());

			third.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated);
			third.Repaged.Should().BeTrue("the location change the first run made still decides the re-page");
			records.Should().Be(3);
			call.Address.Should().Be("14 Oak St");
		}

		[Test]
		public async Task A_cad_value_that_agrees_with_a_dispatchers_edit_does_not_make_it_the_cads()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var created = await ApplyAsync(Email("x"), ParsedWith(new Call { Name = "Fire", Address = "12 Oak St" }, CallEmailParsedFields.Address));
			created.Call.Address = "13 Oak St";

			(await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "13 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);
			var next = await ApplyAsync(Email("x"), ParsedWith(new Call { Address = "16 Oak St" }, CallEmailParsedFields.Address));

			created.Call.Address.Should().Be("13 Oak St", "the dispatcher changed it, and a CAD later sending the same value does not hand it back");
			next.Changes.Should().ContainSingle(c => c.Group == EmailCallUpdateService.GroupLocation && c.Reason == EmailCallUpdateService.ReasonEdited);
		}

		[Test]
		public async Task An_email_policy_held_back_entirely_does_not_make_an_older_correction_stale()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			_config.LocationPolicy = (int)CadFieldPolicies.NoteOnly;
			_config.PriorityPolicy = (int)CadFieldPolicies.Update;
			var call = OpenCall("2026-0042");
			var ten = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

			(await ApplyAsync(Email("x", sentOn: ten), ParsedWith(new Call { Address = "14 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);

			var correction = await ApplyAsync(Email("x", sentOn: ten.AddMinutes(-30)), ParsedWith(new Call { Priority = 3 }, CallEmailParsedFields.Priority));

			correction.Outcome.Should().Be(CallEmailUpdateOutcomes.Updated, "the 10:00 email changed nothing, so it is no newer state");
			call.Priority.Should().Be(3);
		}

		[Test]
		public async Task An_email_the_call_already_reflects_still_makes_an_older_one_stale()
		{
			_config.IdSource = (int)CadCallIdSources.FromFormat;
			var call = OpenCall("2026-0042");
			var ten = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

			(await ApplyAsync(Email("x", sentOn: ten), ParsedWith(new Call { Address = "12 Oak St" }, CallEmailParsedFields.Address))).Outcome.Should().Be(CallEmailUpdateOutcomes.NoChange);

			(await ApplyAsync(Email("x", sentOn: ten.AddMinutes(-30)), ParsedWith(new Call { Address = "10 Oak St" }, CallEmailParsedFields.Address))).Outcome
				.Should().Be(CallEmailUpdateOutcomes.Stale);
			call.Address.Should().Be("12 Oak St");
		}
	}
}
