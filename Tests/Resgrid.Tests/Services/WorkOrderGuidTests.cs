using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using FluentMigrator.Model;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model.WorkOrders;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;

namespace Resgrid.Tests.Services
{
    public class WorkOrderGuidMigrationTests
    {
        [TestCase(false), TestCase(true)]
        public void Original_migrations_use_guid_strings_for_all_work_order_keys_and_references(bool postgres)
        {
            var expressions = new List<IMigrationExpression>();
            var context = new Mock<IMigrationContext>();
            context.SetupGet(c => c.Expressions).Returns(expressions);
            var schema = new Mock<IQuerySchema>();
            schema.Setup(s => s.TableExists(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
            context.SetupGet(c => c.QuerySchema).Returns(schema.Object);
            IMigration[] migrations = postgres
                ? new IMigration[] { new M0197_AddWorkOrdersPg(), new M0203_AddWorkOrderIntegrationsPg(), new M0204_AddWorkOrderRecurrencesPg(), new M0206_AddWorkOrderReportingPg(), new M0207_AddWorkOrderOperationsPg() }
                : new IMigration[] { new M0197_AddWorkOrders(), new M0203_AddWorkOrderIntegrations(), new M0204_AddWorkOrderRecurrences(), new M0206_AddWorkOrderReporting(), new M0207_AddWorkOrderOperations() };
            foreach (var migration in migrations) migration.GetUpExpressions(context.Object);
            var tables = expressions.OfType<CreateTableExpression>().ToDictionary(t => t.TableName, t => t.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            foreach (var expression in expressions.OfType<CreateColumnExpression>())
            {
                if (!tables.ContainsKey(expression.TableName)) tables[expression.TableName] = new(StringComparer.OrdinalIgnoreCase);
                tables[expression.TableName][expression.Column.Name] = expression.Column;
            }
            foreach (var expression in expressions.OfType<AlterColumnExpression>()) tables[expression.TableName][expression.Column.Name] = expression.Column;

            foreach (var table in WorkOrderTables.All.Values.Append("WorkOrderReportSnapshots"))
            {
                GuidColumn(tables[table]["Id"]);
                tables[table]["Id"].IsPrimaryKey.Should().BeTrue(table);
                tables[table]["DepartmentId"].Type.Should().Be(DbType.Int32, table);
            }
            var references = new HashSet<string>(new[] { "WorkOrderId", "DuplicateOfId", "RecurrenceId", "CurrentVersionId", "RecurrenceVersionId", "PendingWorkOrderId", "PartId", "SourceActivityId", "WorkOrderPartId", "WorkOrderPartMovementId" }, StringComparer.OrdinalIgnoreCase);
            var foreignKeys = tables.Values.SelectMany(c => c.Values).Where(c => references.Contains(c.Name)).ToList();
            foreignKeys.Should().NotBeEmpty();
            foreach (var column in foreignKeys) GuidColumn(column);
            tables["WorkOrderRecurrences"]["CurrentVersionId"].IsNullable.Should().BeTrue("the first version is inserted after its parent inside the transaction");

            static void GuidColumn(ColumnDefinition column)
            {
                column.Type.Should().Be(DbType.String, column.Name);
                column.Size.Should().Be(36, column.Name);
                column.IsIdentity.Should().BeFalse(column.Name);
                column.DefaultValue.Should().BeOfType<ColumnDefinition.UndefinedDefaultValue>(column.Name);
            }
        }

        [Test]
        public void Workflow_routing_preserves_guid_references_and_rejects_numeric_or_malformed_keys()
        {
            var id = Guid.NewGuid().ToString("D");
            var payload = new JObject { ["WorkOrderId"] = id, ["RecurrenceId"] = id, ["HoldId"] = id, ["PolicyId"] = id, ["TargetUnitId"] = 12, ["Revision"] = 3, ["Title"] = "private" };
            var safe = JObject.Parse(WorkOrderWorkflowPayload.Routing(payload));
            foreach (var name in new[] { "WorkOrderId", "RecurrenceId", "HoldId", "PolicyId" }) safe[name].Value<string>().Should().Be(id);
            safe["TargetUnitId"].Value<int>().Should().Be(12);
            safe["Revision"].Value<int>().Should().Be(3);
            safe.ToString().Should().NotContain("private");
            payload["WorkOrderId"] = 19; payload["RecurrenceId"] = "19"; payload["HoldId"] = "invalid";
            safe = JObject.Parse(WorkOrderWorkflowPayload.Routing(payload));
            safe["WorkOrderId"].Should().BeNull(); safe["RecurrenceId"].Should().BeNull(); safe["HoldId"].Should().BeNull();
        }
    }

    public partial class WorkOrderP2M1Tests
    {
        [Test]
        public async Task Guid_history_cursors_preserve_chronology_across_page_boundaries()
        {
            var start = DateTime.UtcNow.AddDays(-1);
            var rows = new List<WorkOrder>();
            for (var i = 0; i < 55; i++) rows.Add(await ReportSeed(start));
            rows = rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                // Deliberately make GUID order the reverse of event time.
                rows[i].CreatedOn = start.AddMinutes(rows.Count - i);
                await _store.WriteAsync(rows[i]);
            }
            var first = await _service.GetWorkOrderHistoryAsync(_actor, new WorkOrderReportQuery());
            var second = await _service.GetWorkOrderHistoryAsync(_actor, new WorkOrderReportQuery { AfterId = first.NextAfterId });
            first.Items.Should().HaveCount(50); second.Items.Should().HaveCount(5);
            second.NextAfterId.Should().BeNull();
            first.Items.Concat(second.Items).Select(r => r.Order.Id).Should().Equal(rows.OrderBy(r => r.CreatedOn).Select(r => r.Id));
        }

        [Test]
        public async Task Guid_identity_flows_through_creation_children_snapshots_and_retry()
        {
            var input = Input();
            var first = await _service.CreateAsync(_actor, input);
            Guid.TryParseExact(first.Order.Id, "D", out var id).Should().BeTrue();
            id.Should().NotBe(Guid.Empty);
            (await _service.CreateAsync(_actor, input)).Order.Id.Should().Be(first.Order.Id);
            await _service.CommentAsync(_actor, first.Order.Id, first.Order.Revision, "GUID evidence");
            var detail = await _service.GetAsync(_actor, first.Order.Id);
            detail.Activities.Should().HaveCount(2);
            foreach (var activity in _store.All<WorkOrderActivity>())
            {
                Guid.TryParseExact(activity.Id, "D", out _).Should().BeTrue();
                activity.WorkOrderId.Should().Be(first.Order.Id);
            }
            foreach (var snapshot in _store.Snapshots)
            {
                Guid.TryParseExact(snapshot.Id, "D", out _).Should().BeTrue();
                snapshot.WorkOrderId.Should().Be(first.Order.Id);
                detail.Activities.Select(a => a.Id).Should().Contain(snapshot.SourceActivityId);
            }
        }
    }
}
