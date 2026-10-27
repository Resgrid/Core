using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The operations desk (plan §9.5, §9.17): who is on the incident with both status columns kept apart — the support status this
	/// module owns and the position they hold — and the resources by support status.
	/// </summary>
	public sealed class IncidentOperationsController : IncidentBackOfficeControllerBase
	{
		private readonly IIncidentOperationalIntelligenceService _intelligence;
		private readonly IIncidentLedgerService<IncidentResource> _resources;

		public IncidentOperationsController(IncidentDeskServices desk, IIncidentOperationalIntelligenceService intelligence, IIncidentLedgerService<IncidentResource> resources) : base(desk)
		{
			_intelligence = intelligence;
			_resources = resources;
		}

		protected override string Section => "Command";

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default)
		{
			var view = Prepare(new IncidentOperationsDeskView());
			var activationId = Context.Activation.IncidentActivationId;
			view.Summary = await _intelligence.GetDeskSummaryAsync(Actor, activationId, cancellationToken);
			if (ClaimsAuthorizationHelper.CanViewIncidentParticipant())
				view.People = (await _intelligence.GetPersonnelPictureAsync(Actor, activationId, cancellationToken)).ToList();
			view.Resources = (await _resources.ListAsync(Actor, new IncidentLedgerQuery { ActivationIds = Scope, Take = 1000 }.Order("Name"), cancellationToken)).ToList();
			return View(view);
		}
	}

	/// <summary>The logistics desk (plan §9.17.1): the section's numbers, inbound arrivals by window and the way into each logistics desk.</summary>
	public sealed class IncidentLogisticsController : IncidentBackOfficeControllerBase
	{
		private readonly IIncidentOperationalIntelligenceService _intelligence;

		public IncidentLogisticsController(IncidentDeskServices desk, IIncidentOperationalIntelligenceService intelligence) : base(desk)
		{
			_intelligence = intelligence;
		}

		protected override string Section => "Logistics";

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default)
		{
			var view = Prepare(new IncidentCommandDeskView());
			view.Summary = await _intelligence.GetDeskSummaryAsync(Actor, Context.Activation.IncidentActivationId, cancellationToken);
			return View(view);
		}
	}

	/// <summary>
	/// The finance desk (plan §9.14, §9.17.3): each budget line against its committed spend, the latest daily-cost projection, and the
	/// queues finance works (time awaiting certification or approval, invoice exceptions). Each block shows only to its own claim.
	/// </summary>
	public sealed class IncidentFinanceController : IncidentBackOfficeControllerBase
	{
		private readonly IIncidentLedgerService<IncidentBudgetLine> _lines;
		private readonly IIncidentLedgerService<IncidentCommitment> _commitments;
		private readonly IIncidentLedgerService<IncidentCostProjection> _projections;
		private readonly IIncidentLedgerService<IncidentTimeRecord> _time;
		private readonly IIncidentLedgerService<VendorInvoiceReference> _invoices;

		public IncidentFinanceController(IncidentDeskServices desk, IIncidentLedgerService<IncidentBudgetLine> lines, IIncidentLedgerService<IncidentCommitment> commitments,
			IIncidentLedgerService<IncidentCostProjection> projections, IIncidentLedgerService<IncidentTimeRecord> time, IIncidentLedgerService<VendorInvoiceReference> invoices) : base(desk)
		{
			_lines = lines;
			_commitments = commitments;
			_projections = projections;
			_time = time;
			_invoices = invoices;
		}

		protected override string Section => "Finance";
		protected override IncidentBackOfficeArea ModuleArea => IncidentBackOfficeArea.Business;

		private IncidentLedgerQuery Query() => new IncidentLedgerQuery { ActivationIds = Scope, Take = 5000 };

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default)
		{
			var view = Prepare(new IncidentFinanceDeskView());
			if (ClaimsAuthorizationHelper.CanViewIncidentBudget() || ClaimsAuthorizationHelper.CanViewIncidentProcurement())
			{
				var commitments = (await _commitments.ListAsync(Actor, Query(), cancellationToken)).Where(c => c.Status != (int)IncidentCommitmentStatuses.Void).ToList();
				view.Committed = commitments.Sum(c => c.Amount);
				view.OpenCommitments = commitments.Count(c => c.Status is (int)IncidentCommitmentStatuses.Open or (int)IncidentCommitmentStatuses.PartiallyReceived);
				if (ClaimsAuthorizationHelper.CanViewIncidentBudget())
					view.Lines = (await _lines.ListAsync(Actor, Query().Order("Name"), cancellationToken))
						.Select(l => new IncidentBudgetLineRow { Line = l, Committed = commitments.Where(c => c.IncidentBudgetLineId == l.IncidentBudgetLineId).Sum(c => c.Amount) }).ToList();
			}
			if (ClaimsAuthorizationHelper.CanViewIncidentCost())
				view.Projection = (await _projections.ListAsync(Actor, Query().Order("AddedOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
			if (ClaimsAuthorizationHelper.CanViewIncidentTime())
			{
				view.TimeAwaitingCertification = await _time.CountAsync(Actor, Query().WhereIn("Status", new[] { (int)IncidentTimeStatuses.Draft, (int)IncidentTimeStatuses.Submitted }), cancellationToken);
				view.TimeAwaitingApproval = await _time.CountAsync(Actor, Query().Where("Status", (int)IncidentTimeStatuses.Certified), cancellationToken);
			}
			if (ClaimsAuthorizationHelper.CanViewIncidentProcurement())
				view.InvoiceExceptions = await _invoices.CountAsync(Actor, Query().Where("Status", (int)IncidentInvoiceStatuses.Exception), cancellationToken);
			return View(view);
		}
	}

	/// <summary>The incident action plan record (plan §9.1): plan versions per operational period, assembled and approved on the periods desk.</summary>
	public sealed class IncidentIapController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentPeriodsLedgerKinds _kinds;

		public IncidentIapController(IncidentDeskServices desk, IncidentPeriodsLedgerKinds kinds) : base(desk)
		{
			_kinds = kinds;
		}

		protected override string Section => "Records";
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All.Where(k => k.Descriptor.EntityType == typeof(IncidentPlanVersion) || k.Descriptor.EntityType == typeof(IncidentPlanWeatherBlock)).ToList();

		[HttpGet]
		public override Task<IActionResult> Index(CancellationToken cancellationToken = default) =>
			Task.FromResult<IActionResult>(RedirectToAction(nameof(List), new { kind = nameof(IncidentPlanVersion) }));
	}
}
