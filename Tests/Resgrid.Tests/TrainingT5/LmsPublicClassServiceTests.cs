using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Paid public classes (R-C13, §25.4a item 12): a fee per external learner (members never pay), the roster export that always works,
	/// and Business Operations invoices only where that add-on's invoicing is active, one per learner or per sponsoring organization.
	/// </summary>
	[TestFixture]
	public class LmsPublicClassServiceTests
	{
		private const int DepartmentId = 72;
		private const int SessionId = 8;
		private const string Member = "6d3f8b22-2a4c-4e3d-8b7f-1c2d3e4f5a62";

		private string _a, _b, _c;
		private LmsPublicClass _publicClass;
		private List<LmsClassFee> _fees;
		private List<LmsExternalLearner> _learners;
		private bool _invoicing;
		private Mock<IInvoicingService> _invoices;
		private Mock<IContactsService> _contacts;
		private LmsPublicClassService _service;
		private LmsClassSession _session;

		[SetUp]
		public void SetUp()
		{
			_a = LmsExternalLearnerIds.New();
			_b = LmsExternalLearnerIds.New();
			_c = LmsExternalLearnerIds.New();
			_invoicing = true;
			_publicClass = new LmsPublicClass { LmsPublicClassId = 1, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, IsPublic = true, FeeAmount = 45m, Currency = "USD", RateCardItemId = "rate-cpr" };
			_learners = new List<LmsExternalLearner>
			{
				new LmsExternalLearner { LmsExternalLearnerId = 1, DepartmentId = DepartmentId, LearnerUserId = _a, FirstName = "Ann", LastName = "A", Email = "a@x.org", Organization = "Acme", SponsorContactId = "acme" },
				new LmsExternalLearner { LmsExternalLearnerId = 2, DepartmentId = DepartmentId, LearnerUserId = _b, FirstName = "Bob", LastName = "B", Email = "b@x.org", Organization = "Acme", SponsorContactId = "acme" },
				new LmsExternalLearner { LmsExternalLearnerId = 3, DepartmentId = DepartmentId, LearnerUserId = _c, FirstName = "Cy", LastName = "C", Email = "c@x.org", BillingContactId = "cy-customer" }
			};
			_fees = new List<LmsClassFee>();
			var feeRepo = new Mock<ILmsClassFeesRepository>();
			feeRepo.Setup(f => f.GetForSessionAsync(DepartmentId, SessionId)).ReturnsAsync(() => _fees.ToList());
			feeRepo.Setup(f => f.GetByEnrollmentAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int _, int id) => _fees.FirstOrDefault(f => f.LmsClassEnrollmentId == id));
			feeRepo.Setup(f => f.InsertAsync(It.IsAny<LmsClassFee>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsClassFee f, CancellationToken _, bool _) => { f.LmsClassFeeId = _fees.Count + 1; _fees.Add(f); return f; });
			feeRepo.Setup(f => f.SaveOrUpdateAsync(It.IsAny<LmsClassFee>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsClassFee f, CancellationToken _, bool _) => f);
			feeRepo.Setup(f => f.ClaimAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, IEnumerable<int> ids, string claim, CancellationToken _) =>
				{
					var rows = _fees.Where(f => ids.Contains(f.LmsClassFeeId) && f.InvoiceId == null && !f.Waived).ToList();
					rows.ForEach(f => f.InvoiceId = claim);
					return rows.Count;
				});
			feeRepo.Setup(f => f.CompleteClaimAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, IEnumerable<int> ids, string claim, string invoiceId, DateTime _, CancellationToken _) =>
				{
					var rows = _fees.Where(f => ids.Contains(f.LmsClassFeeId) && f.InvoiceId == claim).ToList();
					rows.ForEach(f => f.InvoiceId = invoiceId);
					return rows.Count;
				});

			var publicClasses = new Mock<ILmsPublicClassesRepository>();
			publicClasses.Setup(p => p.GetBySessionAsync(DepartmentId, SessionId)).ReturnsAsync(() => _publicClass);
			publicClasses.Setup(p => p.InsertAsync(It.IsAny<LmsPublicClass>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsPublicClass p, CancellationToken _, bool _) => p);
			publicClasses.Setup(p => p.SaveOrUpdateAsync(It.IsAny<LmsPublicClass>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsPublicClass p, CancellationToken _, bool _) => p);

			var learnerRepo = new Mock<ILmsExternalLearnersRepository>();
			learnerRepo.Setup(r => r.GetByLearnerUserIdAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((int _, string id) => _learners.FirstOrDefault(l => l.LearnerUserId == id));
			learnerRepo.Setup(r => r.GetByLearnerUserIdsAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, IEnumerable<string> ids) => _learners.Where(l => ids.Contains(l.LearnerUserId)).ToList());

			var session = _session = new LmsClassSession { LmsClassSessionId = SessionId, DepartmentId = DepartmentId, Title = "CPR", StartsOn = new DateTime(2027, 3, 1, 9, 0, 0), Currency = "USD", Status = (int)LmsClassSessionStatuses.SignedOff };
			LmsClassRosterRow Row(int id, string userId, string name, int attendance = (int)LmsClassAttendanceStates.Attended) => new LmsClassRosterRow
			{
				Name = name,
				Enrollment = new LmsClassEnrollment { LmsClassEnrollmentId = id, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, UserId = userId, Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = attendance, Hours = 3m }
			};
			var classes = new Mock<ILmsClassService>();
			classes.Setup(c => c.GetSessionAsync(DepartmentId, SessionId)).ReturnsAsync(session);
			classes.Setup(c => c.GetDetailAsync(DepartmentId, SessionId, It.IsAny<DateTime>())).ReturnsAsync(() => new LmsClassSessionDetail
			{
				Session = session,
				Roster = new List<LmsClassRosterRow> { Row(1, _a, "Ann A"), Row(2, _b, "Bob B"), Row(3, _c, "Cy C"), Row(4, Member, "Pat Member") }
			});

			var businessOps = new Mock<IBusinessOperationsAccessService>();
			businessOps.Setup(b => b.CanUseInvoicingAsync(DepartmentId)).ReturnsAsync(() => _invoicing);
			_invoices = new Mock<IInvoicingService>();
			var invoiceNumber = 0;
			_invoices.Setup(i => i.CreateDraftInvoiceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new Invoice { InvoiceId = "inv-" + (++invoiceNumber), DepartmentId = DepartmentId });
			_invoices.Setup(i => i.GetBillingProfileByContactIdAsync("cy-customer", DepartmentId)).ReturnsAsync(new CustomerBillingProfile { ContactId = "cy-customer", Active = true });
			_invoices.Setup(i => i.GetBillingProfilesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomerBillingProfile>
			{
				new CustomerBillingProfile { DepartmentId = DepartmentId, ContactId = "acme", Active = true }
			});
			_contacts = new Mock<IContactsService>();

			_service = new LmsPublicClassService(publicClasses.Object, feeRepo.Object, learnerRepo.Object, classes.Object, businessOps.Object, _invoices.Object, _contacts.Object,
				Mock.Of<IEventAggregator>());
		}

		private async Task FeesForEveryoneAsync()
		{
			foreach (var (id, userId) in new[] { (1, _a), (2, _b), (3, _c), (4, Member) })
				await _service.EnsureFeeAsync(new LmsClassEnrollment { LmsClassEnrollmentId = id, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, UserId = userId });
		}

		[Test]
		public async Task External_learners_carry_the_class_fee_and_members_never_do()
		{
			await FeesForEveryoneAsync();

			_fees.Should().HaveCount(3);
			_fees.Should().OnlyContain(f => LmsExternalLearnerIds.IsExternal(f.LearnerUserId) && f.Amount == 45m && f.Currency == "USD");
			_fees.Single(f => f.LearnerUserId == _a).SponsorContactId.Should().Be("acme", "the learner's sponsoring organization is copied");
			await _service.EnsureFeeAsync(new LmsClassEnrollment { LmsClassEnrollmentId = 1, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, UserId = _a });
			_fees.Should().HaveCount(3, "one fee per roster place");

			_publicClass.FeeAmount = null;
			(await _service.EnsureFeeAsync(new LmsClassEnrollment { LmsClassEnrollmentId = 9, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, UserId = LmsExternalLearnerIds.New() }))
				.Should().BeNull("a free class has no fee");
		}

		[Test]
		public async Task Fees_are_validated_and_saved_rounded()
		{
			await FluentActions.Awaiting(() => _service.SavePublicClassAsync(DepartmentId, SessionId, true, -1m, "USD", null, "officer")).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_xl_fee_invalid");
			await FluentActions.Awaiting(() => _service.SavePublicClassAsync(DepartmentId, SessionId, true, 10m, "USD", "not-an-item", "officer")).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_xl_rate_item_invalid");
			var saved = await _service.SavePublicClassAsync(DepartmentId, SessionId, true, 45.555m, "eur", null, "officer");
			saved.FeeAmount.Should().Be(45.56m);
			saved.Currency.Should().Be("EUR");
			(await _service.SavePublicClassAsync(DepartmentId, SessionId, true, 0m, "x", null, "officer")).FeeAmount.Should().BeNull("zero is a free class");
		}

		[Test]
		public async Task A_fee_without_its_own_currency_takes_the_classs_and_never_a_fixed_dollar()
		{
			// Round 9C (region neutrality): the class carries the department's training currency (CAD here), and the fee follows it.
			_session.Currency = "CAD";
			var saved = await _service.SavePublicClassAsync(DepartmentId, SessionId, true, 30m, null, null, "officer");
			saved.Currency.Should().Be("CAD");
			(await _service.SavePublicClassAsync(DepartmentId, SessionId, true, 30m, "$", null, "officer")).Currency.Should().Be("CAD", "a symbol is not a currency code");
			var fee = await _service.EnsureFeeAsync(new LmsClassEnrollment { LmsClassEnrollmentId = 21, DepartmentId = DepartmentId, LmsClassSessionId = SessionId, UserId = _a });
			fee.Currency.Should().Be("CAD");
			fee.Amount.Should().Be(30m);
		}

		[Test]
		public async Task The_roster_always_exports_without_business_operations()
		{
			_invoicing = false;
			await FeesForEveryoneAsync();

			var csv = await _service.ExportRosterCsvAsync(DepartmentId, SessionId, key => key);
			var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

			lines.Should().HaveCount(5, "a header, three external learners and the member");
			lines.Single(l => l.StartsWith("Ann A")).Should().Contain("a@x.org").And.Contain("Acme").And.Contain("45.00").And.Contain("FeeStatus.Due");
			lines.Single(l => l.StartsWith("Pat Member")).Should().Contain("Kind.Member").And.NotContain("@", "a member's address is not exported");
			_invoices.Verify(i => i.GetInvoiceByIdAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Without_business_operations_invoicing_no_invoice_is_raised()
		{
			_invoicing = false;
			await FeesForEveryoneAsync();

			var run = await _service.RaiseInvoicesAsync(DepartmentId, SessionId, LmsClassInvoiceModes.PerLearner, "officer", "127.0.0.1", "test");

			run.Error.Should().Be("lms_xl_invoicing_unavailable");
			_invoices.Verify(i => i.CreateDraftInvoiceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			(await _service.GetCustomersAsync(DepartmentId)).Should().BeEmpty();
			(await _service.GetBillingAsync(DepartmentId, SessionId)).InvoicingAvailable.Should().BeFalse();
		}

		[Test]
		public async Task Per_organization_one_invoice_per_sponsor_and_learners_without_one_are_skipped()
		{
			await FeesForEveryoneAsync();

			var run = await _service.RaiseInvoicesAsync(DepartmentId, SessionId, LmsClassInvoiceModes.PerOrganization, "officer", "127.0.0.1", "test");

			run.Error.Should().BeNull();
			run.InvoiceIds.Should().ContainSingle();
			run.LearnersInvoiced.Should().Be(2);
			run.Skipped.Should().ContainSingle().Which.Value.Should().Be("lms_xl_no_sponsor");
			_invoices.Verify(i => i.CreateDraftInvoiceAsync(DepartmentId, "acme", "officer", "127.0.0.1", "test", "USD", It.IsAny<CancellationToken>()), Times.Once);
			_invoices.Verify(i => i.SaveInvoiceLineItemsAsync("inv-1", DepartmentId, It.Is<List<InvoiceLineItem>>(lines =>
				lines.Count == 2 && lines.All(l => l.Amount == 45m && l.Quantity == 1m && l.RateCardItemId == "rate-cpr" && l.Description.Contains("CPR"))),
				"officer", "127.0.0.1", "test", It.IsAny<CancellationToken>()), Times.Once);
			_fees.Where(f => f.LearnerUserId != _c).Should().OnlyContain(f => f.InvoiceId == "inv-1");

			var again = await _service.RaiseInvoicesAsync(DepartmentId, SessionId, LmsClassInvoiceModes.PerOrganization, "officer", "127.0.0.1", "test");
			again.InvoiceIds.Should().BeEmpty("each fee is invoiced once");
		}

		[Test]
		public async Task Per_learner_uses_the_learners_own_customer_and_makes_one_when_missing()
		{
			await FeesForEveryoneAsync();
			_contacts.Setup(c => c.SaveContactAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Contact c, CancellationToken _) => { c.ContactId = "new-" + c.FirstName; return c; });

			var run = await _service.RaiseInvoicesAsync(DepartmentId, SessionId, LmsClassInvoiceModes.PerLearner, "officer", "127.0.0.1", "test");

			run.InvoiceIds.Should().HaveCount(3);
			_invoices.Verify(i => i.CreateDraftInvoiceAsync(DepartmentId, "cy-customer", "officer", It.IsAny<string>(), It.IsAny<string>(), "USD", It.IsAny<CancellationToken>()), Times.Once);
			_contacts.Verify(c => c.SaveContactAsync(It.Is<Contact>(x => x.FirstName == "Ann" && x.Email == "a@x.org" && x.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
			_invoices.Verify(i => i.SaveBillingProfileAsync(It.Is<CustomerBillingProfile>(p => p.ContactId == "new-Ann" && p.BillingEmail == "a@x.org"), "officer", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_learners[0].BillingContactId.Should().Be("new-Ann", "the customer is linked for next time");
		}

		[Test]
		public async Task A_waived_or_invoiced_fee_cannot_be_changed_here()
		{
			await FeesForEveryoneAsync();
			var waived = await _service.SetFeeAsync(DepartmentId, 1, true, null, "officer");
			waived.Waived.Should().BeTrue();
			await FluentActions.Awaiting(() => _service.SetFeeAsync(DepartmentId, 2, false, "unknown-customer", "officer")).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_xl_customer_invalid");

			_fees.Single(f => f.LmsClassEnrollmentId == 2).InvoiceId = "inv-7";
			await FluentActions.Awaiting(() => _service.SetFeeAsync(DepartmentId, 2, true, null, "officer")).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_xl_fee_invoiced");
		}
	}
}
