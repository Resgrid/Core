using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The position catalog (plan §9.2): department and incident positions with their capabilities through the generic ledger, plus
	/// seeding the missing positions of a standard catalog (NIMS core, NWCG, CICCS) — never changing an existing row.
	/// </summary>
	public sealed class IncidentPositionsController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentPositionsLedgerKinds _kinds;
		private readonly IIncidentOrganizationService _organization;

		public IncidentPositionsController(IncidentDeskServices desk, IncidentPositionsLedgerKinds kinds, IIncidentOrganizationService organization) : base(desk)
		{
			_kinds = kinds;
			_organization = organization;
		}

		protected override string Section => "Department";
		protected override bool RequiresActivation => false;
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		protected override IEnumerable<LedgerDeskAction> DeskActions()
		{
			if (CanWrite && ClaimsAuthorizationHelper.CanManageIncidentPosition())
				foreach (var catalog in new[] { IncidentPositionSources.Nims, IncidentPositionSources.Nwcg, IncidentPositionSources.Ciccs })
					yield return new LedgerDeskAction { Action = "Seed" + catalog, LabelKey = "SeedCatalog." + catalog, Icon = "fa-download" };
		}

		[HttpGet]
		public IActionResult SeedNims() => View("Seed", Prepare(new IncidentSeedView { Catalog = IncidentPositionSources.Nims }));

		[HttpGet]
		public IActionResult SeedNwcg() => View("Seed", Prepare(new IncidentSeedView { Catalog = IncidentPositionSources.Nwcg }));

		[HttpGet]
		public IActionResult SeedCiccs() => View("Seed", Prepare(new IncidentSeedView { Catalog = IncidentPositionSources.Ciccs }));

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Seed(int catalog, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentPosition()) return Forbid();
			if (!Enum.IsDefined(typeof(IncidentPositionSources), catalog)) return BadRequest();
			try
			{
				var added = await _organization.SeedCatalogAsync(Actor, (IncidentPositionSources)catalog, cancellationToken);
				TempData["IboSaved"] = true;
				TempData["IboMessage"] = Desk.Strings["CatalogSeeded", added].Value;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(List), new { kind = nameof(IncidentPositionDefinition) });
		}
	}
}
