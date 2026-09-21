using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        [Test]
        public async Task Department_currency_controls_orders_retries_edits_and_vendor_charges()
        {
            Maintenance();
            await _service.SavePolicyAsync(_actor, new() { Currency = "CAD", SpendingThreshold = 100 });
            var input = Input(); input.Content.Currency = "typo";
            var order = await _service.CreateAsync(_actor, input);
            order.Input.Content.Currency.Should().Be("CAD");
            _store.All<WorkOrder>().Single().CurrencyCode.Should().Be("CAD");

            var policy = await _service.PolicyAsync(_actor); policy.Currency = "EUR"; policy.SpendingThreshold = 200;
            await _service.SavePolicyAsync(_actor, policy);
            input.Content.Currency = "USD";
            (await _service.CreateAsync(_actor, input)).Order.Id.Should().Be(order.Order.Id);
            var edit = (await _service.GetAsync(_actor, order.Order.Id)).Input;
            edit.Content.Currency = "EUR"; edit.Content.Description = "Updated repair";
            await _service.UpdateAsync(_actor, order.Order.Id, edit);
            (await _service.GetAsync(_actor, order.Order.Id)).Input.Content.Currency.Should().Be("CAD");
            (await _service.CreateAsync(_actor, Input())).Input.Content.Currency.Should().Be("EUR");

            var id = await Assigned(); var detail = await _service.GetAsync(_actor, id);
            var charge = new WorkOrderVendorChargeInput { Revision = detail.Order.Revision, RequestId = Guid.NewGuid().ToString("D"),
                Content = new() { Currency = "typo", VendorName = "Vendor", InvoiceReference = "INV-1", Description = "Repair", Amount = 25, ServiceDate = _maintenanceClock.Utc } };
            await _service.AddVendorChargeAsync(_actor, id, charge);
            charge.Content.Currency = "USD";
            await _service.AddVendorChargeAsync(_actor, id, charge);
            JsonConvert.DeserializeObject<WorkOrderVendorChargeContent>(_store.All<WorkOrderVendorCharge>().Single().Content).Currency.Should().Be("EUR");
        }

        [Test]
        public async Task Currency_changes_retain_prior_thresholds_and_enforce_the_new_threshold()
        {
            Maintenance();
            await _service.SavePolicyAsync(_actor, new() { Currency = "USD", ApprovalsEnabled = true, SpendingThreshold = 100 });
            var policy = await _service.PolicyAsync(_actor); policy.Currency = "EUR"; policy.SpendingThreshold = 250;
            await _service.SavePolicyAsync(_actor, policy);
            var saved = await _service.PolicyAsync(_actor);
            saved.SpendingRules.Single(r => r.Currency == "USD").Threshold.Should().Be(100);
            saved.SpendingRules.Single(r => r.Currency == "EUR").Threshold.Should().Be(250);
            saved.SpendingThreshold.Should().Be(250);
            var id = await Assigned(); var detail = await _service.GetAsync(_actor, id);
            var charge = new WorkOrderVendorChargeInput { Revision = detail.Order.Revision, RequestId = Guid.NewGuid().ToString("D"),
                Content = new() { VendorName = "Vendor", InvoiceReference = "INV-2", Description = "Repair", Amount = 251, ServiceDate = _maintenanceClock.Utc } };
            (await FluentActions.Awaiting(() => _service.AddVendorChargeAsync(_actor, id, charge)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("SpendingApprovalRequired");
        }

        [TestCase("usd"), TestCase("US"), TestCase("ZZZ"), TestCase("")]
        public async Task Settings_reject_unsupported_currencies(string currency)
        {
            Maintenance();
            (await FluentActions.Awaiting(() => _service.SavePolicyAsync(_actor, new() { Currency = currency })).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("OperationsPolicyInvalid");
            _store.All<WorkOrderPolicy>().Should().BeEmpty();
        }

        [Test]
        public async Task Currency_settings_are_tenant_scoped_and_require_department_management()
        {
            Maintenance();
            await _service.SavePolicyAsync(_actor, new() { Currency = "CAD" });
            await FluentActions.Awaiting(() => _service.SavePolicyAsync(new ChecklistActor { DepartmentId = 77, UserId = "technician" }, new() { Currency = "EUR", Revision = 1 })).Should().ThrowAsync<WorkOrderException>();
            (await _service.PolicyAsync(_actor)).Currency.Should().Be("CAD");
            (await _service.PolicyAsync(new ChecklistActor { DepartmentId = 88, UserId = "manager" })).Currency.Should().Be("USD");
        }

        [Test]
        public async Task Imports_apply_department_currency_and_detect_settings_changes_after_preview()
        {
            Maintenance(); await _service.SavePolicyAsync(_actor, new() { Currency = "CAD" });
            var batch = WorkOrderCsvImport.Parse(WorkOrderCsvImport.Header + "\nPump,,0,1,,,,,12.50,,SHOP", Guid.NewGuid().ToString("D"));
            batch.Rows.Single().ParseError.Should().BeNull();
            batch.PreviewHash = (await _service.PreviewBulkAsync(_actor, batch)).PreviewHash;
            batch.Rows.Single().Import.Content.Currency.Should().Be("CAD");
            var policy = await _service.PolicyAsync(_actor); policy.Currency = "EUR"; await _service.SavePolicyAsync(_actor, policy);
            (await FluentActions.Awaiting(() => _service.ApplyBulkAsync(_actor, batch)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("BulkPreviewChanged");
            _store.All<WorkOrder>().Should().BeEmpty();
            batch.PreviewHash = (await _service.PreviewBulkAsync(_actor, batch)).PreviewHash;
            (await _service.ApplyBulkAsync(_actor, batch)).Rows.Single().Applied.Should().BeTrue();
            (await _service.GetAsync(_actor, _store.All<WorkOrder>().Single().Id)).Input.Content.Currency.Should().Be("EUR");
            var legacy = WorkOrderCsvImport.Parse(WorkOrderCsvImport.LegacyHeader + "\nPump,,0,1,,,,,TYPO,12.50,,SHOP", Guid.NewGuid().ToString("D"));
            (await _service.PreviewBulkAsync(_actor, legacy)).Rows.Single().ErrorCode.Should().BeNull();
            legacy.Rows.Single().Import.Content.Currency.Should().Be("EUR");
        }

        [Test]
        public async Task Generated_orders_pin_current_department_currency_without_decrypting_or_relabeling_old_quotes()
        {
            Maintenance(); await _service.SavePolicyAsync(_actor, new() { Currency = "CAD" });
            var schedule = Schedule(); schedule.Template.Content.Currency = "EUR"; schedule.Template.Content.EstimatedCost = 50;
            var id = await _service.SaveRecurrenceAsync(_actor, schedule);
            (await _service.RecurrenceAsync(_actor, id)).Settings.Template.Content.Currency.Should().Be("CAD");
            var policy = await _service.PolicyAsync(_actor); policy.Currency = "EUR"; await _service.SavePolicyAsync(_actor, policy);
            _read.Invocations.Clear(); _write.Invocations.Clear();
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Errors.Should().Be(0);
            _read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty();
            var row = _store.All<WorkOrder>().Single(); row.CurrencyCode.Should().Be("EUR"); row.Content.Should().BeNull();
            policy = await _service.PolicyAsync(_actor); policy.Currency = "GBP"; await _service.SavePolicyAsync(_actor, policy);
            var generated = await _service.GetAsync(_actor, row.Id);
            generated.Input.Content.Currency.Should().Be("EUR"); generated.Input.Content.EstimatedCost.Should().BeNull();
            (await _service.RecurrenceAsync(_actor, id)).Settings.Template.Content.EstimatedCost.Should().Be(50);
        }
    }
}
