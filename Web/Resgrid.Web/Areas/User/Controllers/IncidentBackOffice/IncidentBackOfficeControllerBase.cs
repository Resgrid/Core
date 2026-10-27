using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	public enum IncidentBackOfficeArea { Foundation, Business, Coordination }

	/// <summary>
	/// Base of every Incident Back Office desk (large-incident-logistics-finance-plan.md §14). Enforces the D6 gate on every request
	/// (flag → module switch → add-on; a lapsed add-on is read-only with the banner), resolves the selected activation into the context
	/// bar (URL first, then the remembered one), and hosts the generic ledger actions for the tables a desk declares — each guarded by
	/// the table's own claims, so a page admits exactly what its sibling pages admit.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[DepartmentLocalTime]
	public abstract class IncidentBackOfficeControllerBase : SecureBaseController
	{
		public const string ActivationCookie = "rg-ibo-activation";

		protected IncidentBackOfficeControllerBase(IncidentDeskServices desk)
		{
			Desk = desk;
		}

		protected IncidentDeskServices Desk { get; }
		protected IncidentBackOfficeAccess Access { get; private set; } = IncidentBackOfficeAccess.Denied;
		protected IncidentActivationContext Context { get; private set; }
		protected IReadOnlyList<string> Scope { get; private set; } = Array.Empty<string>();
		protected IncidentTime Time { get; private set; }

		/// <summary>Which add-on capability the desk belongs to (LI-A/B, LI-C or LI-D).</summary>
		protected virtual IncidentBackOfficeArea ModuleArea => IncidentBackOfficeArea.Foundation;
		/// <summary>Incident-scoped desks redirect to the selector when no activation is chosen.</summary>
		protected virtual bool RequiresActivation => true;
		/// <summary>The section of the shell's tab strip (Command, Planning, Logistics, Finance, Records, Coordination, Department).</summary>
		protected abstract string Section { get; }
		/// <summary>The tables this desk hosts through the generic ledger actions.</summary>
		protected virtual IReadOnlyList<ILedgerKind> Kinds => Array.Empty<ILedgerKind>();

		protected bool CanWrite => ModuleArea switch
		{
			IncidentBackOfficeArea.Business => Access.CanWriteBusiness,
			IncidentBackOfficeArea.Coordination => Access.CanWriteCoordination,
			_ => Access.CanWrite
		};

		protected IncidentActor Actor => new IncidentActor
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
			UserAgent = Request?.Headers["User-Agent"].ToString(),
			IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
		};

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			Access = await Desk.Access.GetAccessAsync(DepartmentId);
			var areaEnabled = ModuleArea switch
			{
				IncidentBackOfficeArea.Business => Access.BusinessEnabled,
				IncidentBackOfficeArea.Coordination => Access.CoordinationEnabled,
				_ => true
			};
			if (!Access.Enabled || !areaEnabled)
			{
				context.Result = NotFound();
				return;
			}
			var canView = ModuleArea == IncidentBackOfficeArea.Coordination ? ClaimsAuthorizationHelper.CanViewCoordinationBoard() || ClaimsAuthorizationHelper.CanViewIncidentBackOffice()
				: ClaimsAuthorizationHelper.CanViewIncidentBackOffice();
			if (!canView)
			{
				context.Result = Forbid();
				return;
			}
			if (Access.Level == IncidentBackOfficeAccessLevels.None)
			{
				context.Result = RedirectToAction("Index", "IncidentBackOfficeBilling", new { area = "User" });
				return;
			}

			var department = await Desk.Departments.GetDepartmentByIdAsync(DepartmentId);
			var activationId = Request.Query["aid"].ToString();
			if (string.IsNullOrWhiteSpace(activationId) && context.ActionArguments.TryGetValue("aid", out var argument))
				activationId = argument as string;
			if (string.IsNullOrWhiteSpace(activationId))
				activationId = Request.Cookies[ActivationCookie];
			if (!string.IsNullOrWhiteSpace(activationId))
			{
				Context = await Desk.Activations.GetContextAsync(Actor, activationId);
				if (Context != null)
				{
					Scope = await Desk.Activations.GetScopeAsync(Actor, activationId);
					if (Request.Cookies[ActivationCookie] != activationId)
						Response.Cookies.Append(ActivationCookie, activationId, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true });
				}
			}
			Time = new IncidentTime(Context?.Activation?.TimeZoneId, department?.TimeZone);

			if (RequiresActivation && Context == null)
			{
				context.Result = RedirectToAction("Index", "IncidentBackOffice", new { area = "User" });
				return;
			}

			// A lapsed add-on is read-only: every write is refused with the banner's message (the Invoicing precedent).
			if (HttpMethods.IsPost(Request.Method) && !CanWrite)
			{
				TempData["IboMessage"] = Desk.Strings["LapsedReadOnly"].Value;
				context.Result = Redirect(Request.Headers["Referer"].ToString() is { Length: > 0 } referer && Url.IsLocalUrl(new Uri(referer).PathAndQuery) ? new Uri(referer).PathAndQuery : Url.Action("Index", "IncidentBackOffice", new { area = "User" }));
				return;
			}

			ViewData["IboAccess"] = Access;
			ViewData["IboContext"] = Context;
			ViewData["IboTime"] = Time;
			ViewData["IboCanWrite"] = CanWrite;
			ViewData["IboSection"] = Section;
			ViewData["IboArea"] = ModuleArea.ToString();
			await next();
		}

		protected T Prepare<T>(T view, string tab = null) where T : IncidentDeskView
		{
			view.Access = Access;
			view.Context = Context;
			view.CanWrite = CanWrite;
			view.Section = Section;
			view.Tab = tab;
			if (TempData["IboMessage"] is string message) view.Message = message;
			if (TempData["IboNotice"] is string notice) view.Notice = notice;
			if (TempData["IboSaved"] is bool saved) view.Saved = saved;
			return view;
		}

		protected LedgerRequest LedgerRequestFor(string search = null, int page = 0, string parentField = null, string parentValue = null, bool reveal = false)
		{
			var filters = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var (key, value) in Request.Query)
				if (key.StartsWith("f_", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
					filters[key.Substring(2)] = value.ToString();
			return new LedgerRequest
			{
				Actor = Actor,
				Context = Context,
				Scope = Scope,
				Time = Time,
				Desk = Desk,
				Filters = filters,
				Search = search,
				Page = Math.Max(0, page),
				ParentField = parentField,
				ParentValue = parentValue,
				Reveal = reveal
			};
		}

		/// <summary>A rule refusal shown to the person in their language (IncidentRuleException carries a resx key).</summary>
		protected string RuleText(Exception ex)
		{
			switch (ex)
			{
				case IncidentRuleException rule:
					var text = Desk.Strings["Rule." + rule.MessageKey];
					return text.ResourceNotFound ? Desk.Strings["SaveFailed"].Value : string.Format(CultureInfo.CurrentCulture, text.Value, rule.Args);
				case IncidentConcurrencyException:
					return Desk.Strings["Rule.Concurrency"].Value;
				default:
					Framework.Logging.LogException(ex);
					return Desk.Strings["SaveFailed"].Value;
			}
		}

		protected ILedgerKind Kind(string kind) =>
			Kinds.FirstOrDefault(k => string.Equals(k.Descriptor.Key, kind, StringComparison.OrdinalIgnoreCase));

		protected IActionResult Back(string fallbackAction, object route = null)
		{
			var returnUrl = Request.Form.TryGetValue("returnUrl", out var value) ? value.ToString() : null;
			if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
				return Redirect(returnUrl);
			return RedirectToAction(fallbackAction, route);
		}

		#region Generic ledger actions

		[HttpGet]
		public virtual async Task<IActionResult> List(string kind, string search = null, int page = 0, string pf = null, string pv = null, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			if (!ledger.Descriptor.CanRead()) return Forbid();
			var view = Prepare(await ledger.ListAsync(LedgerRequestFor(search, page, pf, pv), cancellationToken), ledger.Descriptor.Key);
			view.KindCanWrite = CanWrite && ledger.Descriptor.CanWrite() && !ledger.Descriptor.IsReadOnly;
			view.Siblings = Kinds.Where(k => k.Descriptor.CanRead()).Select(k => k.Descriptor).ToList();
			if (!string.IsNullOrWhiteSpace(pf) && !string.IsNullOrWhiteSpace(pv))
			{
				var parentField = ledger.Descriptor.Fields.FirstOrDefault(f => f.Name == pf);
				if (parentField?.RefTable != null)
					view.ParentLabel = (await Desk.Lookups.GetLabelsAsync(Actor, parentField.RefTable, new[] { pv }, cancellationToken)).TryGetValue(pv, out var label) ? label : null;
			}
			return View("~/Areas/User/Views/IncidentBackOffice/Ledger/List.cshtml", view);
		}

		[HttpGet]
		public virtual async Task<IActionResult> Edit(string kind, string id = null, bool reveal = false, string pf = null, string pv = null, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			if (!ledger.Descriptor.CanRead()) return Forbid();
			if (string.IsNullOrWhiteSpace(id) && (!ledger.Descriptor.CanWrite() || ledger.Descriptor.IsReadOnly)) return Forbid();
			var view = await ledger.EditAsync(LedgerRequestFor(null, 0, pf, pv, reveal), id, cancellationToken);
			if (view == null) return NotFound();
			Prepare(view, ledger.Descriptor.Key);
			view.KindCanWrite = CanWrite && ledger.Descriptor.CanWrite() && !ledger.Descriptor.IsReadOnly && (!ledger.Descriptor.AppendOnly || view.IsNew);
			view.Children = ChildKinds(ledger.Descriptor);
			if (!view.IsNew && CanWrite)
				view.RowActions.AddRange(RowActions(ledger.Descriptor, view));
			return View("~/Areas/User/Views/IncidentBackOffice/Ledger/Edit.cshtml", view);
		}

		/// <summary>Tables hosted on this desk whose rows reference <paramref name="parent"/> (a facility's capacities, a team's slots).</summary>
		private List<LedgerKindDescriptor> ChildKinds(LedgerKindDescriptor parent)
		{
			var key = parent.EntityType.Name + "Id";
			return Kinds.Select(k => k.Descriptor).Where(d => d != parent && d.CanRead() && d.Fields.Any(f => f.Name == key && f.RefTable == parent.Table)).ToList();
		}

		[HttpPost, ValidateAntiForgeryToken]
		public virtual async Task<IActionResult> Save(string kind, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			if (!ledger.Descriptor.CanWrite() || ledger.Descriptor.IsReadOnly) return Forbid();
			var form = await Request.ReadFormAsync(cancellationToken);
			if (ledger.Descriptor.AppendOnly && !string.IsNullOrWhiteSpace(form["Id"])) return Forbid();
			try
			{
				var (id, errors) = await ledger.SaveAsync(LedgerRequestFor(null, 0, form["ParentField"], form["ParentValue"]), form, cancellationToken);
				if (errors.Count > 0)
				{
					var view = await ledger.EditAsync(LedgerRequestFor(null, 0, form["ParentField"], form["ParentValue"]), id, cancellationToken) ?? new LedgerEditView { Kind = ledger.Descriptor };
					foreach (var field in ledger.Descriptor.Fields.Where(f => form.ContainsKey(f.Name)))
						view.Values[field.Name] = form[field.Name];
					foreach (var (field, error) in errors)
						view.Errors[field] = Desk.Strings["Error." + error].Value;
					Prepare(view, ledger.Descriptor.Key);
					view.KindCanWrite = true;
					return View("~/Areas/User/Views/IncidentBackOffice/Ledger/Edit.cshtml", view);
				}
				TempData["IboSaved"] = true;
				var parentField = form["ParentField"].ToString();
				return string.IsNullOrWhiteSpace(form["returnUrl"]) || !Url.IsLocalUrl(form["returnUrl"])
					? RedirectToAction(nameof(Edit), new { kind, id, pf = string.IsNullOrWhiteSpace(parentField) ? null : parentField, pv = string.IsNullOrWhiteSpace(parentField) ? null : form["ParentValue"].ToString() })
					: Redirect(form["returnUrl"]);
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
				var id = form["Id"].ToString();
				return RedirectToAction(nameof(Edit), new { kind, id = string.IsNullOrWhiteSpace(id) ? null : id, pf = form["ParentField"].ToString(), pv = form["ParentValue"].ToString() });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public virtual async Task<IActionResult> Delete(string kind, string id, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			if (!ledger.Descriptor.CanWrite() || ledger.Descriptor.IsReadOnly || !ledger.Descriptor.CanDelete) return Forbid();
			try
			{
				await ledger.DeleteAsync(LedgerRequestFor(), id, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return Back(nameof(List), new { kind });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public virtual async Task<IActionResult> Transition(string kind, string id, int status, string reason = null, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			var allowed = ledger.Descriptor.CanTransitionTo == null ? ledger.Descriptor.CanWrite() : ledger.Descriptor.CanTransitionTo(status);
			if (!allowed || ledger.Descriptor.IsReadOnly) return Forbid();
			try
			{
				await TransitionHookAsync(ledger, id, status, reason, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return Back(nameof(Edit), new { kind, id });
		}

		/// <summary>Desks whose transitions run through a domain service (fill acceptance, check-in, approvals) override this.</summary>
		protected virtual Task TransitionHookAsync(ILedgerKind ledger, string id, int status, string reason, CancellationToken cancellationToken)
			=> ledger.TransitionAsync(LedgerRequestFor(), id, status, reason, cancellationToken);

		/// <summary>The exact rows behind a list as CSV, formula prefixes neutralized (plan §9.17 export, §13.9 C12). Protected columns never export.</summary>
		[HttpGet]
		public virtual async Task<IActionResult> Export(string kind, string search = null, string pf = null, string pv = null, CancellationToken cancellationToken = default)
		{
			var ledger = Kind(kind);
			if (ledger == null) return NotFound();
			if (!ledger.Descriptor.CanRead()) return Forbid();
			var request = LedgerRequestFor(search, 0, pf, pv);
			request.PageSize = 5000;
			var list = await ledger.ListAsync(request, cancellationToken);
			var csv = new StringBuilder();
			csv.AppendLine(string.Join(",", list.Columns.Select(c => global::Resgrid.Services.IncidentLogistics.IncidentCsv.Cell(Label(ledger.Descriptor, c.Name)))));
			foreach (var row in list.Rows)
				csv.AppendLine(string.Join(",", list.Columns.Select(c => global::Resgrid.Services.IncidentLogistics.IncidentCsv.Cell(row.Cells.TryGetValue(c.Name, out var v) ? v : string.Empty))));
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"{ledger.Descriptor.Table}-{DateTime.UtcNow:yyyyMMddHHmm}.csv");
		}

		/// <summary>Domain actions offered on one stored row of <paramref name="kind"/>; each posts <c>id</c> to the named action.</summary>
		protected virtual IEnumerable<LedgerDeskAction> RowActions(LedgerKindDescriptor kind, LedgerEditView row) => Array.Empty<LedgerDeskAction>();

		/// <summary>The desk's own workflows, linked above its tables on the landing page.</summary>
		protected virtual IEnumerable<LedgerDeskAction> DeskActions() => Array.Empty<LedgerDeskAction>();

		protected string Label(LedgerKindDescriptor kind, string field) => LedgerText.Field(Desk.Strings, kind, field);

		/// <summary>The desk's landing page: its tables with their counts in the selected incident (one table goes straight to its list).</summary>
		[HttpGet]
		public virtual async Task<IActionResult> Index(CancellationToken cancellationToken = default)
		{
			var readable = Kinds.Where(k => k.Descriptor.CanRead()).ToList();
			if (readable.Count == 0) return Forbid();
			if (readable.Count == 1 && !DeskActions().Any()) return RedirectToAction(nameof(List), new { kind = readable[0].Descriptor.Key });
			var view = Prepare(new LedgerDeskView());
			view.Actions.AddRange(DeskActions());
			var request = LedgerRequestFor();
			foreach (var kind in readable)
				view.Entries.Add(new LedgerDeskEntry
				{
					Kind = kind.Descriptor,
					Count = await kind.CountAsync(request, cancellationToken),
					CanWrite = CanWrite && kind.Descriptor.CanWrite() && !kind.Descriptor.IsReadOnly
				});
			return View("~/Areas/User/Views/IncidentBackOffice/Ledger/Desk.cshtml", view);
		}

		#endregion
	}
}
