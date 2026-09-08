using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Services.Records.Connectors;
using static Resgrid.Tests.Rms.FakeOrderFeedProvider;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// External ordering-system connectors (RMS plan section 4.1): the gates a connector must pass before it may
	/// exist or run, what an import may and may not do to the department's own record, and the reconciliation
	/// that replaces every automatic update the plan forbids.
	/// </summary>
	[TestFixture]
	public class RecordDeploymentConnectorsServiceTests
	{
		private RmsDefinitionHarness _h;
		private bool _enabled;
		private string _passphrase;
		private int _disableAfter;
		private int _minPoll;

		[SetUp]
		public void SetUp()
		{
			_enabled = RecordsConnectorConfig.Enabled; _passphrase = RecordsConnectorConfig.CredentialPassphrase; _disableAfter = RecordsConnectorConfig.DisableAfterConsecutiveFailures; _minPoll = RecordsConnectorConfig.MinPollIntervalMinutes;
			RecordsConnectorConfig.Enabled = true;
			RecordsConnectorConfig.CredentialPassphrase = "unit-test-passphrase";
			RecordsConnectorConfig.MinPollIntervalMinutes = 15;
			_h = new RmsDefinitionHarness();
		}

		[TearDown]
		public void TearDown()
		{
			RecordsConnectorConfig.Enabled = _enabled; RecordsConnectorConfig.CredentialPassphrase = _passphrase; RecordsConnectorConfig.DisableAfterConsecutiveFailures = _disableAfter; RecordsConnectorConfig.MinPollIntervalMinutes = _minPoll;
		}

		private static RecordDeploymentConnectorInput Input(Action<RecordDeploymentConnectorInput> configure = null)
		{
			var input = new RecordDeploymentConnectorInput
			{
				ProviderKey = RmsExternalOrderConnectorProviders.Generic, Name = "County ordering", SourceSystem = "County Orders", BaseUrl = "https://orders.example.gov/feed",
				CredentialKind = RmsConnectorCredentialKinds.Bearer, Credential = "secret-token", ReadEnabled = true, PollIntervalMinutes = 30, MaxRequestsPerHour = 12, TermsReference = "https://orders.example.gov/terms"
			};
			configure?.Invoke(input);
			return input;
		}

		/// <summary>A connector that has passed every gate and can run.</summary>
		private async Task<RmsExternalOrderConnector> ReadyAsync(Action<RecordDeploymentConnectorInput> configure = null)
		{
			var created = await _h.Connectors.CreateAsync(Dept, Admin, Input(configure));
			await _h.Connectors.AcknowledgeTermsAsync(Dept, Admin, created.Connector.RmsExternalOrderConnectorId);
			return await _h.Connectors.SetEnabledAsync(Dept, Admin, created.Connector.RmsExternalOrderConnectorId, true);
		}

		#region Gates

		[Test]
		public async Task Connector_management_is_department_administration_only()
		{
			Func<Task> create = () => _h.Connectors.CreateAsync(Dept, Author, Input());
			await create.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> list = () => _h.Connectors.ListAsync(Dept, "viewer");
			await list.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> reconcile = () => _h.Connectors.GetReconciliationAsync(Dept, Author);
			await reconcile.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task A_connector_cannot_exist_without_the_installation_switch_and_a_credential_passphrase()
		{
			RecordsConnectorConfig.Enabled = false;
			Func<Task> off = () => _h.Connectors.CreateAsync(Dept, Admin, Input());
			await off.Should().ThrowAsync<InvalidOperationException>().WithMessage("*switched off*");

			RecordsConnectorConfig.Enabled = true;
			RecordsConnectorConfig.CredentialPassphrase = "";
			Func<Task> noPass = () => _h.Connectors.CreateAsync(Dept, Admin, Input());
			await noPass.Should().ThrowAsync<InvalidOperationException>().WithMessage("*passphrase*");
		}

		[Test]
		public async Task Write_authority_plain_http_and_unknown_providers_are_refused()
		{
			Func<Task> write = () => _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.WriteEnabled = true));
			await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Write authority*");

			Func<Task> http = () => _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.BaseUrl = "http://orders.example.gov/feed"));
			await http.Should().ThrowAsync<ArgumentException>().WithMessage("*https*");

			Func<Task> provider = () => _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.ProviderKey = "scraper"));
			await provider.Should().ThrowAsync<ArgumentException>().WithMessage("*not a connector provider*");

			Func<Task> header = () => _h.Connectors.CreateAsync(Dept, Admin, Input(i => { i.CredentialKind = RmsConnectorCredentialKinds.Header; i.CredentialHeaderName = null; }));
			await header.Should().ThrowAsync<ArgumentException>().WithMessage("*header name*");

			_h.Defs.Connectors.Should().BeEmpty();
		}

		[Test]
		public async Task Creating_encrypts_the_credential_hashes_a_one_time_token_and_never_hands_either_back()
		{
			var created = await _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.PollIntervalMinutes = 1));

			created.InboundToken.Should().NotBeNullOrEmpty().And.HaveLength(43);
			created.Connector.CredentialCiphertext.Should().Be("stored");
			created.Connector.InboundTokenHash.Should().Be("set");
			created.Connector.IsEnabled.Should().BeFalse();
			created.Connector.WriteEnabled.Should().BeFalse();
			created.Connector.PollIntervalMinutes.Should().Be(RecordsConnectorConfig.MinPollIntervalMinutes, "an interval below the floor is raised to it");

			var stored = _h.Defs.Connectors.Single();
			stored.CredentialCiphertext.Should().NotBeNullOrEmpty().And.NotBe("secret-token").And.NotBe("stored");
			stored.InboundTokenHash.Should().Be(RecordDeploymentConnectorsService.Hash(created.InboundToken)).And.NotBe(created.InboundToken);
			Resgrid.Framework.SymmetricEncryption.Decrypt(stored.CredentialCiphertext, RecordsConnectorConfig.CredentialPassphrase).Should().Be("secret-token");

			(await _h.Connectors.GetAsync(Dept, Admin, stored.RmsExternalOrderConnectorId)).CredentialCiphertext.Should().Be("stored");
			_h.Store.Audits.Should().Contain(a => a.Purpose == "Create connector" && a.CorrelationId == stored.RmsExternalOrderConnectorId);
		}

		[Test]
		public async Task Enabling_needs_acknowledged_terms_read_authority_and_a_credential_where_the_kind_needs_one()
		{
			var id = (await _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.Credential = null))).Connector.RmsExternalOrderConnectorId;

			Func<Task> unacknowledged = () => _h.Connectors.SetEnabledAsync(Dept, Admin, id, true);
			await unacknowledged.Should().ThrowAsync<InvalidOperationException>().WithMessage("*terms*");

			await _h.Connectors.AcknowledgeTermsAsync(Dept, Admin, id);
			Func<Task> noCredential = () => _h.Connectors.SetEnabledAsync(Dept, Admin, id, true);
			await noCredential.Should().ThrowAsync<InvalidOperationException>().WithMessage("*credential*");

			var stored = _h.Defs.Connectors.Single();
			await _h.Connectors.UpdateAsync(Dept, Admin, id, stored.RowVersion, Input(i => i.Credential = "now-set"));
			var enabled = await _h.Connectors.SetEnabledAsync(Dept, Admin, id, true);
			enabled.IsEnabled.Should().BeTrue();
			enabled.IsReadyToRun.Should().BeTrue();
			enabled.TermsAcknowledgedByUserId.Should().Be(Admin);

			// Read authority withdrawn: the connector switches itself off rather than running without authority.
			var withdrawn = await _h.Connectors.UpdateAsync(Dept, Admin, id, enabled.RowVersion, Input(i => i.ReadEnabled = false));
			withdrawn.IsEnabled.Should().BeFalse();
			withdrawn.IsReadyToRun.Should().BeFalse();
		}

		[Test]
		public async Task Terms_are_acknowledged_for_a_source_so_pointing_elsewhere_needs_a_fresh_acknowledgement()
		{
			var connector = await ReadyAsync();
			var moved = await _h.Connectors.UpdateAsync(Dept, Admin, connector.RmsExternalOrderConnectorId, connector.RowVersion, Input(i => i.BaseUrl = "https://other.example.gov/feed"));
			moved.TermsAcknowledgedOn.Should().BeNull();
			moved.IsEnabled.Should().BeFalse();

			Func<Task> stale = () => _h.Connectors.UpdateAsync(Dept, Admin, connector.RmsExternalOrderConnectorId, connector.RowVersion, Input());
			await stale.Should().ThrowAsync<RecordConcurrencyException>();
		}

		#endregion

		#region Import

		[Test]
		public async Task A_run_provisions_unseen_orders_with_their_requests_as_requested_fills_owned_by_the_connector()
		{
			var connector = await ReadyAsync();
			_h.Feed.Serve(Feed("v1", Order("O-1001", "open", Request("O-1"), Request("E-3", "requested", "equipment"), Request("C-9", "cancelled", "crew"))));

			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			result.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Ok, result.Run.Error);
			result.Run.Trigger.Should().Be(RmsConnectorRunTriggers.Manual);
			result.Run.OrdersSeen.Should().Be(1); result.Run.OrdersCreated.Should().Be(1); result.Run.RequestsAdded.Should().Be(2); result.Run.SnapshotsRecorded.Should().Be(0);
			result.Run.SourceVersion.Should().Be("v1");
			_h.Feed.LastCredential.Should().Be("secret-token", "the provider receives the decrypted credential");

			var order = _h.Defs.Orders.Single();
			order.OrderNumber.Should().Be("O-1001");
			order.SourceScheme.Should().Be("local");
			order.ProfileKey.Should().Be(RmsDeploymentProfiles.LocalMutualAid);
			order.OwnershipMarker.Should().Be(RmsExternalOrderOwnership.Connector);
			order.ConnectorId.Should().Be(connector.RmsExternalOrderConnectorId);
			order.SourceVersion.Should().Be("v1");
			order.ArtifactContentType.Should().Be("application/json");
			order.ArtifactSafeUrl.Should().Be("https://orders.example.gov/O-1001");
			Encoding.UTF8.GetString(order.ArtifactData).Should().Contain("\"orderNumber\":\"O-1001\"");

			var fills = _h.Defs.Fills.Where(f => f.RmsExternalOrderId == order.RmsExternalOrderId).ToList();
			fills.Select(f => f.RequestNumber).Should().BeEquivalentTo(new[] { "O-1", "E-3" }, "a cancelled request is not a fill the department is asked for");
			fills.Should().OnlyContain(f => f.Status == (int)RmsDeploymentFillStatus.Requested);

			var stored = _h.Defs.Connectors.Single();
			stored.LastSuccessOn.Should().NotBeNull(); stored.LastPolledOn.Should().NotBeNull(); stored.RequestsThisHour.Should().Be(1); stored.ConsecutiveFailures.Should().Be(0);
			_h.Defs.ConnectorRuns.Should().ContainSingle(r => r.Outcome == RmsConnectorRunOutcomes.Ok);
			_h.Store.Audits.Should().Contain(a => a.Purpose.StartsWith("Import order O-1001") && a.ActorUserId == Admin);
		}

		[Test]
		public async Task An_unchanged_feed_records_nothing_and_a_changed_one_lands_as_a_new_snapshot_without_touching_local_fills()
		{
			var connector = await ReadyAsync();
			_h.Feed.Serve(Feed("v1", Order("O-1001", "open", Request("O-1"))));
			await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			var order = _h.Defs.Orders.Single();
			var fill = _h.Defs.Fills.Single();
			await _h.Deployments.TransitionFillAsync(Dept, Admin, fill.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Accepted });
			var firstChecksum = order.ArtifactChecksum;

			var same = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			same.Run.Unchanged.Should().Be(1); same.Run.SnapshotsRecorded.Should().Be(0); same.Run.OrdersCreated.Should().Be(0); same.Run.RequestsAdded.Should().Be(0);
			_h.Defs.References.Should().BeEmpty("no snapshot was superseded");

			// The source now says the request is mobilized and adds a second request.
			_h.Feed.Serve(Feed("v2", Order("O-1001", "mobilized", Request("O-1", "mobilized"), Request("E-3", "requested", "equipment"))));
			var changed = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			changed.Run.SnapshotsRecorded.Should().Be(1); changed.Run.RequestsAdded.Should().Be(1); changed.Run.Conflicts.Should().Be(1);
			var stored = _h.Defs.Orders.Single();
			stored.SourceVersion.Should().Be("v2");
			stored.ArtifactChecksum.Should().NotBe(firstChecksum);
			_h.Defs.References.Should().ContainSingle(r => r.SemanticRole == "superseded-snapshot" && r.Checksum == firstChecksum, "the first snapshot stays as signed history");
			_h.Defs.Fills.Single(f => f.RequestNumber == "O-1").Status.Should().Be((int)RmsDeploymentFillStatus.Accepted, "the connector never transitions a local fill");
			_h.Defs.Fills.Single(f => f.RequestNumber == "E-3").Status.Should().Be((int)RmsDeploymentFillStatus.Requested);

			var items = await _h.Connectors.GetReconciliationAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			items.Should().ContainSingle(i => i.Kind == RecordDeploymentReconciliationItem.SourceStatusAhead && i.RequestNumber == "O-1" && i.SourceStatus == "mobilized" && i.LocalStatus == "filled");
		}

		[Test]
		public async Task Reconciliation_lists_every_disagreement_and_applies_none_of_them()
		{
			var connector = await ReadyAsync();
			_h.Feed.Serve(Feed("v1", Order("O-1001", "open", Request("O-1"), Request("E-3", "requested", "equipment"))));
			await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			var order = _h.Defs.Orders.Single();
			// The department adds a fill of its own and moves the first one along.
			await _h.Deployments.AddFillAsync(Dept, Admin, order.RmsExternalOrderId, new RecordDeploymentFillInput { RequestNumber = "LOCAL-7", RequestCategory = "overhead", ResourceKind = "person", Position = "SOFR" });
			var first = _h.Defs.Fills.Single(f => f.RequestNumber == "O-1");
			await _h.Deployments.TransitionFillAsync(Dept, Admin, first.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Accepted });
			await _h.Deployments.TransitionFillAsync(Dept, Admin, first.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Mobilized });

			// The source closes the order, still shows O-1 merely filled, and has forgotten E-3.
			_h.Feed.Serve(Feed("v2", Order("O-1001", "closed", Request("O-1", "filled"), Request("N-4", "requested"))));
			await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			var items = await _h.Connectors.GetReconciliationAsync(Dept, Admin);
			items.Select(i => i.Kind + ":" + (i.RequestNumber ?? "-")).Should().BeEquivalentTo(
				RecordDeploymentReconciliationItem.SourceClosedLocalOpen + ":-",
				RecordDeploymentReconciliationItem.SourceStatusBehind + ":O-1",
				RecordDeploymentReconciliationItem.LocalFillMissingInSource + ":E-3",
				RecordDeploymentReconciliationItem.LocalFillMissingInSource + ":LOCAL-7");
			items.Should().OnlyContain(i => i.OrderId == order.RmsExternalOrderId && i.RecordId == order.RecordId && i.ConnectorId == connector.RmsExternalOrderConnectorId);

			_h.Defs.Orders.Single().Status.Should().Be((int)RmsExternalOrderStatus.Mobilized, "a source closure never closes the local deployment");
			_h.Defs.Fills.Single(f => f.RequestNumber == "O-1").Status.Should().Be((int)RmsDeploymentFillStatus.Mobilized);
			_h.Defs.Fills.Should().Contain(f => f.RequestNumber == "N-4" && f.Status == (int)RmsDeploymentFillStatus.Requested, "a request new to the department is the source's own fact");
		}

		[Test]
		public async Task A_closed_out_deployment_is_signed_history_and_takes_no_further_snapshots()
		{
			var connector = await ReadyAsync();
			_h.Feed.Serve(Feed("v1", Order("O-1001", "open", Request("O-1"))));
			await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			var order = _h.Defs.Orders.Single();
			var fill = _h.Defs.Fills.Single();
			foreach (var step in new[] { RmsDeploymentFillStatus.Accepted, RmsDeploymentFillStatus.Mobilized, RmsDeploymentFillStatus.CheckedIn, RmsDeploymentFillStatus.Released, RmsDeploymentFillStatus.Demobilized, RmsDeploymentFillStatus.Returned })
				await _h.Deployments.TransitionFillAsync(Dept, Admin, fill.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = step });
			await _h.Deployments.CloseoutAsync(Dept, Admin, order.RmsExternalOrderId, _h.Defs.Orders.Single().RowVersion, "done");
			_h.Defs.Orders.Single().Status.Should().Be((int)RmsExternalOrderStatus.ClosedOut);

			_h.Feed.Serve(Feed("v2", Order("O-1001", "closed", Request("O-1", "released"), Request("Z-1"))));
			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			result.Run.Unchanged.Should().Be(1); result.Run.SnapshotsRecorded.Should().Be(0); result.Run.RequestsAdded.Should().Be(0);
			_h.Defs.Orders.Single().SourceVersion.Should().Be("v1");
		}

		[Test]
		public async Task Provider_rules_reject_an_order_that_is_not_a_fact_of_that_source()
		{
			var connector = await ReadyAsync(i => { i.ProviderKey = RmsExternalOrderConnectorProviders.Iroc; i.ProfileKey = null; i.SourceScheme = null; });
			connector.SourceScheme.Should().Be("iroc"); connector.ProfileKey.Should().Be(RmsDeploymentProfiles.UsWildland);
			var noIncident = Order("O-2", "open", Request("O-1")); noIncident.IncidentNumber = null;
			var noCategory = Order("O-3", "open", Request("O-1", "requested", null));
			_h.Feed.Serve(Feed("v1", Order("O-1", "open", Request("O-1")), noIncident, noCategory));

			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			result.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Ok, result.Run.Error);
			result.Run.OrdersSeen.Should().Be(3); result.Run.OrdersCreated.Should().Be(1); result.Run.Rejected.Should().Be(2);
			result.Messages.Should().Contain(m => m.StartsWith("Order O-2:") && m.Contains("incident number")).And.Contain(m => m.StartsWith("Order O-3:") && m.Contains("category"));
			_h.Defs.Orders.Single().OrderNumber.Should().Be("O-1");
			_h.Defs.Orders.Single().SourceScheme.Should().Be("iroc");
		}

		[Test]
		public async Task A_feed_that_breaks_the_contract_is_rejected_whole_and_counted_as_a_failure_of_the_run_not_the_connector()
		{
			var connector = await ReadyAsync();
			_h.Feed.ServeRaw("{\"contract\":\"somebody-elses.v9\",\"orders\":[]}");
			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			result.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Rejected);
			result.Run.Error.Should().Contain("contract");
			_h.Defs.Orders.Should().BeEmpty();
			_h.Defs.Connectors.Single().IsEnabled.Should().BeTrue();
		}

		[Test]
		public async Task The_cursor_is_followed_page_by_page_and_remembered_for_the_next_run()
		{
			var connector = await ReadyAsync();
			var page1 = Feed("v1", Order("O-1", "open", Request("O-1"))); page1.Cursor = "p2";
			var page2 = Feed("v1", Order("O-2", "open", Request("O-1"))); page2.Cursor = null;
			_h.Feed.Serve(page1); _h.Feed.Serve(page2, "p2");

			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			result.Run.RequestCount.Should().Be(2); result.Run.OrdersCreated.Should().Be(2);
			_h.Feed.CursorsSeen.Should().Equal(null, "p2");
			_h.Defs.Connectors.Single().LastCursor.Should().BeNull("the last page ended the cursor");
			_h.Defs.OrdersRepo.Verify(r => r.GetForDepartmentAsync(Dept, true), Times.Once,
				"the department's orders are matched against once per run, not re-read for every page");
		}

		[Test]
		public async Task A_run_that_cannot_claim_the_connector_is_skipped_rather_than_run_twice()
		{
			var connector = await ReadyAsync();
			_h.Feed.Serve(Feed("v1", Order("O-1001", "open", Request("O-1"))));
			// Another runner holds the connector: the claim is what stops two runs from spending the same hourly
			// budget, importing the same pages and overwriting each other's cursor.
			_h.Defs.ConnectorsRepo.Setup(r => r.TryBumpRowVersionAsync(Dept, connector.RmsExternalOrderConnectorId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);

			result.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Rejected);
			_h.Feed.Fetches.Should().Be(0);
			_h.Defs.Orders.Should().BeEmpty();
			_h.Defs.ConnectorRuns.Should().ContainSingle("a skipped run is still recorded");
			_h.Defs.Connectors.Single().LastPolledOn.Should().BeNull("the connector row belongs to whoever holds the claim");
		}

		#endregion

		#region Limits and failure

		[Test]
		public async Task The_hourly_request_limit_stops_a_run_and_polls_and_pushes_share_it()
		{
			var connector = await ReadyAsync(i => i.MaxRequestsPerHour = 2);
			_h.Feed.Serve(Feed("v1", Order("O-1", "open", Request("O-1"))));
			(await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId)).Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Ok);

			var token = await _h.Connectors.RotateInboundTokenAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			var pushed = await _h.Connectors.ImportInboundAsync(connector.RmsExternalOrderConnectorId, token, Newtonsoft.Json.JsonConvert.SerializeObject(Feed("v2", Order("O-1", "open", Request("O-1"), Request("O-2")))));
			pushed.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Ok);
			pushed.Run.Trigger.Should().Be(RmsConnectorRunTriggers.Inbound);
			pushed.Run.RequestsAdded.Should().Be(1);

			var limited = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			limited.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.RateLimited);
			_h.Feed.Fetches.Should().Be(1, "a spent window refuses before touching the source");

			// An hour later the window rolls over.
			var stored = _h.Defs.Connectors.Single();
			stored.RateWindowStartedOn = DateTime.UtcNow.AddHours(-2);
			(await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId)).Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Ok);
			_h.Defs.Connectors.Single().RequestsThisHour.Should().Be(1);
		}

		[Test]
		public async Task An_inbound_push_needs_the_connectors_token_and_learns_nothing_from_a_refusal()
		{
			var connector = await ReadyAsync();
			var feed = Newtonsoft.Json.JsonConvert.SerializeObject(Feed("v1", Order("O-1", "open", Request("O-1"))));

			Func<Task> wrong = () => _h.Connectors.ImportInboundAsync(connector.RmsExternalOrderConnectorId, "not-the-token", feed);
			await wrong.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> unknown = () => _h.Connectors.ImportInboundAsync(Guid.NewGuid().ToString(), "anything", feed);
			await unknown.Should().ThrowAsync<UnauthorizedAccessException>();
			_h.Defs.ConnectorRuns.Should().BeEmpty("a refused push leaves no trace a caller could probe");

			var token = await _h.Connectors.RotateInboundTokenAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			(await _h.Connectors.ImportInboundAsync(connector.RmsExternalOrderConnectorId, token, feed)).Run.OrdersCreated.Should().Be(1);
			_h.Defs.Orders.Single().CreatedByUserId.Should().Be(Admin, "a push acts under the administrator who authorized the connector");

			// A disabled connector refuses pushes too, and a deleted one loses its token entirely.
			await _h.Connectors.SetEnabledAsync(Dept, Admin, connector.RmsExternalOrderConnectorId, false);
			(await _h.Connectors.ImportInboundAsync(connector.RmsExternalOrderConnectorId, token, feed)).Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Disabled);
			await _h.Connectors.DeleteAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			Func<Task> deleted = () => _h.Connectors.ImportInboundAsync(connector.RmsExternalOrderConnectorId, token, feed);
			await deleted.Should().ThrowAsync<UnauthorizedAccessException>();
			_h.Defs.Connectors.Single().CredentialCiphertext.Should().BeNull();
			(await _h.Connectors.ListAsync(Dept, Admin)).Should().BeEmpty();
		}

		[Test]
		public async Task Repeated_failures_switch_the_connector_off_until_an_administrator_looks()
		{
			RecordsConnectorConfig.DisableAfterConsecutiveFailures = 2;
			var connector = await ReadyAsync();
			_h.Feed.Throw = new InvalidOperationException("The source answered 503 Service Unavailable.");

			var first = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			first.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Failed);
			_h.Defs.Connectors.Single().ConsecutiveFailures.Should().Be(1);
			_h.Defs.Connectors.Single().IsEnabled.Should().BeTrue();

			var second = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			second.Messages.Should().Contain(m => m.Contains("disabled"));
			var stored = _h.Defs.Connectors.Single();
			stored.IsEnabled.Should().BeFalse();
			stored.LastError.Should().Contain("503");

			var third = await _h.Connectors.RunAsync(Dept, Admin, connector.RmsExternalOrderConnectorId);
			third.Run.Outcome.Should().Be(RmsConnectorRunOutcomes.Disabled);
			_h.Feed.Fetches.Should().Be(2);

			// Re-enabling clears the failure count so the next honest failure starts a new run.
			(await _h.Connectors.SetEnabledAsync(Dept, Admin, connector.RmsExternalOrderConnectorId, true)).ConsecutiveFailures.Should().Be(0);
		}

		[Test]
		public async Task The_poll_sweep_runs_only_connectors_that_are_ready_and_due()
		{
			var ready = await ReadyAsync();
			var dormant = (await _h.Connectors.CreateAsync(Dept, Admin, Input(i => i.Name = "Dormant"))).Connector;
			_h.Feed.Serve(Feed("v1", Order("O-1", "open", Request("O-1"))));

			(await _h.Connectors.RunDueAsync()).Should().Be(1);
			_h.Defs.ConnectorRuns.Should().ContainSingle(r => r.RmsExternalOrderConnectorId == ready.RmsExternalOrderConnectorId && r.Trigger == RmsConnectorRunTriggers.Poll && r.TriggeredByUserId == null);
			_h.Defs.ConnectorRuns.Should().NotContain(r => r.RmsExternalOrderConnectorId == dormant.RmsExternalOrderConnectorId);

			(await _h.Connectors.RunDueAsync()).Should().Be(0, "the interval has not elapsed since the last poll");

			RecordsConnectorConfig.Enabled = false;
			_h.Defs.Connectors.Single(c => c.RmsExternalOrderConnectorId == ready.RmsExternalOrderConnectorId).LastPolledOn = DateTime.UtcNow.AddHours(-3);
			(await _h.Connectors.RunDueAsync()).Should().Be(0, "the installation switch stops every poll");
		}

		#endregion
	}
}
