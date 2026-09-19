using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>Workforce &amp; Business Operations plan Phase B (B4 / Verification): invoice math, lifecycle and payment choke point.</summary>
	[TestFixture]
	public class InvoicingServiceTests
	{
		private Mock<ICustomerBillingProfileRepository> _profiles;
		private Mock<IRateCardRepository> _rateCards;
		private Mock<IRateCardItemRepository> _rateCardItems;
		private Mock<IInvoiceRepository> _invoices;
		private Mock<IInvoiceLineItemRepository> _lineItems;
		private Mock<IInvoicePaymentRepository> _payments;
		private Mock<IInvoiceNumberSequenceRepository> _sequence;
		private Mock<IDepartmentBillingIdentityRepository> _identities;
		private Mock<IContactsService> _contacts;
		private Mock<ICallsService> _calls;
		private Mock<IUnitsService> _units;
		private Mock<IDomainEventOutboxService> _outbox;
		private Mock<IEventAggregator> _events;
		private Mock<IPdfProvider> _pdf;
		private Mock<IEmailService> _email;
		private Mock<IDepartmentsService> _departments;
		private Mock<IAddressService> _addresses;
		private FakeUnitOfWork _unitOfWork;
		private List<DomainEventEnvelope> _published;
		private List<AuditEvent> _audits;

		[SetUp]
		public void SetUp()
		{
			_profiles = new Mock<ICustomerBillingProfileRepository>();
			_rateCards = new Mock<IRateCardRepository>();
			_rateCardItems = new Mock<IRateCardItemRepository>();
			_invoices = new Mock<IInvoiceRepository>();
			_lineItems = new Mock<IInvoiceLineItemRepository>();
			_payments = new Mock<IInvoicePaymentRepository>();
			_sequence = new Mock<IInvoiceNumberSequenceRepository>();
			_identities = new Mock<IDepartmentBillingIdentityRepository>();
			_contacts = new Mock<IContactsService>();
			_calls = new Mock<ICallsService>();
			_units = new Mock<IUnitsService>();
			_outbox = new Mock<IDomainEventOutboxService>();
			_events = new Mock<IEventAggregator>();
			_pdf = new Mock<IPdfProvider>();
			_email = new Mock<IEmailService>();
			_departments = new Mock<IDepartmentsService>();
			_addresses = new Mock<IAddressService>();
			_unitOfWork = new FakeUnitOfWork();
			_departments.Setup(d => d.GetDepartmentByIdAsync(7, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 7, Name = "Test County Fire" });
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns<string>(html => System.Text.Encoding.UTF8.GetBytes(html));
			_email.Setup(e => e.SendInvoiceAsync(It.IsAny<EmailNotification>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
			_published = new List<DomainEventEnvelope>();
			_audits = new List<AuditEvent>();

			_outbox.Setup(o => o.EnqueueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, DomainEventEnvelope, CancellationToken>((_, __, e, ___) => _published.Add(e))
				.ReturnsAsync(new DomainEventOutboxEntry());
			_events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));

			// Repositories echo the saved entity and assign a GUID to a null id, as RepositoryBase does.
			_invoices.Setup(r => r.SaveOrUpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Invoice i, CancellationToken _, bool __) => { i.InvoiceId ??= Guid.NewGuid().ToString(); return i; });
			_payments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePayment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((InvoicePayment p, CancellationToken _, bool __) => { p.InvoicePaymentId ??= Guid.NewGuid().ToString(); return p; });
			_lineItems.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoiceLineItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((InvoiceLineItem l, CancellationToken _, bool __) => { l.InvoiceLineItemId ??= Guid.NewGuid().ToString(); return l; });
			_contacts.Setup(c => c.GetContactByIdAsync(It.IsAny<string>())).ReturnsAsync(new Contact { ContactId = "contact-1", DepartmentId = 7, CompanyName = "Acme Logistics" });
		}

		private InvoicingService Build() => new InvoicingService(_profiles.Object, _rateCards.Object, _rateCardItems.Object, _invoices.Object,
			_lineItems.Object, _payments.Object, _sequence.Object, _identities.Object, _contacts.Object, _calls.Object, _units.Object, _outbox.Object, _events.Object,
			_pdf.Object, _email.Object, _departments.Object, _addresses.Object, _unitOfWork);

		/// <summary>Counts the transaction lifecycle the service drives; a nested call must join the open transaction rather than open its own.</summary>
		private sealed class FakeUnitOfWork : IUnitOfWork
		{
			public int Opened, Commits, Discards;
			public DbTransaction Transaction { get; private set; }
			public DbConnection Connection => null;
			public DbConnection CreateOrGetConnection() { if (Transaction == null) { Opened++; Transaction = new Mock<DbTransaction>().Object; } return null; }
			public Task<DbConnection> CreateOrGetConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateOrGetConnection());
			public void CommitChanges() { Commits++; Transaction = null; }
			public void DiscardChanges() { Discards++; Transaction = null; }
			public void Dispose() { }
		}

		private static CustomerBillingProfile Profile(decimal? taxRate = null, string components = null, decimal? discount = null, bool taxExempt = false) =>
			new CustomerBillingProfile { CustomerBillingProfileId = "profile-1", DepartmentId = 7, ContactId = "contact-1", Active = true, TermsNetDays = 30, TaxRate = taxRate, TaxComponentsJson = components, DefaultDiscountPercent = discount, TaxExempt = taxExempt };

		private static InvoiceLineItem Line(decimal amount, bool taxable = true) => new InvoiceLineItem { Amount = amount, Quantity = 1, UnitRate = amount, Taxable = taxable, Description = "x" };

		// ---------------------------------------------------------------- totals (decisions 12, 14, 23)

		[Test]
		public void Totals_apply_discount_before_a_flat_tax_rate()
		{
			var invoice = new Invoice { DiscountPercent = 10m };
			InvoicingService.ComputeTotals(invoice, new[] { Line(1000m), Line(250m, taxable: false) }, Profile(taxRate: 8m));

			invoice.SubTotal.Should().Be(1250.00m);
			invoice.DiscountAmount.Should().Be(125.00m);
			// Taxable base 1000 minus its pro-rata share of the discount (125 * 1000/1250 = 100) = 900; 8% = 72.
			invoice.TaxAmount.Should().Be(72.00m);
			invoice.Total.Should().Be(1197.00m);
			invoice.TaxComponentsJson.Should().BeNull();
		}

		[Test]
		public void Totals_snapshot_named_tax_components_with_amounts()
		{
			var invoice = new Invoice();
			var profile = Profile(components: "[{\"Name\":\"GST\",\"RatePercent\":5,\"RegistrationNumber\":\"123456789 RT0001\"},{\"Name\":\"BC PST\",\"RatePercent\":7,\"RegistrationNumber\":\"PST-1001-2345\"}]");
			InvoicingService.ComputeTotals(invoice, new[] { Line(200m) }, profile);

			invoice.TaxAmount.Should().Be(24.00m);
			invoice.Total.Should().Be(224.00m);
			var snapshot = InvoicingService.ParseTaxComponents(invoice.TaxComponentsJson);
			snapshot.Select(x => (x.Name, x.Amount)).Should().Equal(("GST", 10.00m), ("BC PST", 14.00m));
		}

		[Test]
		public void Tax_exempt_profile_and_discount_larger_than_subtotal_are_clamped()
		{
			var invoice = new Invoice { DiscountPercent = 100m };
			InvoicingService.ComputeTotals(invoice, new[] { Line(10m) }, Profile(taxRate: 20m, taxExempt: true));
			invoice.DiscountAmount.Should().Be(10.00m);
			invoice.TaxAmount.Should().Be(0m);
			invoice.Total.Should().Be(0m);
		}

		[Test]
		public void Amount_paid_counts_effective_payments_only()
		{
			var invoice = new Invoice();
			var payments = new[]
			{
				new InvoicePayment { Amount = 100m },
				new InvoicePayment { Amount = 50m, RefundedAmount = 20m, Status = (int)InvoicePaymentStatuses.PartiallyRefunded },
				new InvoicePayment { Amount = 70m, Status = (int)InvoicePaymentStatuses.DisputeLost }
			};
			InvoicingService.ComputeTotals(invoice, new[] { Line(500m) }, Profile(), payments);
			invoice.AmountPaid.Should().Be(130.00m);
		}

		// ---------------------------------------------------------------- status derivation (decision 6)

		[TestCase(0, 100, 0, false, InvoiceStatus.Sent)]
		[TestCase(0, 100, 0, true, InvoiceStatus.Overdue)]
		[TestCase(0, 100, 40, true, InvoiceStatus.PartiallyPaid)]
		[TestCase(0, 100, 100, true, InvoiceStatus.Paid)]
		[TestCase(0, 100, 120, false, InvoiceStatus.Paid)]
		public void Status_is_derived_from_balance_and_due_date(int _, decimal total, decimal paid, bool pastDue, InvoiceStatus expected)
		{
			var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
			var invoice = new Invoice { Status = (int)InvoiceStatus.Sent, Total = total, AmountPaid = paid, DueOn = pastDue ? now.AddDays(-1) : now.AddDays(10) };
			InvoicingService.DeriveStatus(invoice, now).Should().Be((int)expected);
		}

		[Test]
		public void Draft_and_void_never_move_through_payment_derivation()
		{
			var now = DateTime.UtcNow;
			InvoicingService.DeriveStatus(new Invoice { Status = (int)InvoiceStatus.Void, Total = 10, AmountPaid = 10 }, now).Should().Be((int)InvoiceStatus.Void);
			InvoicingService.DeriveStatus(new Invoice { Status = (int)InvoiceStatus.Draft, Total = 10, AmountPaid = 10 }, now).Should().Be((int)InvoiceStatus.Draft);
		}

		// ---------------------------------------------------------------- time on scene (decision 9)

		[Test]
		public void On_scene_minutes_run_from_first_on_scene_to_the_next_state()
		{
			var t0 = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);
			var states = new List<UnitState>
			{
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Responding, Timestamp = t0 },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.OnScene, Timestamp = t0.AddMinutes(12) },
				new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = t0.AddMinutes(95) }
			};
			InvoicingService.OnSceneMinutes(states, t0, t0.AddHours(3)).Should().Be(83);
		}

		[Test]
		public void On_scene_minutes_fall_back_to_the_call_window_and_never_go_negative()
		{
			var t0 = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);
			InvoicingService.OnSceneMinutes(new List<UnitState>(), t0, t0.AddMinutes(45)).Should().Be(45);
			InvoicingService.OnSceneMinutes(new List<UnitState>(), t0, t0.AddMinutes(-5)).Should().Be(0);
		}

		[TestCase(83, 0, 0, 1.3833)]
		[TestCase(83, 60, 0, 1.3833)]
		[TestCase(83, 120, 0, 2.0)]
		[TestCase(83, 0, 15, 1.5)]
		[TestCase(83, 0, 30, 1.5)]
		[TestCase(61, 0, 30, 1.5)]
		[TestCase(5, 60, 30, 1.0)]
		public void Rounding_and_minimum_minutes_produce_billable_hours(int minutes, int minimum, int rounding, decimal expectedHours)
		{
			var item = new RateCardItem { MinimumMinutes = minimum == 0 ? null : minimum, RoundingMinutes = rounding == 0 ? null : rounding };
			InvoicingService.ApplyRounding(minutes, item).Should().Be(expectedHours);
		}

		// ---------------------------------------------------------------- create / send / void

		[Test]
		public async Task Create_draft_assigns_the_next_number_and_the_profile_discount_and_publishes_created()
		{
			_profiles.Setup(p => p.GetByContactIdAsync("contact-1", 7)).ReturnsAsync(Profile(discount: 10m));
			_sequence.Setup(s => s.GetNextNumberAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(1042);

			var invoice = await Build().CreateDraftInvoiceAsync(7, "contact-1", "user-1", "127.0.0.1", "test", "cad");

			invoice.InvoiceNumber.Should().Be(1042);
			invoice.Status.Should().Be((int)InvoiceStatus.Draft);
			invoice.DiscountPercent.Should().Be(10m);
			invoice.Currency.Should().Be("CAD");
			invoice.CustomerBillingProfileId.Should().Be("profile-1");
			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoiceCreated);
			_published.Single().AggregateId.Should().Be(invoice.InvoiceId);
			_audits.Select(a => a.Type).Should().Equal(AuditLogTypes.InvoiceCreated);
		}

		[Test]
		public async Task Create_draft_requires_an_active_billing_profile()
		{
			_profiles.Setup(p => p.GetByContactIdAsync("contact-1", 7)).ReturnsAsync((CustomerBillingProfile)null);
			var act = async () => await Build().CreateDraftInvoiceAsync(7, "contact-1", "user-1", null, null);
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_profile_required");
			_sequence.Verify(s => s.GetNextNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Mark_sent_needs_lines_sets_dates_from_terms_and_publishes_sent()
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, Status = (int)InvoiceStatus.Draft, CustomerBillingProfileId = "profile-1", ContactId = "contact-1" };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", 7)).ReturnsAsync(invoice);
			_profiles.Setup(p => p.GetByIdForDepartmentAsync("profile-1", 7)).ReturnsAsync(Profile(taxRate: 5m));
			_lineItems.Setup(l => l.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoiceLineItem>());
			_payments.Setup(p => p.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoicePayment>());

			var noLines = async () => await Build().MarkSentAsync("inv-1", 7, null, "user-1", null, null);
			await noLines.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_invoice_has_no_lines");

			_lineItems.Setup(l => l.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoiceLineItem> { Line(100m) });
			var sent = await Build().MarkSentAsync("inv-1", 7, "ap@acme.test", "user-1", null, null);

			sent.Status.Should().Be((int)InvoiceStatus.Sent);
			sent.Total.Should().Be(105.00m);
			sent.IssuedOn.Should().NotBeNull();
			sent.DueOn.Should().Be(sent.IssuedOn.Value.AddDays(30));
			sent.SentToEmail.Should().Be("ap@acme.test");
			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoiceSent);
		}

		[Test]
		public async Task Void_refuses_paid_invoices_and_publishes_voided_otherwise()
		{
			var paid = new Invoice { InvoiceId = "inv-p", DepartmentId = 7, Status = (int)InvoiceStatus.Paid, Total = 10, AmountPaid = 10 };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-p", 7)).ReturnsAsync(paid);
			var act = async () => await Build().VoidInvoiceAsync("inv-p", 7, "dup", "user-1", null, null);
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_invoice_paid_cannot_void");

			var sent = new Invoice { InvoiceId = "inv-s", DepartmentId = 7, Status = (int)InvoiceStatus.Sent, Total = 10, CustomerBillingProfileId = "profile-1", ContactId = "contact-1" };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-s", 7)).ReturnsAsync(sent);
			_lineItems.Setup(l => l.GetByInvoiceIdAsync("inv-s", 7)).ReturnsAsync(new List<InvoiceLineItem>());
			_payments.Setup(p => p.GetByInvoiceIdAsync("inv-s", 7)).ReturnsAsync(new List<InvoicePayment>());
			var voided = await Build().VoidInvoiceAsync("inv-s", 7, "duplicate", "user-1", null, null);
			voided.Status.Should().Be((int)InvoiceStatus.Void);
			voided.VoidReason.Should().Be("duplicate");
			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoiceVoided);
		}

		// ---------------------------------------------------------------- payments (the choke point)

		private Invoice SentInvoice(decimal total, params InvoicePayment[] existing)
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, Status = (int)InvoiceStatus.Sent, Total = total, CustomerBillingProfileId = "profile-1", ContactId = "contact-1", DueOn = DateTime.UtcNow.AddDays(10) };
			var stored = existing.ToList();
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", 7)).ReturnsAsync(invoice);
			_payments.Setup(p => p.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(() => stored.ToList());
			_payments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePayment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((InvoicePayment p, CancellationToken _, bool __) => { p.InvoicePaymentId ??= Guid.NewGuid().ToString(); if (!stored.Contains(p)) stored.Add(p); return p; });
			return invoice;
		}

		[Test]
		public async Task Partial_then_full_payment_moves_sent_to_partially_paid_to_paid_and_publishes_paid_once()
		{
			var invoice = SentInvoice(100m);
			var service = Build();

			await service.RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-1", DepartmentId = 7, Amount = 40m, Method = (int)InvoicePaymentMethods.Check, Reference = "1001" }, "user-1", null, null);
			invoice.Status.Should().Be((int)InvoiceStatus.PartiallyPaid);
			invoice.AmountPaid.Should().Be(40m);
			invoice.PaidOn.Should().BeNull();

			await service.RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-1", DepartmentId = 7, Amount = 60m, Method = (int)InvoicePaymentMethods.Cash }, "user-1", null, null);
			invoice.Status.Should().Be((int)InvoiceStatus.Paid);
			invoice.AmountPaid.Should().Be(100m);
			invoice.PaidOn.Should().NotBeNull();

			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoicePaymentRecorded, WorkflowTriggerEventType.InvoicePaymentRecorded, WorkflowTriggerEventType.InvoicePaid);
			_audits.Select(a => a.Type).Should().Equal(AuditLogTypes.InvoicePaymentRecorded, AuditLogTypes.InvoicePaymentRecorded);
		}

		[Test]
		public async Task Online_payment_replays_are_idempotent_on_the_gateway_transaction_id()
		{
			SentInvoice(100m);
			var first = new InvoicePayment { InvoicePaymentId = "pay-1", InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" };
			_payments.Setup(p => p.GetByGatewayTransactionIdAsync((int)PaymentProviders.Stripe, "pi_123")).ReturnsAsync(first);

			var result = await Build().RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" }, null, null, null);

			result.Should().BeSameAs(first);
			_published.Should().BeEmpty();
			_payments.Verify(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePayment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Draft_and_void_invoices_are_not_payable()
		{
			var draft = new Invoice { InvoiceId = "inv-d", DepartmentId = 7, Status = (int)InvoiceStatus.Draft, Total = 10 };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-d", 7)).ReturnsAsync(draft);
			var act = async () => await Build().RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-d", DepartmentId = 7, Amount = 5, Method = 0 }, "u", null, null);
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_invoice_not_payable");
		}

		[Test]
		public async Task Refund_reopens_the_balance_and_a_lost_dispute_removes_the_payment_entirely()
		{
			var payment = new InvoicePayment { InvoicePaymentId = "pay-1", InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, PaidOn = DateTime.UtcNow };
			var invoice = SentInvoice(100m, payment);
			invoice.Status = (int)InvoiceStatus.Paid; invoice.AmountPaid = 100m; invoice.PaidOn = DateTime.UtcNow;
			_payments.Setup(p => p.GetByIdForDepartmentAsync("pay-1", 7)).ReturnsAsync(payment);
			var service = Build();

			await service.ApplyPaymentRefundAsync("pay-1", 7, 30m, false, "user-1", null, null);
			payment.Status.Should().Be((int)InvoicePaymentStatuses.PartiallyRefunded);
			invoice.AmountPaid.Should().Be(70m);
			invoice.Status.Should().Be((int)InvoiceStatus.PartiallyPaid);
			invoice.PaidOn.Should().BeNull();

			await service.ApplyPaymentRefundAsync("pay-1", 7, 100m, true, "user-1", null, null);
			payment.Status.Should().Be((int)InvoicePaymentStatuses.DisputeLost);
			invoice.AmountPaid.Should().Be(0m);
			invoice.Status.Should().Be((int)InvoiceStatus.Sent);

			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoicePaymentRefunded, WorkflowTriggerEventType.InvoicePaymentDisputed);
		}

		// ---------------------------------------------------------------- overdue sweep + aging

		[Test]
		public async Task Overdue_sweep_transitions_only_sent_and_partially_paid_and_publishes_each_once()
		{
			var asOf = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
			var candidates = new List<Invoice>
			{
				new Invoice { InvoiceId = "a", DepartmentId = 7, Status = (int)InvoiceStatus.Sent, DueOn = asOf.AddDays(-1), ContactId = "contact-1" },
				new Invoice { InvoiceId = "b", DepartmentId = 7, Status = (int)InvoiceStatus.PartiallyPaid, DueOn = asOf.AddDays(-9), ContactId = "contact-1" },
				new Invoice { InvoiceId = "c", DepartmentId = 7, Status = (int)InvoiceStatus.Paid, DueOn = asOf.AddDays(-9), ContactId = "contact-1" }
			};
			_invoices.Setup(r => r.GetOverdueCandidatesAsync(asOf, It.IsAny<int>())).ReturnsAsync(candidates);

			var count = await Build().MarkOverdueInvoicesAsync(asOf);

			count.Should().Be(2);
			candidates.Take(2).Select(x => x.Status).Should().AllBeEquivalentTo((int)InvoiceStatus.Overdue);
			candidates[2].Status.Should().Be((int)InvoiceStatus.Paid);
			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoiceOverdue, WorkflowTriggerEventType.InvoiceOverdue);
		}

		[Test]
		public async Task Aging_buckets_open_balances_by_days_past_due()
		{
			var asOf = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
			_invoices.Setup(r => r.GetAgingDataAsync(7)).ReturnsAsync(new List<InvoiceAgingRow>
			{
				new InvoiceAgingRow { InvoiceId = "1", Total = 100, AmountPaid = 0, DueOn = asOf.AddDays(5) },
				new InvoiceAgingRow { InvoiceId = "2", Total = 100, AmountPaid = 25, DueOn = asOf.AddDays(-10) },
				new InvoiceAgingRow { InvoiceId = "3", Total = 100, AmountPaid = 0, DueOn = asOf.AddDays(-45) },
				new InvoiceAgingRow { InvoiceId = "4", Total = 100, AmountPaid = 0, DueOn = asOf.AddDays(-75) },
				new InvoiceAgingRow { InvoiceId = "5", Total = 100, AmountPaid = 0, DueOn = asOf.AddDays(-200) },
				new InvoiceAgingRow { InvoiceId = "6", Total = 100, AmountPaid = 100, DueOn = asOf.AddDays(-200) }
			});

			var report = await Build().GetAccountsReceivableAgingAsync(7, asOf);

			report.Buckets.Select(b => (b.Label, b.Count, b.Balance)).Should().Equal(("Current", 1, 100m), ("1-30", 1, 75m), ("31-60", 1, 100m), ("61-90", 1, 100m), ("90+", 1, 100m));
			report.TotalBalance.Should().Be(475m);
			report.TotalCount.Should().Be(5);
		}

		// ---------------------------------------------------------------- rendering + delivery (B4)

		[Test]
		public void Render_shows_department_identity_customer_lines_discount_tax_components_and_balance()
		{
			var invoice = new Invoice
			{
				InvoiceId = "inv-1", InvoiceNumber = 1042, Status = (int)InvoiceStatus.PartiallyPaid, Currency = "CAD", SubTotal = 1000m, DiscountPercent = 10m, DiscountAmount = 100m,
				TaxAmount = 108m, Total = 1008m, AmountPaid = 500m, IssuedOn = new DateTime(2026, 9, 18), DueOn = new DateTime(2026, 10, 18), TermsText = "Net 30",
				TaxComponentsJson = "[{\"Name\":\"GST\",\"RatePercent\":5,\"RegistrationNumber\":\"123456789 RT0001\",\"Amount\":45},{\"Name\":\"BC PST\",\"RatePercent\":7,\"RegistrationNumber\":\"PST-1001\",\"Amount\":63}]",
				LineItems = new List<InvoiceLineItem> { new InvoiceLineItem { Description = "Engine 1 standby <script>", Quantity = 4m, UnitRate = 250m, Amount = 1000m, Taxable = true, SortOrder = 0 } }
			};
			var model = new InvoiceRenderModel
			{
				Invoice = invoice, DepartmentName = "Test County Fire & Rescue", TaxRegistrationNumber = "123456789 RT0001", SecondaryTaxRegistrationNumber = "PST-1001",
				RemitTo = new Address { Address1 = "1 Main St", City = "Tahoe", State = "CA", PostalCode = "96150", Country = "USA" },
				CustomerName = "Acme Logistics", CustomerEmail = "ap@acme.test", TaxComponents = InvoicingService.ParseTaxComponents(invoice.TaxComponentsJson), FooterText = "Thank you"
			};

			var html = InvoicingService.RenderInvoiceHtml(model);

			html.Should().Contain("Test County Fire &amp; Rescue").And.Contain("1 Main St, Tahoe CA 96150, USA").And.Contain("Tax registration: 123456789 RT0001");
			html.Should().Contain("<strong>1042</strong>").And.Contain("Partially paid").And.Contain("2026-09-18").And.Contain("2026-10-18");
			html.Should().Contain("Acme Logistics").And.Contain("ap@acme.test");
			html.Should().Contain("Engine 1 standby &lt;script&gt;").And.NotContain("<script>");
			html.Should().Contain("Discount (10%)").And.Contain("-100.00 CAD");
			html.Should().Contain("GST (5%)").And.Contain("123456789 RT0001").And.Contain("45.00 CAD").And.Contain("BC PST (7%)").And.Contain("63.00 CAD");
			html.Should().Contain("1,008.00 CAD").And.Contain("Balance due").And.Contain("508.00 CAD");
			html.Should().Contain("Net 30").And.Contain("Thank you").And.NotContain("Pay this invoice online");
		}

		[Test]
		public void Render_hides_customer_details_and_notes_on_a_protected_invoice()
		{
			var invoice = new Invoice { InvoiceId = "inv-1", InvoiceNumber = 7, Status = (int)InvoiceStatus.Sent, Currency = "USD", Total = 10m, Notes = "secret", IsProtected = true, LineItems = new List<InvoiceLineItem>() };
			var html = InvoicingService.RenderInvoiceHtml(new InvoiceRenderModel { Invoice = invoice, DepartmentName = "D", CustomerName = ProtectedDataEnvelope.RedactionValue });
			html.Should().Contain(ProtectedDataEnvelope.RedactionValue).And.NotContain("secret");
		}

		[Test]
		public async Task Send_marks_a_draft_sent_then_emails_the_pdf_to_the_billing_email()
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, InvoiceNumber = 1042, Status = (int)InvoiceStatus.Draft, CustomerBillingProfileId = "profile-1", ContactId = "contact-1", Currency = "USD" };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", 7)).ReturnsAsync(invoice);
			var profile = Profile(taxRate: 0m); profile.BillingEmail = "ap@acme.test";
			_profiles.Setup(p => p.GetByIdForDepartmentAsync("profile-1", 7)).ReturnsAsync(profile);
			_lineItems.Setup(l => l.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoiceLineItem> { Line(100m) });
			_payments.Setup(p => p.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoicePayment>());
			_identities.Setup(i => i.GetByDepartmentIdAsync(7)).ReturnsAsync(new DepartmentBillingIdentity { DepartmentId = 7, LegalBusinessName = "Test County Fire District" });
			EmailNotification captured = null;
			_email.Setup(e => e.SendInvoiceAsync(It.IsAny<EmailNotification>(), 7, It.IsAny<string>(), null, "Invoice #1042"))
				.Callback<EmailNotification, int, string, string, string>((n, _, __, ___, ____) => captured = n).ReturnsAsync(true);

			var result = await Build().SendInvoiceAsync("inv-1", 7, null, "user-1", null, null);

			result.Status.Should().Be((int)InvoiceStatus.Sent);
			result.SentToEmail.Should().Be("ap@acme.test");
			captured.Should().NotBeNull();
			captured.To.Should().Be("ap@acme.test");
			captured.Subject.Should().Be("Invoice #1042 from Test County Fire District");
			captured.AttachmentName.Should().Be("invoice-1042.pdf");
			System.Text.Encoding.UTF8.GetString(captured.AttachmentData).Should().Contain("<strong>1042</strong>");
			_published.Select(x => x.Trigger).Should().Equal(WorkflowTriggerEventType.InvoiceSent);
		}

		[Test]
		public async Task Send_refuses_void_invoices_and_invoices_without_a_recipient()
		{
			var voided = new Invoice { InvoiceId = "inv-v", DepartmentId = 7, Status = (int)InvoiceStatus.Void, CustomerBillingProfileId = "profile-1" };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-v", 7)).ReturnsAsync(voided);
			var act = async () => await Build().SendInvoiceAsync("inv-v", 7, null, "u", null, null);
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_invoice_void");

			var sent = new Invoice { InvoiceId = "inv-s", DepartmentId = 7, Status = (int)InvoiceStatus.Sent, CustomerBillingProfileId = "profile-1" };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-s", 7)).ReturnsAsync(sent);
			_profiles.Setup(p => p.GetByIdForDepartmentAsync("profile-1", 7)).ReturnsAsync(Profile());
			var noRecipient = async () => await Build().SendInvoiceAsync("inv-s", 7, "  ", "u", null, null);
			await noRecipient.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_no_recipient_email");
			_email.Verify(e => e.SendInvoiceAsync(It.IsAny<EmailNotification>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Overdue_sweep_skips_departments_whose_entitlement_lapsed()
		{
			var asOf = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
			_invoices.Setup(r => r.GetOverdueCandidatesAsync(asOf, It.IsAny<int>())).ReturnsAsync(new List<Invoice>
			{
				new Invoice { InvoiceId = "a", DepartmentId = 7, Status = (int)InvoiceStatus.Sent, DueOn = asOf.AddDays(-1), ContactId = "contact-1" },
				new Invoice { InvoiceId = "b", DepartmentId = 8, Status = (int)InvoiceStatus.Sent, DueOn = asOf.AddDays(-1), ContactId = "contact-1" }
			});
			var asked = new List<int>();
			var count = await Build().MarkOverdueInvoicesAsync(asOf, d => { asked.Add(d); return Task.FromResult(d == 7); });
			count.Should().Be(1);
			asked.Should().Equal(7, 8);
		}

		// ---------------------------------------------------------------- workflow payload hygiene

		[Test]
		public async Task Workflow_payload_redacts_the_contact_name_on_a_protected_invoice()
		{
			_profiles.Setup(p => p.GetByContactIdAsync("contact-1", 7)).ReturnsAsync(Profile());
			_sequence.Setup(s => s.GetNextNumberAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(1);
			_invoices.Setup(r => r.SaveOrUpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Invoice i, CancellationToken _, bool __) => { i.InvoiceId ??= "inv-x"; i.IsProtected = true; return i; });

			await Build().CreateDraftInvoiceAsync(7, "contact-1", "user-1", null, null);

			var payload = Newtonsoft.Json.Linq.JObject.FromObject(_published.Single().Payload);
			((string)payload["ContactName"]).Should().Be(ProtectedDataEnvelope.RedactionValue);
			payload.Properties().Select(p => p.Name).Should().NotContain(new[] { "Notes", "SentToEmail", "VoidReason", "BillingEmail" });
		}

		[Test]
		public void Invoice_triggers_are_registered_in_the_payload_and_have_localized_names()
		{
			foreach (var trigger in new[] { 52, 53, 54, 55, 56, 57, 94, 95 })
				InvoiceWorkflowPayload.IsInvoice(trigger).Should().BeTrue($"trigger {trigger} is an invoice trigger");
			InvoiceWorkflowPayload.IsInvoice(70).Should().BeFalse();
			((int)WorkflowTriggerEventType.InvoiceCreated).Should().Be(52);
			((int)WorkflowTriggerEventType.InvoiceVoided).Should().Be(57);
			((int)WorkflowTriggerEventType.InvoicePaymentRefunded).Should().Be(94);
			((int)WorkflowTriggerEventType.InvoicePaymentDisputed).Should().Be(95);
		}
		// ---------------------------------------------------------------- PR #512 review: line-item save, payment idempotency, aging currencies

		private Invoice DraftWithProfile()
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, Status = (int)InvoiceStatus.Draft, CustomerBillingProfileId = "profile-1", ContactId = "contact-1", Currency = "USD" };
			var stored = new List<InvoiceLineItem>();
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", 7)).ReturnsAsync(invoice);
			_profiles.Setup(p => p.GetByIdForDepartmentAsync("profile-1", 7)).ReturnsAsync(Profile());
			_payments.Setup(p => p.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(new List<InvoicePayment>());
			_lineItems.Setup(l => l.DeleteByInvoiceIdAsync("inv-1", 7, It.IsAny<CancellationToken>())).Callback(() => stored.Clear()).ReturnsAsync(0);
			_lineItems.Setup(l => l.GetByInvoiceIdAsync("inv-1", 7)).ReturnsAsync(() => stored.ToList());
			_lineItems.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoiceLineItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((InvoiceLineItem l, CancellationToken _, bool __) => { l.InvoiceLineItemId ??= Guid.NewGuid().ToString(); stored.Add(l); return l; });
			return invoice;
		}

		[Test]
		public async Task Saving_line_items_re_applies_the_rate_card_minimum_charge()
		{
			DraftWithProfile();
			_rateCardItems.Setup(r => r.GetByIdForDepartmentAsync("item-1", 7)).ReturnsAsync(new RateCardItem { RateCardItemId = "item-1", DepartmentId = 7, Rate = 100m, MinimumCharge = 150m });

			var saved = await Build().SaveInvoiceLineItemsAsync("inv-1", 7, new List<InvoiceLineItem>
			{
				new InvoiceLineItem { Description = "Engine, half an hour", RateCardItemId = "item-1", Quantity = 0.5m, UnitRate = 100m },
				new InvoiceLineItem { Description = "Engine, two hours", RateCardItemId = "item-1", Quantity = 2m, UnitRate = 100m },
				new InvoiceLineItem { Description = "Engine, nothing yet", RateCardItemId = "item-1", Quantity = 0m, UnitRate = 100m },
				new InvoiceLineItem { Description = "Ad hoc", Quantity = 0.5m, UnitRate = 100m }
			}, "user-1", null, null);

			saved.LineItems.Select(l => l.Amount).Should().Equal(150m, 200m, 0m, 50m);
			saved.SubTotal.Should().Be(400m);
			_rateCardItems.Verify(r => r.GetByIdForDepartmentAsync("item-1", 7), Times.Once, "the minimum is looked up once per rate card item");
		}

		[Test]
		public async Task Draft_save_commits_header_and_lines_as_one_transaction()
		{
			var invoice = DraftWithProfile();
			var edit = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, Notes = "net 30", Currency = "EUR", DiscountPercent = 0 };

			var saved = await Build().SaveDraftAsync(edit, new List<InvoiceLineItem> { new InvoiceLineItem { Description = "Engine", Quantity = 1, UnitRate = 100m } }, "user-1", null, null);

			saved.Notes.Should().Be("net 30");
			saved.Currency.Should().Be("EUR");
			saved.SubTotal.Should().Be(100m);
			invoice.Notes.Should().Be("net 30");
			_unitOfWork.Opened.Should().Be(1, "the line-item save joins the draft save's transaction");
			_unitOfWork.Commits.Should().Be(1);
			_unitOfWork.Discards.Should().Be(0);
		}

		[Test]
		public async Task Draft_save_rolls_back_when_a_line_is_refused()
		{
			DraftWithProfile();
			var edit = new Invoice { InvoiceId = "inv-1", DepartmentId = 7, Notes = "net 30", Currency = "USD" };

			var act = async () => await Build().SaveDraftAsync(edit, new List<InvoiceLineItem> { new InvoiceLineItem { Description = " ", Quantity = 1, UnitRate = 100m } }, "user-1", null, null);

			await act.Should().ThrowAsync<ArgumentException>();
			_unitOfWork.Opened.Should().Be(1);
			_unitOfWork.Commits.Should().Be(0);
			_unitOfWork.Discards.Should().Be(1);
		}

		[Test]
		public async Task A_gateway_transaction_recorded_under_another_department_is_a_conflict_not_a_replay()
		{
			SentInvoice(100m);
			var elsewhere = new InvoicePayment { InvoicePaymentId = "pay-9", InvoiceId = "inv-9", DepartmentId = 9, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" };
			_payments.Setup(p => p.GetByGatewayTransactionIdAsync((int)PaymentProviders.Stripe, "pi_123")).ReturnsAsync(elsewhere);

			var act = async () => await Build().RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" }, null, null, null);

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invoicing_payment_conflict");
			_payments.Verify(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePayment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_delivery_that_loses_the_insert_race_returns_the_winner_instead_of_a_second_payment()
		{
			SentInvoice(100m);
			var winner = new InvoicePayment { InvoicePaymentId = "pay-1", InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" };
			var lookups = 0;
			_payments.Setup(p => p.GetByGatewayTransactionIdAsync((int)PaymentProviders.Stripe, "pi_123")).ReturnsAsync(() => ++lookups == 1 ? null : winner);
			_payments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePayment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new Npgsql.PostgresException("duplicate key value violates unique constraint \"ux_invoicepayments_gateway\"", "ERROR", "ERROR", "23505"));

			var result = await Build().RecordPaymentAsync(new InvoicePayment { InvoiceId = "inv-1", DepartmentId = 7, Amount = 100m, Method = (int)InvoicePaymentMethods.Online, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_123" }, null, null, null);

			result.Should().BeSameAs(winner);
			_published.Should().BeEmpty("the winning delivery already published the payment");
		}

		[Test]
		public async Task Aging_keeps_balances_apart_per_currency()
		{
			var asOf = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
			_invoices.Setup(r => r.GetAgingDataAsync(7)).ReturnsAsync(new List<InvoiceAgingRow>
			{
				new InvoiceAgingRow { InvoiceId = "1", Currency = "USD", Total = 100, DueOn = asOf.AddDays(5) },
				new InvoiceAgingRow { InvoiceId = "2", Currency = "EUR", Total = 80, DueOn = asOf.AddDays(-10) },
				new InvoiceAgingRow { InvoiceId = "3", Currency = "usd", Total = 50, DueOn = asOf.AddDays(-10) }
			});

			var report = await Build().GetAccountsReceivableAgingAsync(7, asOf);

			report.BalancesByCurrency.Should().Equal(new SortedDictionary<string, decimal> { ["EUR"] = 80m, ["USD"] = 150m });
			report.Buckets.Single(b => b.Label == "1-30").BalancesByCurrency.Should().Equal(new SortedDictionary<string, decimal> { ["EUR"] = 80m, ["USD"] = 50m });
			report.Buckets.Single(b => b.Label == "Current").BalancesByCurrency.Should().Equal(new SortedDictionary<string, decimal> { ["USD"] = 100m });
		}
	}
}
