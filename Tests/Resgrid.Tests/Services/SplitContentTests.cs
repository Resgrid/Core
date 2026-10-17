using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// ADP catalog review §8, option (a): a mixed table keeps its metadata in plaintext Content and its private members in
	/// ProtectedContent, the row's only ADP slot.
	/// </summary>
	[TestFixture]
	public class SplitContentTests
	{
		private static string Envelope => ProtectedDataEnvelope.Prefix + "1:1:c2VhbGVk";

		[Test]
		public void A_work_order_keeps_its_metadata_and_moves_free_text_about_people_out()
		{
			var content = JsonConvert.SerializeObject(new
			{
				Fields = new WorkOrderContent
				{
					Title = "Pump 3 primer", Description = "Captain Ruiz reports a leak", LocationText = "Station 4 bay 2", EstimatedCost = 125.50m,
					Resolution = "Replaced seal", Cause = "Worn gasket", QualifiedPersonnel = "J. Smith", VendorDetails = "Call Ann at 555-0100",
					Approval = new WorkOrderApproval { State = WorkOrderApprovalState.Requested, Amount = 125.50m, Reason = "Over budget" },
					Steps = new List<WorkOrderTaskStep> { new WorkOrderTaskStep { Text = "Drain tank", Completed = true } }
				},
				RequestHash = "ABC"
			});

			var (metadata, hidden) = SplitContent.Split(content, WorkOrderTables.PrivatePaths("WorkOrders"));

			var plain = JObject.Parse(metadata);
			plain["Fields"]["Title"].Value<string>().Should().Be("Pump 3 primer");
			plain["Fields"]["LocationText"].Value<string>().Should().Be("Station 4 bay 2");
			plain["Fields"]["EstimatedCost"].Value<decimal>().Should().Be(125.50m);
			plain["Fields"]["Approval"]["Amount"].Value<decimal>().Should().Be(125.50m);
			plain["Fields"]["Steps"][0]["Text"].Value<string>().Should().Be("Drain tank");
			plain["RequestHash"].Value<string>().Should().Be("ABC");
			metadata.Should().NotContain("Ruiz").And.NotContain("Replaced seal").And.NotContain("Worn gasket").And.NotContain("J. Smith")
				.And.NotContain("555-0100").And.NotContain("Over budget");

			var merged = JsonConvert.DeserializeObject<Wrapper>(SplitContent.Merge(metadata, hidden)).Fields;
			merged.Description.Should().Be("Captain Ruiz reports a leak");
			merged.Resolution.Should().Be("Replaced seal");
			merged.Cause.Should().Be("Worn gasket");
			merged.QualifiedPersonnel.Should().Be("J. Smith");
			merged.VendorDetails.Should().Be("Call Ann at 555-0100");
			merged.Approval.Reason.Should().Be("Over budget");
			merged.Title.Should().Be("Pump 3 primer");
		}

		private sealed class Wrapper { public WorkOrderContent Fields { get; set; } }

		[Test]
		public void Array_members_keep_their_indices_and_nothing_private_means_no_private_part()
		{
			var content = "{\"PendingKind\":\"Post\",\"PendingCommand\":{\"Lines\":[{\"ItemId\":\"a\",\"Note\":null},{\"ItemId\":\"b\",\"Note\":\"For Engine 2\"}]},\"Attestation\":null}";
			var (metadata, hidden) = SplitContent.Split(content, InventoryTables.PrivatePaths("InventoryOperations"));

			metadata.Should().NotContain("Engine 2");
			JObject.Parse(metadata)["PendingCommand"]["Lines"][1]["ItemId"].Value<string>().Should().Be("b");
			JToken.DeepEquals(JObject.Parse(SplitContent.Merge(metadata, hidden)), JObject.Parse("{\"PendingKind\":\"Post\",\"PendingCommand\":{\"Lines\":[{\"ItemId\":\"a\"},{\"ItemId\":\"b\",\"Note\":\"For Engine 2\"}]}}"))
				.Should().BeTrue("a null private member is simply absent, which decodes to the same default");

			SplitContent.Split("{\"Note\":null,\"ItemName\":\"Gauze\"}", InventoryTables.PrivatePaths("InventoryTransactions")).Private.Should().BeNull();
		}

		[Test]
		public void Dates_and_decimals_pass_through_as_written()
		{
			var content = "{\"ObservedOn\":\"2026-10-17T08:30:00.1234567Z\",\"MeterValue\":1234.500000,\"Note\":\"odd noise\"}";
			var (metadata, _) = SplitContent.Split(content, WorkOrderTables.PrivatePaths("WorkOrderMeterReadings"));
			metadata.Should().Be("{\"ObservedOn\":\"2026-10-17T08:30:00.1234567Z\",\"MeterValue\":1234.500000}");
		}

		[Test]
		public void Merge_fills_only_what_content_lacks_so_a_cleared_value_stays_cleared()
		{
			SplitContent.Merge("{\"Note\":null,\"Hours\":2}", "{\"Note\":\"old\",\"RatePerHour\":40}")
				.Should().Be("{\"Note\":null,\"Hours\":2,\"RatePerHour\":40}");
			SplitContent.Merge("{\"Hours\":2}", Envelope).Should().Be("{\"Hours\":2}", "a sealed part cannot be merged");
		}

		[Test]
		public void A_write_beside_a_sealed_private_part_keeps_it_or_is_refused()
		{
			var unrevealed = new WorkOrderLabor { Content = "{\"Hours\":2,\"Note\":null}", ProtectedContent = Envelope };
			var keep = SplitContent.PrepareWrite(unrevealed);
			keep.Allowed.Should().BeTrue();
			unrevealed.ProtectedContent.Should().Be(Envelope, "the envelope the row never revealed is kept");
			unrevealed.Content.Should().Be("{\"Hours\":2}");

			var overwrite = new WorkOrderLabor { Content = "{\"Hours\":2,\"Note\":\"new\"}", ProtectedContent = Envelope };
			SplitContent.PrepareWrite(overwrite).Allowed.Should().BeFalse("new private values cannot be merged with a sealed part");
			FluentActions.Invoking(() => SplitContent.EnsureSplitForStore(overwrite)).Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void An_unmerged_copy_keeps_private_members_it_never_loaded()
		{
			var copy = new WorkOrderLabor { Content = "{\"Hours\":3}", ProtectedContent = "{\"RatePerHour\":40,\"Note\":\"overtime\"}" };
			var write = SplitContent.PrepareWrite(copy);
			write.Allowed.Should().BeTrue();
			copy.Content.Should().Be("{\"Hours\":3}");
			JToken.DeepEquals(JObject.Parse(copy.ProtectedContent), JObject.Parse("{\"RatePerHour\":40,\"Note\":\"overtime\"}")).Should().BeTrue();
			JObject.Parse(write.InMemoryContent)["Note"].Value<string>().Should().Be("overtime");
		}

		[Test]
		public void Redaction_strips_private_members_merged_on_read()
		{
			var row = new InventoryCount { Content = "{\"Name\":\"Q4 count\",\"Note\":\"short two vials\"}", ProtectedContent = "{\"Note\":\"short two vials\"}" };
			SplitContent.Redact(row);
			row.Content.Should().Be("{\"Name\":\"Q4 count\"}");
			row.ProtectedContent.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public void A_typed_checklist_answer_keeps_its_value_but_free_text_does_not()
		{
			var typed = new ChecklistCompletionItem { Content = ChecklistTables.StoredAnswer(new ChecklistAnswer { ItemId = "x", Status = ChecklistAnswerStatus.Answered, Value = "12.5", Note = "low" }, ChecklistItemType.NumericReading) };
			var free = new ChecklistCompletionItem { Content = ChecklistTables.StoredAnswer(new ChecklistAnswer { ItemId = "y", Status = ChecklistAnswerStatus.Answered, Value = "Medic Jones signed" }, ChecklistItemType.FreeText) };
			var legacy = new ChecklistCompletionItem { Content = JsonConvert.SerializeObject(new ChecklistAnswer { ItemId = "z", Value = "pass" }) };

			SplitContent.PrepareWrite(typed);
			JObject.Parse(typed.Content)["Value"].Value<string>().Should().Be("12.5");
			typed.ProtectedContent.Should().Be("{\"Note\":\"low\"}");

			SplitContent.PrepareWrite(free);
			free.Content.Should().NotContain("Medic Jones");
			free.ProtectedContent.Should().Contain("Medic Jones");

			SplitContent.PrepareWrite(legacy);
			legacy.Content.Should().NotContain("pass", "an answer stored before its type was recorded keeps its value private");

			JsonConvert.DeserializeObject<ChecklistAnswer>(SplitContent.Merge(free.Content, free.ProtectedContent)).Value.Should().Be("Medic Jones signed");
		}

		[Test]
		public void Mixed_tables_seal_protected_content_and_wholly_private_tables_seal_content()
		{
			var catalog = new ProtectedFieldCatalog();
			var bound = AdpTableBindings.V1.SelectMany(b => b.Columns).Select(c => c.FieldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var mixed = ChecklistTables.PrivateMembers.Keys.Concat(WorkOrderTables.PrivateMembers.Keys).Concat(InventoryTables.PrivateMembers.Keys).ToList();
			mixed.Should().HaveCount(19);
			foreach (var table in mixed)
			{
				var slot = table.ToLowerInvariant() + ".protectedcontent";
				catalog.GetById(slot).Should().NotBeNull(slot);
				bound.Should().Contain(slot);
				catalog.IsProtectedField(table, "Content").Should().BeFalse($"{table}.Content is plaintext metadata");
				bound.Should().NotContain(table.ToLowerInvariant() + ".content");
			}
			foreach (var table in new[] { "ChecklistCompletionFiles", "WorkOrderFiles", "WorkOrderSafetyHolds", "RecordInventoryUsages", "InventoryVendors" })
			{
				catalog.IsProtectedField(table, "Content").Should().BeTrue($"{table} stays wholly protected");
				bound.Should().Contain(table.ToLowerInvariant() + ".content");
			}
			catalog.GetById("workorders.protectedcontent").AddedInCatalogVersion.Should().Be(WorkOrderTables.CatalogVersion, "the slot replaced Content at the same version");
			catalog.GetById("inventorycounts.protectedcontent").AddedInCatalogVersion.Should().Be(InventoryTables.OperationsCatalogVersion);
			catalog.GetById("checklistschedules.protectedcontent").AddedInCatalogVersion.Should().Be(17);
		}

		[Test]
		public void The_migration_split_rules_are_a_frozen_copy_of_the_module_maps()
		{
			var runtime = ChecklistTables.PrivateMembers.Concat(WorkOrderTables.PrivateMembers).Concat(InventoryTables.PrivateMembers)
				.ToDictionary(p => p.Key, p => p.Value);
			M0274_SplitModuleProtectedContent.Rules.Should().BeEquivalentTo(runtime);
			Resgrid.Providers.MigrationsPg.Migrations.M0274_SplitModuleProtectedContentPg.Rules.Should().BeEquivalentTo(runtime);
		}

		[Test]
		public void The_migration_splitter_matches_the_runtime_splitter()
		{
			var samples = new Dictionary<string, string>
			{
				["WorkOrderActivities"] = "{\"Note\":\"Hold: crew injured\",\"Snapshot\":{\"Title\":\"Ladder\",\"Description\":\"Firefighter Lee hurt\",\"Approval\":{\"State\":1,\"Reason\":\"x\"},\"Steps\":[]},\"ResponseDueOn\":\"2026-10-17T00:00:00Z\"}",
				["InventoryOperations"] = "{\"Fingerprint\":\"F\",\"PendingCommand\":{\"RequestId\":\"r\",\"Lines\":[{\"ItemId\":\"a\",\"Note\":\"n1\",\"Quantity\":1.500000},{\"ItemId\":\"b\",\"Note\":null}]},\"PendingIssues\":[{\"Note\":\"to medic\"}],\"PendingReturn\":null,\"Attestation\":\"I saw it\"}",
				["ChecklistCompletions"] = "{\"Note\":\"Smells of smoke\",\"LocationDescription\":\"Bay 2\",\"Latitude\":47.6062,\"Longitude\":-122.3321,\"ClientCompletedOn\":null}",
				["InventoryTransactions"] = "{\"SchemaVersion\":1,\"LegacySource\":{\"InventoryId\":5,\"Note\":\"legacy note\",\"Location\":\"Shelf A\"}}"
			};
			foreach (var (table, content) in samples)
			{
				var runtime = SplitContent.Split(content, M0274_SplitModuleProtectedContent.Rules[table]);
				var frozen = M0274_SplitModuleProtectedContent.SplitJson(content, M0274_SplitModuleProtectedContent.Rules[table]);
				JToken.DeepEquals(JObject.Parse(frozen.Content), JObject.Parse(runtime.Content)).Should().BeTrue(table);
				JToken.DeepEquals(JObject.Parse(frozen.Hidden), JObject.Parse(runtime.Private)).Should().BeTrue(table);
				JToken.DeepEquals(JObject.Parse(M0274_SplitModuleProtectedContent.MergeJson(frozen.Content, frozen.Hidden)), JObject.Parse(SplitContent.Merge(runtime.Content, runtime.Private)))
					.Should().BeTrue(table);
			}
		}
	}
}
