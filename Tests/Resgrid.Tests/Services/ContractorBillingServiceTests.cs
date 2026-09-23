using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase C-M2 (contractor path): rate schedules (graph, cascade, clone,
	/// import/export, prefill), service contracts (status machine, compliance evaluation, expiry sweep, protected
	/// compliance documents), bids (numbering, discount cascade, rate snapshots, lifecycle events, send, conversion,
	/// expiry sweep) and the billing engine's invoice generation and packet.
	/// </summary>
	[TestFixture]
	public class ContractorBillingServiceTests
	{
		private const int DeptId = 7;
		private const string User = "manager";
		private static readonly DateTime Day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

		private List<RateSchedule> _schedules;
		private List<RateScheduleEntry> _entries;
		private List<RateScheduleEntryBand> _bands;
		private List<RatePremium> _premiums;
		private List<ServiceContract> _contracts;
		private List<ServiceContractDocumentRequirement> _requirements;
		private List<DepartmentComplianceDocument> _documents;
		private List<Bid> _bids;
		private List<BidLineItem> _lines;
		private List<CustomerBillingProfile> _profiles;
		private List<DomainEventEnvelope> _published;
		private List<AuditEvent> _audits;
		private Mock<IContactsService> _contactsService;
		private Mock<IDeploymentService> _deployments;
		private Mock<ICallsService> _calls;
		private Mock<ICalendarService> _calendar;
		private Mock<IEmailService> _email;
		private Mock<IDeploymentRepository> _deploymentRows;
		private Mock<IDeploymentAttachmentRepository> _attachments;
		private RateScheduleService _rates;
		private ServiceContractService _contractService;
		private BidsService _bidsService;

		[SetUp]
		public void SetUp()
		{
			_schedules = new List<RateSchedule>(); _entries = new List<RateScheduleEntry>(); _bands = new List<RateScheduleEntryBand>(); _premiums = new List<RatePremium>();
			_contracts = new List<ServiceContract>(); _requirements = new List<ServiceContractDocumentRequirement>(); _documents = new List<DepartmentComplianceDocument>();
			_bids = new List<Bid>(); _lines = new List<BidLineItem>(); _profiles = new List<CustomerBillingProfile>(); _published = new List<DomainEventEnvelope>(); _audits = new List<AuditEvent>();

			var schedules = Repo<IRateScheduleRepository, RateSchedule>(_schedules, s => s.RateScheduleId, (s, id) => s.RateScheduleId = id);
			schedules.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _schedules.FirstOrDefault(s => s.RateScheduleId == id));
			schedules.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<bool>())).ReturnsAsync((int _, bool inactive) => _schedules.Where(s => !s.IsDeleted && (inactive || s.IsActive)).ToList());
			var entries = Repo<IRateScheduleEntryRepository, RateScheduleEntry>(_entries, e => e.RateScheduleEntryId, (e, id) => e.RateScheduleEntryId = id);
			entries.Setup(r => r.GetByScheduleAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool inactive) => _entries.Where(e => e.RateScheduleId == id && !e.IsDeleted && (inactive || e.IsActive)).ToList());
			var bands = Repo<IRateScheduleEntryBandRepository, RateScheduleEntryBand>(_bands, b => b.RateScheduleEntryBandId, (b, id) => b.RateScheduleEntryBandId = id);
			bands.Setup(r => r.GetByScheduleAsync(It.IsAny<string>())).ReturnsAsync((string id) => _bands.Where(b => _entries.Any(e => e.RateScheduleId == id && e.RateScheduleEntryId == b.RateScheduleEntryId)).ToList());
			bands.Setup(r => r.GetByEntryAsync(It.IsAny<string>())).ReturnsAsync((string id) => _bands.Where(b => b.RateScheduleEntryId == id).ToList());
			bands.Setup(r => r.DeleteByEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, CancellationToken _) => { _bands.RemoveAll(b => b.RateScheduleEntryId == id); return true; });
			var premiums = Repo<IRatePremiumRepository, RatePremium>(_premiums, p => p.RatePremiumId, (p, id) => p.RatePremiumId = id);
			premiums.Setup(r => r.GetByScheduleAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool inactive) => _premiums.Where(p => p.RateScheduleId == id && !p.IsDeleted && (inactive || p.IsActive)).ToList());
			var contracts = Repo<IServiceContractRepository, ServiceContract>(_contracts, c => c.ServiceContractId, (c, id) => c.ServiceContractId = id);
			contracts.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _contracts.FirstOrDefault(c => c.ServiceContractId == id));
			contracts.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<int?>())).ReturnsAsync((int _, int? status) => _contracts.Where(c => !c.IsDeleted && (!status.HasValue || c.Status == status)).ToList());
			contracts.Setup(r => r.GetByContactIdAsync(DeptId, It.IsAny<string>())).ReturnsAsync((int _, string contactId) => _contracts.Where(c => c.ContactId == contactId && !c.IsDeleted).ToList());
			contracts.Setup(r => r.GetLapsedAsync(It.IsAny<DateTime>())).ReturnsAsync((DateTime asOf) => _contracts.Where(c => c.Status == (int)ServiceContractStatuses.Active && c.EndOn < asOf).ToList());
			contracts.Setup(r => r.GetEndingBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync((DateTime from, DateTime to) => _contracts.Where(c => c.Status == (int)ServiceContractStatuses.Active && c.EndOn >= from && c.EndOn <= to).ToList());
			var requirements = Repo<IServiceContractDocumentRequirementRepository, ServiceContractDocumentRequirement>(_requirements, r => r.ServiceContractDocumentRequirementId, (r, id) => r.ServiceContractDocumentRequirementId = id);
			requirements.Setup(r => r.GetByContractAsync(It.IsAny<string>())).ReturnsAsync((string id) => _requirements.Where(x => x.ServiceContractId == id).ToList());
			requirements.Setup(r => r.DeleteByContractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, CancellationToken _) => { _requirements.RemoveAll(x => x.ServiceContractId == id); return true; });
			var documents = new Mock<IDepartmentComplianceDocumentRepository>();
			documents.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentComplianceDocument>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentComplianceDocument d, CancellationToken _, bool __) => { if (d.DepartmentComplianceDocumentId == 0) d.DepartmentComplianceDocumentId = _documents.Count + 1; _documents.RemoveAll(x => x.DepartmentComplianceDocumentId == d.DepartmentComplianceDocumentId); _documents.Add(d); return d; });
			documents.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _documents.Where(d => !d.IsDeleted).Select(d => { var copy = Resgrid.Framework.ObjectCopier.CloneJson(d); copy.Data = null; return copy; }).ToList());
			documents.Setup(r => r.GetByIdWithDataAsync(It.IsAny<int>())).ReturnsAsync((int id) => { var d = _documents.FirstOrDefault(x => x.DepartmentComplianceDocumentId == id); return d == null ? null : Resgrid.Framework.ObjectCopier.CloneJson(d); });
			documents.Setup(r => r.GetExpiringAsync(It.IsAny<DateTime>())).ReturnsAsync((DateTime asOf) => _documents.Where(d => !d.IsDeleted && d.ExpiresOn.HasValue && d.ExpiresOn <= asOf.AddDays(d.AlertLeadDays)).ToList());
			var bids = Repo<IBidRepository, Bid>(_bids, b => b.BidId, (b, id) => b.BidId = id);
			bids.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _bids.FirstOrDefault(b => b.BidId == id));
			bids.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int _, int? status, int __, int ___) => _bids.Where(b => !b.IsDeleted && (!status.HasValue || b.Status == status)).ToList());
			bids.Setup(r => r.GetByContactIdAsync(DeptId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int _, string contactId, int __, int ___) => _bids.Where(b => b.ContactId == contactId && !b.IsDeleted).ToList());
			bids.Setup(r => r.GetByContractAsync(It.IsAny<string>())).ReturnsAsync((string contractId) => _bids.Where(b => b.ServiceContractId == contractId && !b.IsDeleted).ToList());
			bids.Setup(r => r.GetExpiryCandidatesAsync(It.IsAny<DateTime>())).ReturnsAsync((DateTime asOf) => _bids.Where(b => b.Status == (int)BidStatuses.Submitted && b.ValidUntil < asOf).ToList());
			var lines = Repo<IBidLineItemRepository, BidLineItem>(_lines, l => l.BidLineItemId, (l, id) => l.BidLineItemId = id);
			lines.Setup(r => r.GetByBidAsync(It.IsAny<string>())).ReturnsAsync((string id) => _lines.Where(l => l.BidId == id).ToList());
			lines.Setup(r => r.DeleteAsync(It.IsAny<BidLineItem>(), It.IsAny<CancellationToken>())).ReturnsAsync((BidLineItem l, CancellationToken _) => { _lines.RemoveAll(x => x.BidLineItemId == l.BidLineItemId); return true; });
			var sequence = new Mock<IBidNumberSequenceRepository>();
			var next = 100;
			sequence.Setup(s => s.GetNextNumberAsync(DeptId, It.IsAny<CancellationToken>())).ReturnsAsync(() => ++next);
			var profiles = new Mock<ICustomerBillingProfileRepository>();
			profiles.Setup(p => p.GetByContactIdAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string contactId, int _) => _profiles.FirstOrDefault(p => p.ContactId == contactId));
			profiles.Setup(p => p.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _profiles.FirstOrDefault(p => p.CustomerBillingProfileId == id));
			var identities = new Mock<IDepartmentBillingIdentityRepository>();
			identities.Setup(i => i.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentBillingIdentity { DepartmentId = DeptId, LegalBusinessName = "Test County Fire Ltd." });

			_contactsService = new Mock<IContactsService>();
			_contactsService.Setup(c => c.GetContactByIdAsync("customer")).ReturnsAsync(new Contact { ContactId = "customer", DepartmentId = DeptId, ContactType = 1, CompanyName = "Province Wildfire", Email = "ap@province.example" });
			_contactsService.Setup(c => c.GetContactByIdAsync("other")).ReturnsAsync(new Contact { ContactId = "other", DepartmentId = DeptId, ContactType = 1, CompanyName = "Other Agency" });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Test County Fire", TimeZone = "Pacific Standard Time" });
			departments.Setup(d => d.GetAllAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			var outbox = new Mock<IDomainEventOutboxService>();
			outbox.Setup(o => o.EnqueueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, DomainEventEnvelope, CancellationToken>((_, __, e, ___) => _published.Add(e)).ReturnsAsync(new DomainEventOutboxEntry());
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));
			_deploymentRows = new Mock<IDeploymentRepository>();
			_attachments = new Mock<IDeploymentAttachmentRepository>();
			_attachments.Setup(a => a.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync(new List<DeploymentAttachment>());
			_deployments = new Mock<IDeploymentService>();
			_calls = new Mock<ICallsService>();
			_calendar = new Mock<ICalendarService>();
			_email = new Mock<IEmailService>();
			_email.Setup(e => e.SendInvoiceAsync(It.IsAny<EmailNotification>(), DeptId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns<string>(html => System.Text.Encoding.UTF8.GetBytes(html));

			_rates = new RateScheduleService(schedules.Object, entries.Object, bands.Object, premiums.Object, contracts.Object, profiles.Object, events.Object, null);
			_contractService = new ServiceContractService(contracts.Object, requirements.Object, documents.Object, _deploymentRows.Object, _attachments.Object, profiles.Object, _contactsService.Object, departments.Object, outbox.Object, events.Object, null);
			_bidsService = new BidsService(bids.Object, lines.Object, sequence.Object, contracts.Object, profiles.Object, identities.Object, _rates, _deployments.Object, _contactsService.Object, departments.Object, _calls.Object, _calendar.Object, _email.Object, pdf.Object, outbox.Object, events.Object, null);
		}

		private static Mock<TRepo> Repo<TRepo, T>(List<T> store, Func<T, string> id, Action<T, string> setId) where TRepo : class, IRepository<T> where T : class, IEntity
		{
			var mock = new Mock<TRepo>();
			mock.Setup(r => r.SaveOrUpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((T entity, CancellationToken _, bool __) => { if (string.IsNullOrWhiteSpace(id(entity))) setId(entity, Guid.NewGuid().ToString()); store.RemoveAll(x => id(x) == id(entity)); store.Add(entity); return entity; });
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object key) => store.FirstOrDefault(x => id(x) == (string)key));
			return mock;
		}

		private async Task<RateSchedule> SeedScheduleAsync()
		{
			var schedule = await _rates.SaveScheduleAsync(new RateSchedule { DepartmentId = DeptId, Name = "2026 wildfire", Currency = "CAD" }, User, null, null);
			var bands = _rates.PrefillHourlyBands(40, 20, 1.5m, 8, 2m, 12);
			await _rates.SaveEntryAsync(new RateScheduleEntry { RateScheduleId = schedule.RateScheduleId, DepartmentId = DeptId, EntryType = (int)RateEntryTypes.PersonnelCertification, Name = "FFT2", CertificationCode = "FFT2", BillingBasis = (int)BillingBases.Hourly, Bands = bands }, User, null, null);
			await _rates.SaveEntryAsync(new RateScheduleEntry { RateScheduleId = schedule.RateScheduleId, DepartmentId = DeptId, EntryType = (int)RateEntryTypes.Crew, Name = "Type 6 (3)", GroupKey = "t6", CrewSize = 3, BillingBasis = (int)BillingBases.Hourly, Bands = new List<RateScheduleEntryBand> { new RateScheduleEntryBand { BandType = (int)RateBandTypes.Deployment, Rate = 300 } } }, User, null, null);
			await _rates.SavePremiumAsync(new RatePremium { RateScheduleId = schedule.RateScheduleId, DepartmentId = DeptId, Name = "Night", DeploymentAdder = 5, Overtime1Adder = 7.5m }, User, null, null);
			return await _rates.GetScheduleByIdAsync(schedule.RateScheduleId, DeptId);
		}

		#region Rate schedules

		[Test]
		public async Task Schedule_graph_saves_bands_prefilled_from_multipliers_and_clones_with_new_ids()
		{
			var schedule = await SeedScheduleAsync();
			schedule.Entries.Should().HaveCount(2);
			var fft2 = schedule.Entries.Single(e => e.Name == "FFT2");
			fft2.Bands.Select(b => (b.BandType, b.Rate)).Should().BeEquivalentTo(new[] { ((int)RateBandTypes.Standby, 20m), ((int)RateBandTypes.Deployment, 40m), ((int)RateBandTypes.Overtime1, 60m), ((int)RateBandTypes.Overtime2, 80m) });
			fft2.Band(RateBandTypes.Overtime2).ThresholdStartHours.Should().Be(12);
			schedule.Premiums.Should().ContainSingle(p => p.Name == "Night");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.RateScheduleCreated).And.Contain(a => a.Type == AuditLogTypes.RateScheduleEntryChanged).And.Contain(a => a.Type == AuditLogTypes.RatePremiumChanged);

			var clone = await _rates.CloneScheduleAsync(schedule.RateScheduleId, DeptId, "2027 wildfire", User, null, null);
			clone.RateScheduleId.Should().NotBe(schedule.RateScheduleId);
			clone.Entries.Should().HaveCount(2);
			clone.Entries.Select(e => e.RateScheduleEntryId).Should().NotIntersectWith(schedule.Entries.Select(e => e.RateScheduleEntryId));
			clone.Entries.Single(e => e.Name == "FFT2").Bands.Should().HaveCount(4);
			clone.Premiums.Should().ContainSingle();
		}

		[Test]
		public async Task Entry_validation_rejects_crew_without_size_and_duplicate_bands()
		{
			var schedule = await _rates.SaveScheduleAsync(new RateSchedule { DepartmentId = DeptId, Name = "S" }, User, null, null);
			(await FluentActions.Awaiting(() => _rates.SaveEntryAsync(new RateScheduleEntry { RateScheduleId = schedule.RateScheduleId, DepartmentId = DeptId, EntryType = (int)RateEntryTypes.Crew, Name = "Crew" }, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("rateschedules_crew_size_required");
			var dup = new List<RateScheduleEntryBand> { new RateScheduleEntryBand { BandType = 1, Rate = 1 }, new RateScheduleEntryBand { BandType = 1, Rate = 2 } };
			(await FluentActions.Awaiting(() => _rates.SaveEntryAsync(new RateScheduleEntry { RateScheduleId = schedule.RateScheduleId, DepartmentId = DeptId, EntryType = 0, Name = "X", Bands = dup }, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("rateschedules_band_duplicate");
		}

		[Test]
		public async Task Effective_schedule_cascades_contract_then_profile_then_department_default()
		{
			var defaultSchedule = await _rates.SaveScheduleAsync(new RateSchedule { DepartmentId = DeptId, Name = "A department default" }, User, null, null);
			var profileSchedule = await _rates.SaveScheduleAsync(new RateSchedule { DepartmentId = DeptId, Name = "Profile" }, User, null, null);
			var contractSchedule = await _rates.SaveScheduleAsync(new RateSchedule { DepartmentId = DeptId, Name = "Contract" }, User, null, null);
			(await _rates.GetEffectiveScheduleForContactAsync("customer", DeptId)).RateScheduleId.Should().Be(defaultSchedule.RateScheduleId);

			_profiles.Add(new CustomerBillingProfile { CustomerBillingProfileId = "prof", ContactId = "customer", DepartmentId = DeptId, Active = true, DefaultRateScheduleId = profileSchedule.RateScheduleId });
			(await _rates.GetEffectiveScheduleForContactAsync("customer", DeptId)).RateScheduleId.Should().Be(profileSchedule.RateScheduleId);

			var contract = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", StartOn = Day, RateScheduleId = contractSchedule.RateScheduleId }, User, null, null);
			(await _rates.GetEffectiveScheduleForContactAsync("customer", DeptId, contract.ServiceContractId)).RateScheduleId.Should().Be(contractSchedule.RateScheduleId);

			// An expired contract schedule falls through to the profile.
			contractSchedule.ExpiresOn = Day.AddYears(-1);
			await _rates.SaveScheduleAsync(contractSchedule, User, null, null);
			(await _rates.GetEffectiveScheduleForContactAsync("customer", DeptId, contract.ServiceContractId)).RateScheduleId.Should().Be(profileSchedule.RateScheduleId);
		}

		[Test]
		public async Task Export_import_round_trips_the_graph_without_ids_and_delete_refuses_a_schedule_in_use()
		{
			var schedule = await SeedScheduleAsync();
			var json = await _rates.ExportScheduleJsonAsync(schedule.RateScheduleId, DeptId);
			json.Should().NotContain(schedule.RateScheduleId).And.Contain("\"GroupKey\": \"t6\"");
			var imported = await _rates.ImportScheduleJsonAsync(DeptId, json, User, null, null);
			imported.RateScheduleId.Should().NotBe(schedule.RateScheduleId);
			imported.Currency.Should().Be("CAD");
			imported.Entries.Should().HaveCount(2);
			imported.Entries.Single(e => e.Name == "FFT2").Band(RateBandTypes.Overtime1).Rate.Should().Be(60);
			imported.Premiums.Single().Overtime1Adder.Should().Be(7.5m);
			(await FluentActions.Awaiting(() => _rates.ImportScheduleJsonAsync(DeptId, "{ nope", User, null, null)).Should().ThrowAsync<Resgrid.Framework.JsonInputException>()).Which.Message.Should().Contain("line 1");

			await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", StartOn = Day, RateScheduleId = schedule.RateScheduleId }, User, null, null);
			(await FluentActions.Awaiting(() => _rates.DeleteScheduleAsync(schedule.RateScheduleId, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("rateschedules_in_use");
		}

		#endregion

		[TestCase("{\"Name\":\"test\",\"FormatVersion\":2}", "FormatVersion")]
		[TestCase("{\"Name\":\"test\",\"Currency\":\"dollars\"}", "Currency")]
		[TestCase("{\"Name\":\"test\",\"EffectiveOn\":\"2026-09-22\",\"ExpiresOn\":\"2026-09-21\"}", "ExpiresOn")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[{\"Name\":\"valid\"},{\"Name\":\"bad\",\"EntryType\":1}]}", "Entries[1].CrewSize")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[{\"Name\":\"bad\",\"Bands\":[{\"Rate\":-1}]}]}", "Bands[0].Rate")]
		[TestCase("{\"Name\":\"test\",\"Premiums\":[{\"Name\":\"bad\",\"StandbyAdder\":-1}]}", "Premiums[0].StandbyAdder")]
		[TestCase("{\"Name\":\"test\",\"Premiums\":[{}]}", "Premiums[0].Name")]
		[TestCase("{\"Name\":\"test\",\"Policy\":{\"MealEligibility\":[{\"MealCode\":\"breakfast\",\"StartsBeforeMinutes\":1440}]}}", "StartsBeforeMinutes")]
		public async Task Invalid_imports_report_the_field_before_writing_any_rows(string json, string field)
		{
			(await FluentActions.Awaiting(() => _rates.ImportScheduleJsonAsync(DeptId, json, User, null, null)).Should().ThrowAsync<Resgrid.Framework.JsonInputException>()).Which.Message.Should().Contain(field);
			_schedules.Should().BeEmpty();
			_entries.Should().BeEmpty();
			_bands.Should().BeEmpty();
			_premiums.Should().BeEmpty();
		}

		#region Contracts and compliance

		[Test]
		public async Task Contract_status_machine_publishes_79_and_refuses_bad_transitions()
		{
			var contract = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", ContractNumber = "WFS-1", StartOn = Day, EndOn = Day.AddMonths(6), TermsNetDays = 45, InvoiceSubmissionEmail = "ap@province.example" }, User, null, null);
			contract.Status.Should().Be((int)ServiceContractStatuses.Draft);
			(await FluentActions.Awaiting(() => _contractService.SetContractStatusAsync(contract.ServiceContractId, DeptId, ServiceContractStatuses.Suspended, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("contracts_status_transition_invalid");

			var active = await _contractService.SetContractStatusAsync(contract.ServiceContractId, DeptId, ServiceContractStatuses.Active, User, null, null);
			active.Status.Should().Be((int)ServiceContractStatuses.Active);
			var evt = _published.Single(e => e.Trigger == WorkflowTriggerEventType.ContractStatusChanged);
			evt.AggregateType.Should().Be("ServiceContract");
			var payload = JObject.FromObject(evt.Payload);
			payload["OldStatus"].Value<int>().Should().Be(0);
			payload["ContactName"].Value<string>().Should().Be("Province Wildfire");
			payload["ContractNumber"].Value<string>().Should().Be("WFS-1");
			(await FluentActions.Awaiting(() => _contractService.DeleteContractAsync(contract.ServiceContractId, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("contracts_active");
			(await FluentActions.Awaiting(() => _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Bad", StartOn = Day, EndOn = Day.AddDays(-1) }, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("contracts_dates_invalid");
		}

		[Test]
		public async Task Compliance_evaluation_uses_current_department_documents_and_deployment_attachments()
		{
			var contract = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", StartOn = Day }, User, null, null);
			await _contractService.SaveRequirementsAsync(contract.ServiceContractId, DeptId, new List<ServiceContractDocumentRequirement>
			{
				new ServiceContractDocumentRequirement { Name = "Insurance", Stage = (int)DocumentRequirementStages.InvoiceSubmission, ComplianceDocumentType = (int)ComplianceDocumentTypes.InsuranceCertificate, IsMandatory = true },
				new ServiceContractDocumentRequirement { Name = "Signed service request", Stage = (int)DocumentRequirementStages.DeploymentStart, IsMandatory = true },
				new ServiceContractDocumentRequirement { Name = "Bond", Stage = (int)DocumentRequirementStages.BidSubmission, ComplianceDocumentType = (int)ComplianceDocumentTypes.Bond, IsMandatory = false }
			}, User, null, null);
			await _contractService.SaveComplianceDocumentAsync(new DepartmentComplianceDocument { DepartmentId = DeptId, DocumentType = (int)ComplianceDocumentTypes.InsuranceCertificate, Name = "COI 2026", DocumentNumber = "POL-9", ExpiresOn = Day.AddMonths(3) }, new byte[] { 1, 2, 3 }, "coi.pdf", "application/pdf", User, null, null);
			await _contractService.SaveComplianceDocumentAsync(new DepartmentComplianceDocument { DepartmentId = DeptId, DocumentType = (int)ComplianceDocumentTypes.Bond, Name = "Old bond", ExpiresOn = Day.AddYears(-1) }, null, null, null, User, null, null);

			var forContract = await _contractService.GetContractComplianceForContractAsync(contract.ServiceContractId, DeptId);
			forContract.Items.Should().HaveCount(3);
			forContract.Items.Single(i => i.Name == "Insurance").Satisfied.Should().BeTrue();
			forContract.Items.Single(i => i.Name == "Insurance").SatisfiedBy.Should().Be("COI 2026");
			forContract.Items.Single(i => i.Name == "Bond").Satisfied.Should().BeFalse("an expired document does not satisfy");
			forContract.Items.Single(i => i.Name == "Signed service request").Satisfied.Should().BeFalse();
			forContract.AllMandatorySatisfied.Should().BeFalse();

			_deploymentRows.Setup(d => d.GetByIdForDepartmentAsync("dep-1", DeptId)).ReturnsAsync(new Deployment { DeploymentId = "dep-1", DepartmentId = DeptId, ServiceContractId = contract.ServiceContractId });
			_attachments.Setup(a => a.GetByDeploymentAsync("dep-1")).ReturnsAsync(new List<DeploymentAttachment> { new DeploymentAttachment { DeploymentAttachmentId = 5, AttachmentType = (int)DeploymentAttachmentTypes.SignedServiceRequest, Name = "Signed request" } });
			var forDeployment = await _contractService.GetContractComplianceAsync("dep-1", DeptId);
			forDeployment.Items.Single(i => i.Name == "Signed service request").DeploymentAttachmentId.Should().Be(5);
			forDeployment.AllMandatorySatisfied.Should().BeTrue();
			(await _contractService.GetComplianceDocumentAsync(1, DeptId, includeData: true)).Data.Should().Equal(1, 2, 3);
			(await _contractService.GetComplianceDocumentsAsync(DeptId)).Should().OnlyContain(d => d.Data == null, "list reads never carry bytes");
		}

		[Test]
		public async Task Expiry_sweep_expires_lapsed_contracts_announces_expiring_ones_once_a_day_and_is_department_gated()
		{
			var lapsed = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Lapsed", StartOn = Day.AddYears(-1), EndOn = Day.AddDays(-1), Status = (int)ServiceContractStatuses.Active }, User, null, null);
			var ending = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Ending", StartOn = Day.AddYears(-1), EndOn = Day.AddDays(10), Status = (int)ServiceContractStatuses.Active }, User, null, null);
			_published.Clear();
			var sweepDay = Day.AddYears(1).AddDays(new Random().Next(0, 300));
			lapsed.EndOn = sweepDay.AddDays(-1); ending.EndOn = sweepDay.AddDays(10);
			await _contractService.RunExpirySweepAsync(sweepDay, null);
			_contracts.Single(c => c.ServiceContractId == lapsed.ServiceContractId).Status.Should().Be((int)ServiceContractStatuses.Expired);
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ContractStatusChanged && e.AggregateId == lapsed.ServiceContractId);
			var expiring = _published.Single(e => e.Trigger == WorkflowTriggerEventType.ContractExpiring);
			expiring.AggregateId.Should().Be(ending.ServiceContractId);
			JObject.FromObject(expiring.Payload)["DaysUntilEnd"].Value<int>().Should().Be(10);

			_published.Clear();
			await _contractService.RunExpirySweepAsync(sweepDay.AddHours(2), null);
			_published.Should().NotContain(e => e.Trigger == WorkflowTriggerEventType.ContractExpiring, "one announcement per contract per day");

			_published.Clear();
			ending.EndOn = sweepDay.AddDays(30).AddDays(5);
			(await _contractService.RunExpirySweepAsync(sweepDay.AddDays(30), _ => Task.FromResult(false))).Should().Be(0, "departments without the entitlement are skipped");
			_published.Should().BeEmpty();
		}

		#endregion

		#region Bids

		private async Task<(Bid Bid, RateSchedule Schedule)> SeedBidAsync(decimal? contractDiscount = 10, decimal? profileDiscount = 5)
		{
			var schedule = await SeedScheduleAsync();
			_profiles.Add(new CustomerBillingProfile { CustomerBillingProfileId = "prof", ContactId = "customer", DepartmentId = DeptId, Active = true, DefaultDiscountPercent = profileDiscount, TaxRate = 5 });
			var contract = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", StartOn = Day, RateScheduleId = schedule.RateScheduleId, DiscountPercent = contractDiscount, TermsNetDays = 45, Status = (int)ServiceContractStatuses.Active }, User, null, null);
			var bid = await _bidsService.CreateDraftBidAsync(DeptId, "customer", contract.ServiceContractId, "Type 6 engine, Ridge Fire", User, null, null);
			return (bid, schedule);
		}

		[Test]
		public async Task Draft_bid_takes_the_next_number_the_contract_schedule_and_the_discount_cascade_and_publishes_74()
		{
			var (bid, schedule) = await SeedBidAsync();
			bid.BidNumber.Should().Be(101);
			bid.RateScheduleId.Should().Be(schedule.RateScheduleId);
			bid.DiscountPercent.Should().Be(10, "contract beats profile");
			bid.CustomerBillingProfileId.Should().Be("prof");
			bid.ValidUntil.Should().NotBeNull();
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.BidCreated && e.AggregateType == "Bid");
			JObject.FromObject(_published.Single(e => e.Trigger == WorkflowTriggerEventType.BidCreated).Payload)["Currency"].Value<string>().Should().Be("CAD");

			var noContract = await _bidsService.CreateDraftBidAsync(DeptId, "customer", null, null, User, null, null);
			noContract.DiscountPercent.Should().Be(5, "profile default when no contract");
			noContract.Title.Should().Be("Bid for Province Wildfire");
			(await FluentActions.Awaiting(() => _bidsService.CreateDraftBidAsync(DeptId, "other", bid.ServiceContractId, null, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_contract_contact_mismatch");
		}

		[Test]
		public async Task Lines_snapshot_the_entry_rate_plus_premiums_and_estimates_follow_the_invoice_rules()
		{
			var (bid, schedule) = await SeedBidAsync();
			var fft2 = schedule.Entries.Single(e => e.Name == "FFT2");
			var night = schedule.Premiums.Single();
			var saved = await _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem>
			{
				new BidLineItem { RateScheduleEntryId = fft2.RateScheduleEntryId, LineType = (int)BidLineTypes.PersonnelCertification, Description = "FFT2 × 3", Quantity = 3, EstimatedHoursPerDay = 10, EstimatedDays = 5, PremiumIdsJson = "[\"" + night.RatePremiumId + "\"]" },
				new BidLineItem { LineType = (int)BidLineTypes.FreeForm, Description = "Mobilization", Quantity = 1, UnitRate = 500, Taxable = false }
			}, User, null, null);
			var personnel = saved.LineItems.Single(l => l.LineType == (int)BidLineTypes.PersonnelCertification);
			personnel.UnitRate.Should().Be(45, "deployment band 40 + night adder 5");
			personnel.EstimatedAmount.Should().Be(3 * 45 * 10 * 5);
			saved.EstimatedSubTotal.Should().Be(6750 + 500);
			saved.EstimatedDiscountAmount.Should().Be(725);
			// 5 % tax on the taxable base after the pro-rata discount: (6750 − 725 × 6750/7250) × 5 %.
			saved.EstimatedTaxAmount.Should().Be(Math.Round((6750m - Math.Round(725m * 6750m / 7250m, 2)) * 0.05m, 2));
			saved.EstimatedTotal.Should().Be(saved.EstimatedSubTotal - saved.EstimatedDiscountAmount + saved.EstimatedTaxAmount);

			// Re-saving with one line keeps its id and removes the other.
			var again = await _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem> { new BidLineItem { BidLineItemId = personnel.BidLineItemId, RateScheduleEntryId = fft2.RateScheduleEntryId, Description = "FFT2 × 3", Quantity = 3, UnitRate = 45, EstimatedHoursPerDay = 8, EstimatedDays = 5 } }, User, null, null);
			again.LineItems.Should().ContainSingle().Which.BidLineItemId.Should().Be(personnel.BidLineItemId);
			(await FluentActions.Awaiting(() => _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem> { new BidLineItem { Description = "", Quantity = 1 } }, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_line_invalid");
		}

		[Test]
		public async Task Lifecycle_sends_the_pdf_publishes_75_to_78_and_locks_after_acceptance()
		{
			var (bid, schedule) = await SeedBidAsync();
			(await FluentActions.Awaiting(() => _bidsService.SubmitBidAsync(bid.BidId, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_no_lines");
			await _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem> { new BidLineItem { Description = "Engine", Quantity = 1, UnitRate = 1000, EstimatedDays = 3 } }, User, null, null);
			(await FluentActions.Awaiting(() => _bidsService.AcceptBidAsync(bid.BidId, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_status_transition_invalid");

			EmailNotification sent = null;
			_email.Setup(e => e.SendInvoiceAsync(It.IsAny<EmailNotification>(), DeptId, null, null, "Bid #101", It.IsAny<string>())).Callback<EmailNotification, int, string, string, string, string>((n, _, __, ___, ____, _____) => sent = n).ReturnsAsync(true);
			var submitted = await _bidsService.SendBidAsync(bid.BidId, DeptId, null, User, null, null);
			submitted.Status.Should().Be((int)BidStatuses.Submitted);
			submitted.SentToEmail.Should().Be("ap@province.example", "the contact e-mail is the default recipient");
			sent.AttachmentName.Should().Be("bid-101.pdf");
			System.Text.Encoding.UTF8.GetString(sent.AttachmentData).Should().Contain("Bid #101").And.Contain("Test County Fire Ltd.").And.Contain("Engine");
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.BidSent);

			var accepted = await _bidsService.AcceptBidAsync(bid.BidId, DeptId, User, null, null);
			accepted.AcceptedOn.Should().NotBeNull();
			accepted.IsEditable.Should().BeFalse();
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.BidAccepted);
			(await FluentActions.Awaiting(() => _bidsService.SaveBidAsync(new Bid { BidId = bid.BidId, DepartmentId = DeptId, Title = "x" }, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_locked");

			var second = await _bidsService.CreateDraftBidAsync(DeptId, "customer", null, "Second", User, null, null);
			await _bidsService.SaveBidLineItemsAsync(second.BidId, DeptId, new List<BidLineItem> { new BidLineItem { Description = "Line", Quantity = 1, UnitRate = 1 } }, User, null, null);
			await _bidsService.SubmitBidAsync(second.BidId, DeptId, User, null, null);
			var declined = await _bidsService.DeclineBidAsync(second.BidId, DeptId, "Too expensive", User, null, null);
			declined.DeclineReason.Should().Be("Too expensive");
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.BidDeclined && e.AggregateId == second.BidId);
			_audits.Select(a => a.Type).Should().Contain(new[] { AuditLogTypes.BidCreated, AuditLogTypes.BidSent, AuditLogTypes.BidAccepted, AuditLogTypes.BidDeclined });
		}

		[Test]
		public async Task Expiry_sweep_expires_submitted_bids_past_valid_until_and_publishes_78()
		{
			var (bid, _) = await SeedBidAsync();
			await _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem> { new BidLineItem { Description = "Line", Quantity = 1, UnitRate = 1 } }, User, null, null);
			await _bidsService.SubmitBidAsync(bid.BidId, DeptId, User, null, null);
			_bids.Single().ValidUntil = Day.AddDays(-1);
			(await _bidsService.RunExpirySweepAsync(Day, _ => Task.FromResult(false))).Should().Be(0);
			(await _bidsService.RunExpirySweepAsync(Day, null)).Should().Be(1);
			_bids.Single().Status.Should().Be((int)BidStatuses.Expired);
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.BidExpired);
			(await _bidsService.SubmitBidAsync(bid.BidId, DeptId, User, null, null)).Status.Should().Be((int)BidStatuses.Submitted, "an expired bid can be re-submitted");
		}

		[Test]
		public async Task Conversion_creates_the_call_and_deployment_with_rate_snapshots_and_stamps_the_bid()
		{
			var (bid, schedule) = await SeedBidAsync();
			var crew = schedule.Entries.Single(e => e.EntryType == (int)RateEntryTypes.Crew);
			var fft2 = schedule.Entries.Single(e => e.Name == "FFT2");
			await _bidsService.SaveBidLineItemsAsync(bid.BidId, DeptId, new List<BidLineItem> { new BidLineItem { RateScheduleEntryId = crew.RateScheduleEntryId, LineType = (int)BidLineTypes.Crew, Description = "Type 6", Quantity = 1, CrewSize = 3 } }, User, null, null);
			await _bidsService.SubmitBidAsync(bid.BidId, DeptId, User, null, null);
			var request = new BidConversionRequest { BidId = bid.BidId, CallName = "Ridge Fire", CallPriority = 2, StartOn = Day, EndOn = Day.AddDays(3), Address = "Ridge Rd", Units = new List<BidConversionUnit> { new BidConversionUnit { UnitId = 1, RateScheduleEntryId = crew.RateScheduleEntryId, CallSign = "E6", Seats = new List<BidConversionSeat> { new BidConversionSeat { UserId = "alice", RateScheduleEntryId = fft2.RateScheduleEntryId, PremiumIds = new List<string> { schedule.Premiums[0].RatePremiumId } } } } } };
			(await FluentActions.Awaiting(() => _bidsService.ConvertBidToDeploymentAsync(request, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_not_accepted");
			await _bidsService.AcceptBidAsync(bid.BidId, DeptId, User, null, null);

			Call savedCall = null;
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { c.CallId = 42; savedCall = c; return c; });
			Deployment savedDeployment = null;
			_deployments.Setup(d => d.SaveDeploymentAsync(It.IsAny<Deployment>(), User, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((Deployment d, string _, string __, string ___, CancellationToken ____) => { d.DeploymentId ??= "dep-1"; savedDeployment = d; return d; });
			_deployments.Setup(d => d.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(() => savedDeployment);
			DeploymentPersonnelInput seated = null;
			_deployments.Setup(d => d.AddUnitAsync("dep-1", DeptId, 1, "E6", null, User, null, null, It.IsAny<CancellationToken>(), crew.RateScheduleEntryId)).ReturnsAsync(new DeploymentRosterResult { Unit = new DeploymentUnit { DeploymentUnitId = "du-1", UnitId = 1 } });
			_deployments.Setup(d => d.AddPersonnelAsync("dep-1", DeptId, It.IsAny<DeploymentPersonnelInput>(), User, null, null, It.IsAny<CancellationToken>())).Callback<string, int, DeploymentPersonnelInput, string, string, string, CancellationToken>((_, __, input, ___, ____, _____, ______) => seated = input).ReturnsAsync(new DeploymentRosterResult { Personnel = new DeploymentPersonnel { DeploymentPersonnelId = "dp-1" }, Warnings = new List<DeploymentRosterWarning> { new DeploymentRosterWarning { Code = DeploymentRosterWarning.CertificationExpiring, SubjectId = "alice" } } });
			_calendar.Setup(c => c.AddNewCalendarItemAsync(It.IsAny<CalendarItem>(), "Pacific Standard Time", It.IsAny<CancellationToken>())).ReturnsAsync((CalendarItem item, string _, CancellationToken __) => { item.CalendarItemId = 9; return item; });

			var result = await _bidsService.ConvertBidToDeploymentAsync(request, DeptId, User, null, null);
			result.CallId.Should().Be(42);
			savedCall.Contacts.Should().ContainSingle(c => c.ContactId == "customer");
			savedCall.ExternalIdentifier.Should().Be("bid:101");
			savedDeployment.BidId.Should().Be(bid.BidId);
			savedDeployment.FinanceMode.Should().Be((int)DeploymentFinanceModes.Billable);
			savedDeployment.RateScheduleId.Should().Be(schedule.RateScheduleId);
			savedDeployment.DiscountPercent.Should().Be(10);
			savedDeployment.CalendarItemId.Should().Be(9);
			seated.RateScheduleEntryId.Should().Be(fft2.RateScheduleEntryId);
			seated.PremiumIds.Should().ContainSingle();
			seated.DeploymentUnitId.Should().Be("du-1");
			result.Warnings.Should().ContainSingle(w => w.Code == DeploymentRosterWarning.CertificationExpiring);
			result.Bid.ConvertedCallId.Should().Be(42);
			result.Bid.ConvertedDeploymentId.Should().Be("dep-1");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.BidConverted);
			(await FluentActions.Awaiting(() => _bidsService.ConvertBidToDeploymentAsync(request, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("bids_already_converted");
		}

		#endregion

		#region Engine

		[Test]
		public async Task Engine_generates_a_draft_invoice_with_dtr_provenance_and_marks_the_reports_billed()
		{
			var schedule = await SeedScheduleAsync();
			var fft2 = schedule.Entries.Single(e => e.Name == "FFT2");
			var deployment = new Deployment { DeploymentId = "dep-1", DepartmentId = DeptId, FinanceMode = (int)DeploymentFinanceModes.Billable, ContactId = "customer", RateScheduleId = schedule.RateScheduleId, Name = "Ridge Fire", CallId = 42, DiscountPercent = 10, Personnel = new List<DeploymentPersonnel> { new DeploymentPersonnel { DeploymentPersonnelId = "dp-1", UserId = "alice", RateScheduleEntryId = fft2.RateScheduleEntryId, DisplayName = "Alice Smith" } } };
			_deployments.Setup(d => d.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(deployment);
			var timeTracking = new Mock<ITimeTrackingService>();
			timeTracking.Setup(t => t.GetUnbilledApprovedReportsAsync(DeptId, "dep-1")).ReturnsAsync(new List<DeploymentTimeReport> { new DeploymentTimeReport { DeploymentTimeReportId = "r1", DeploymentId = "dep-1", DepartmentId = DeptId, ReportNumber = 3, ReportDate = Day, Status = (int)DeploymentTimeReportStatuses.Approved } });
			timeTracking.Setup(t => t.GetExpensesAsync("dep-1", DeptId)).ReturnsAsync(new List<DeploymentExpense>());
			var entries = new Mock<IDeploymentTimeEntryRepository>();
			entries.Setup(e => e.GetByDeploymentAsync("dep-1")).ReturnsAsync(new List<DeploymentTimeEntry> { new DeploymentTimeEntry { DeploymentTimeEntryId = "e1", DeploymentTimeReportId = "r1", SubjectType = 0, DeploymentPersonnelId = "dp-1", EntryType = 0, StartTime = Day.AddHours(6), EndTime = Day.AddHours(16) } });
			var invoicing = new Mock<IInvoicingService>();
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = DeptId, InvoiceNumber = 500, ContactId = "customer", Currency = "CAD", DiscountPercent = 5, Status = 0, LineItems = new List<InvoiceLineItem>() };
			invoicing.Setup(i => i.CreateDraftInvoiceAsync(DeptId, "customer", User, null, null, "CAD", It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
			invoicing.Setup(i => i.LinkInvoiceToDeploymentAsync("inv-1", DeptId, "dep-1", null, null, User, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
			invoicing.Setup(i => i.SaveInvoiceAsync(It.IsAny<Invoice>(), User, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((Invoice i, string _, string __, string ___, CancellationToken ____) => i);
			List<InvoiceLineItem> savedLines = null;
			invoicing.Setup(i => i.SaveInvoiceLineItemsAsync("inv-1", DeptId, It.IsAny<List<InvoiceLineItem>>(), User, null, null, It.IsAny<CancellationToken>())).Callback<string, int, List<InvoiceLineItem>, string, string, string, CancellationToken>((_, __, l, ___, ____, _____, ______) => savedLines = l).ReturnsAsync(invoice);
			invoicing.Setup(i => i.GetInvoiceByIdAsync("inv-1", DeptId)).ReturnsAsync(invoice);
			var events = new Mock<IEventAggregator>();
			var engine = new ContractorBillingEngine(_deployments.Object, timeTracking.Object, entries.Object, _rates, _contractService, invoicing.Object, new Mock<IUnitsService>().Object, new Mock<IUserProfileService>().Object, events.Object, null);

			var preview = await engine.CalculateDeploymentChargesAsync("dep-1", DeptId);
			preview.Lines.Should().ContainSingle(l => l.Kind == ContractorChargeKinds.Hourly);
			preview.SubTotal.Should().Be(8 * 40 + 2 * 60);
			preview.DiscountPercent.Should().Be(10);

			var generated = await engine.GenerateInvoiceFromDeploymentAsync("dep-1", DeptId, null, User, null, null);
			generated.InvoiceId.Should().Be("inv-1");
			invoice.DiscountPercent.Should().Be(10, "the deployment discount is snapshotted over the profile default");
			savedLines.Should().ContainSingle();
			savedLines[0].DeploymentTimeReportId.Should().Be("r1");
			savedLines[0].CallId.Should().Be(42);
			savedLines[0].Description.Should().Contain("DTR #3").And.Contain("Alice Smith");
			timeTracking.Verify(t => t.MarkTimeReportsBilledAsync(It.Is<IEnumerable<string>>(ids => ids.Single() == "r1"), DeptId, "inv-1", User, null, null, It.IsAny<CancellationToken>()), Times.Once);
			events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.DeploymentInvoiceGenerated)), Times.Once);

			timeTracking.Setup(t => t.GetUnbilledApprovedReportsAsync(DeptId, "dep-1")).ReturnsAsync(new List<DeploymentTimeReport>());
			(await FluentActions.Awaiting(() => engine.GenerateInvoiceFromDeploymentAsync("dep-1", DeptId, null, User, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("contractor_no_charges");
		}

		[Test]
		public async Task Packet_bundles_the_invoice_pdf_dtr_pdfs_receipts_and_required_compliance_documents()
		{
			var contract = await _contractService.SaveContractAsync(new ServiceContract { DepartmentId = DeptId, ContactId = "customer", Name = "Standing", StartOn = Day, InvoiceSubmissionEmail = "ap@province.example" }, User, null, null);
			await _contractService.SaveRequirementsAsync(contract.ServiceContractId, DeptId, new List<ServiceContractDocumentRequirement> { new ServiceContractDocumentRequirement { Name = "Insurance", Stage = (int)DocumentRequirementStages.InvoiceSubmission, ComplianceDocumentType = (int)ComplianceDocumentTypes.InsuranceCertificate }, new ServiceContractDocumentRequirement { Name = "Bond", Stage = (int)DocumentRequirementStages.InvoiceSubmission, ComplianceDocumentType = (int)ComplianceDocumentTypes.Bond } }, User, null, null);
			await _contractService.SaveComplianceDocumentAsync(new DepartmentComplianceDocument { DepartmentId = DeptId, DocumentType = (int)ComplianceDocumentTypes.InsuranceCertificate, Name = "COI", ExpiresOn = Day.AddYears(1) }, new byte[] { 9, 9 }, "coi.pdf", "application/pdf", User, null, null);
			var deployment = new Deployment { DeploymentId = "dep-1", DepartmentId = DeptId, ServiceContractId = contract.ServiceContractId };
			_deploymentRows.Setup(d => d.GetByIdForDepartmentAsync("dep-1", DeptId)).ReturnsAsync(deployment);
			_deployments.Setup(d => d.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(deployment);
			_deployments.Setup(d => d.GetAttachmentAsync(5, DeptId, true)).ReturnsAsync(new DeploymentAttachment { DeploymentAttachmentId = 5, FileName = "receipt.jpg", Data = new byte[] { 1 } });
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = DeptId, InvoiceNumber = 500, DeploymentId = "dep-1", ServiceContractId = contract.ServiceContractId, LineItems = new List<InvoiceLineItem> { new InvoiceLineItem { DeploymentTimeReportId = "r1" } } };
			var invoicing = new Mock<IInvoicingService>();
			invoicing.Setup(i => i.GetInvoiceByIdAsync("inv-1", DeptId)).ReturnsAsync(invoice);
			invoicing.Setup(i => i.GetInvoicePdfAsync("inv-1", DeptId)).ReturnsAsync(new byte[] { 7 });
			InvoiceSendAttachment sentAttachment = null;
			invoicing.Setup(i => i.SendInvoiceAsync("inv-1", DeptId, "ap@province.example", It.IsAny<InvoiceSendAttachment>(), User, null, null, It.IsAny<CancellationToken>())).Callback<string, int, string, InvoiceSendAttachment, string, string, string, CancellationToken>((_, __, ___, a, ____, _____, ______, _______) => sentAttachment = a).ReturnsAsync(invoice);
			var timeTracking = new Mock<ITimeTrackingService>();
			timeTracking.Setup(t => t.GetTimeReportByIdAsync("r1", DeptId)).ReturnsAsync(new DeploymentTimeReport { DeploymentTimeReportId = "r1", ReportNumber = 3, ReportDate = Day });
			timeTracking.Setup(t => t.GetTimeReportPdfAsync("r1", DeptId)).ReturnsAsync(new byte[] { 8 });
			timeTracking.Setup(t => t.GetExpensesAsync("dep-1", DeptId)).ReturnsAsync(new List<DeploymentExpense> { new DeploymentExpense { DeploymentExpenseId = "x1", DeploymentTimeReportId = "r1", Billable = true, ReceiptAttachmentId = 5 } });
			var engine = new ContractorBillingEngine(_deployments.Object, timeTracking.Object, new Mock<IDeploymentTimeEntryRepository>().Object, _rates, _contractService, invoicing.Object, new Mock<IUnitsService>().Object, new Mock<IUserProfileService>().Object, new Mock<IEventAggregator>().Object, null);

			var packet = await engine.BuildInvoicePacketAsync("inv-1", DeptId);
			packet.FileName.Should().Be("invoice-500-packet.zip");
			packet.Contents.Should().BeEquivalentTo("invoice-500.pdf", "dtr/dtr-3-2026-09-18.pdf", "receipts/receipt.jpg", "compliance/coi.pdf");
			packet.MissingRequirements.Should().Equal("Bond");
			using (var zip = new ZipArchive(new MemoryStream(packet.Data), ZipArchiveMode.Read))
				zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(packet.Contents);

			await engine.SendDeploymentInvoiceAsync("inv-1", DeptId, null, User, null, null);
			sentAttachment.ContentType.Should().Be("application/zip");
			sentAttachment.Contents.Should().HaveCount(4);
		}

		#endregion
	}
}
