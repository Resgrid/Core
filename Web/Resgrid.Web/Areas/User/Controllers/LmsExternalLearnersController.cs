using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.LmsExternalLearners;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → External learners (Training T5 item 2, LMS add-on, dark behind Training.Lms): the learner portal's settings and branding,
	/// external learners (invite, enrol in courses and classes, suspend, remove, export and erase for GDPR), which courses external learners
	/// may take, and a class session's public listing, fee, roster export and Business Operations invoices. Managed by the class managers
	/// (department admins, Manage Courses 132, Assign Training 126; no new permission); raising invoices also needs Business Operations'
	/// Invoicing create right. NotFound unless the Trainings module and the operator flag are on.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsExternalLearnersController : SecureBaseController
	{
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsExternalLearnerService _learners;
		private readonly ILmsPublicClassService _publicClasses;
		private readonly ILmsClassService _classes;
		private readonly ILmsCourseService _courses;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners> _localizer;

		private LmsActor _actor;

		public LmsExternalLearnersController(ILmsGateService gate, ILmsAccessService access, ILmsExternalLearnerService learners, ILmsPublicClassService publicClasses,
			ILmsClassService classes, ILmsCourseService courses, IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners> localizer)
		{
			_gate = gate;
			_access = access;
			_learners = learners;
			_publicClasses = publicClasses;
			_classes = classes;
			_courses = courses;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Dark until the operator flag is on; only class managers (one gate read per request, R-L9).
			var actor = await ActorAsync();
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId) || !actor.Gate.FlagOn || !LmsClassesController.MayManageClasses(actor))
			{
				context.Result = NotFound();
				return;
			}
			ViewData["LmsPrograms"] = true;
			ViewData["Section"] = "External";
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);

		private bool CanInvoice => User.HasClaim(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.Create);

		private string L(string key, params object[] args)
		{
			var value = _localizer?[key];
			var text = value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
			return args == null || args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
		}

		private string Code(string code, int packs = 0) => code != null && ServiceCode.IsMatch(code) ? L(code, packs) : L("ActionFailed");

		private void Flash(string message = null, string error = null)
		{
			if (message != null)
				TempData["XlMessage"] = message;
			if (error != null)
				TempData["XlError"] = error;
		}

		private async Task<T> PageAsync<T>(T model) where T : LmsExternalLearnersPage
		{
			model.Message = TempData["XlMessage"] as string;
			model.Error = TempData["XlError"] as string;
			model.CanAuthor = _actor?.Gate?.CanAuthor == true;
			// Dates in the department's time zone and fees in its training currency (region neutrality, round 9C).
			model.Branding ??= await _learners.GetBrandingAsync(await _learners.GetPortalAsync(DepartmentId) ?? new LmsLearnerPortal { DepartmentId = DepartmentId });
			model.DefaultCurrency ??= await _classes.GetTrainingCurrencyAsync(DepartmentId);
			return model;
		}

		private async Task<List<LmsCourse>> OpenCoursesAsync()
		{
			var access = (await _learners.GetCourseAccessAsync(DepartmentId)).Where(a => a.Access != (int)LmsExternalCourseAccessKinds.None).Select(a => a.LmsCourseId).ToHashSet();
			return (await DepartmentCoursesAsync()).Where(c => access.Contains(c.LmsCourseId)).ToList();
		}

		private async Task<List<LmsCourse>> DepartmentCoursesAsync() =>
			((await _courses.GetCourseRowsAsync(await ActorAsync())) ?? new List<LmsCourseRow>()).Select(r => r.Course)
				.Where(c => c != null && !c.IsSystem && !c.IsDeleted && c.DepartmentId == DepartmentId && c.Status == (int)ProtocolStatuses.Published && c.CurrentVersionId.HasValue)
				.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

		private async Task<List<LmsClassSession>> UpcomingSessionsAsync() =>
			(await _classes.GetSessionsAsync(DepartmentId, DateTime.UtcNow, DateTime.UtcNow.AddDays(366)) ?? new List<LmsClassSession>())
				.Where(s => s.IsScheduled && s.StartsOn > DateTime.UtcNow).OrderBy(s => s.StartsOn).ToList();

		private static Dictionary<string, string> CustomerNames(IEnumerable<CustomerBillingProfile> customers) =>
			(customers ?? Enumerable.Empty<CustomerBillingProfile>()).Where(c => !string.IsNullOrWhiteSpace(c.ContactId))
				.GroupBy(c => c.ContactId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().BillingEmail) ? g.Key : g.First().BillingEmail, StringComparer.OrdinalIgnoreCase);

		#endregion Gates and helpers

		#region Learners

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var portal = await _learners.GetPortalAsync(DepartmentId);
			var customers = await _publicClasses.GetCustomersAsync(DepartmentId);
			var model = await PageAsync(new LmsExternalLearnersIndexModel
			{
				Portal = portal,
				PortalUrl = portal == null ? null : _learners.PortalUrl(portal),
				FairUse = await _learners.GetFairUseAsync(DepartmentId),
				Learners = (await _learners.GetLearnersAsync(DepartmentId)).Where(l => l.Status != (int)LmsExternalLearnerStatuses.Erased).ToList(),
				Courses = await OpenCoursesAsync(),
				Sessions = await UpcomingSessionsAsync(),
				Customers = customers,
				CustomerNames = CustomerNames(customers)
			});
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Invite(string email, string firstName, string lastName, string organization, string sponsorContactId, List<int> courseIds,
			List<int> classIds, CancellationToken cancellationToken)
		{
			var result = await _learners.InviteAsync(DepartmentId, new LmsExternalLearnerInvite
			{
				Email = email,
				FirstName = firstName,
				LastName = lastName,
				Organization = organization,
				SponsorContactId = sponsorContactId,
				CourseIds = courseIds ?? new List<int>(),
				ClassSessionIds = classIds ?? new List<int>()
			}, UserId, cancellationToken);
			if (!result.Succeeded)
			{
				Flash(error: Code(result.Error, result.PacksNeeded));
				return RedirectToAction("Index");
			}
			var notes = (result.Value as List<string>) ?? new List<string>();
			Flash(L("Invited"), notes.Count == 0 ? null : string.Join(" ", notes.Distinct().Select(n => Code(n))));
			return RedirectToAction("Learner", new { id = result.Learner.LmsExternalLearnerId });
		}

		[HttpGet]
		public async Task<IActionResult> Learner(int id)
		{
			var learner = await _learners.GetLearnerAsync(DepartmentId, id);
			if (learner == null || learner.Status == (int)LmsExternalLearnerStatuses.Erased)
				return NotFound();
			var customers = await _publicClasses.GetCustomersAsync(DepartmentId);
			return View(await PageAsync(new LmsExternalLearnerModel
			{
				Learner = learner,
				Learning = await _learners.GetMyLearningAsync(DepartmentId, learner, DateTime.UtcNow),
				Courses = await OpenCoursesAsync(),
				Sessions = await UpcomingSessionsAsync(),
				Customers = customers,
				CustomerNames = CustomerNames(customers)
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Update(int id, string firstName, string lastName, string organization, string sponsorContactId, string billingContactId,
			CancellationToken cancellationToken)
		{
			var customers = await _publicClasses.GetCustomersAsync(DepartmentId);
			bool Known(string contactId) => string.IsNullOrWhiteSpace(contactId) || customers.Any(c => string.Equals(c.ContactId, contactId, StringComparison.OrdinalIgnoreCase));
			if (!Known(sponsorContactId) || !Known(billingContactId))
				Flash(error: Code("lms_xl_customer_invalid"));
			else
			{
				var result = await _learners.UpdateAsync(DepartmentId, id, firstName, lastName, organization, sponsorContactId, billingContactId, UserId, cancellationToken);
				if (result.Succeeded) Flash(L("Saved")); else Flash(error: Code(result.Error));
			}
			return RedirectToAction("Learner", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetStatus(int id, LmsExternalLearnerStatuses status, CancellationToken cancellationToken)
		{
			var result = await _learners.SetStatusAsync(DepartmentId, id, status, UserId, cancellationToken);
			if (result.Succeeded) Flash(L("Saved")); else Flash(error: Code(result.Error, result.PacksNeeded));
			return RedirectToAction("Learner", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Resend(int id, CancellationToken cancellationToken)
		{
			var result = await _learners.ResendInviteAsync(DepartmentId, id, UserId, cancellationToken);
			if (result.Succeeded) Flash(L("Invited")); else Flash(error: Code(result.Error));
			return RedirectToAction("Learner", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EnrollCourse(int id, int courseId, CancellationToken cancellationToken)
		{
			var result = await _learners.EnrollInCourseAsync(DepartmentId, id, courseId, UserId, cancellationToken);
			if (result.Succeeded) Flash(L("Enrolled")); else Flash(error: Code(result.Error));
			return RedirectToAction("Learner", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EnrollClass(int id, int classId, CancellationToken cancellationToken)
		{
			var result = await _learners.EnrollInClassAsync(DepartmentId, id, classId, UserId, cancellationToken);
			if (result.Succeeded) Flash(L("Enrolled")); else Flash(error: Code(result.Error));
			return RedirectToAction("Learner", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Export(int id)
		{
			var json = await _learners.ExportAsync(DepartmentId, id);
			return json == null ? NotFound() : File(Encoding.UTF8.GetBytes(json), "application/json", "external-learner-" + id.ToString(CultureInfo.InvariantCulture) + ".json");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Erase(int id, bool confirm, CancellationToken cancellationToken)
		{
			if (!confirm)
			{
				Flash(error: L("EraseConfirmRequired"));
				return RedirectToAction("Learner", new { id });
			}
			var result = await _learners.EraseAsync(DepartmentId, id, UserId, cancellationToken);
			if (!result.Succeeded)
			{
				Flash(error: Code(result.Error));
				return RedirectToAction("Learner", new { id });
			}
			Flash(L("Erased"));
			return RedirectToAction("Index");
		}

		#endregion Learners

		#region Portal settings and course access

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			var portal = await _learners.GetPortalAsync(DepartmentId);
			return View(await PageAsync(new LmsLearnerPortalSettingsModel
			{
				Portal = portal,
				PortalUrl = portal == null ? null : _learners.PortalUrl(portal),
				Branding = await _learners.GetBrandingAsync(portal ?? new LmsLearnerPortal { DepartmentId = DepartmentId }),
				Input = portal == null ? new LmsLearnerPortalInput() : new LmsLearnerPortalInput
				{
					IsEnabled = portal.IsEnabled,
					SelfRegistration = portal.SelfRegistration,
					PublicListing = portal.PublicListing,
					AccentColor = portal.AccentColor,
					WelcomeText = portal.WelcomeText,
					HideLogo = portal.HideLogo
				}
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings([Bind(Prefix = "Input")] LmsLearnerPortalInput input, CancellationToken cancellationToken)
		{
			try
			{
				await _learners.SavePortalAsync(DepartmentId, input, UserId, cancellationToken);
				Flash(L("Saved"));
			}
			catch (InvalidOperationException ex)
			{
				Flash(error: Code(ex.Message));
			}
			return RedirectToAction("Settings");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RegenerateKey(CancellationToken cancellationToken)
		{
			try
			{
				await _learners.RegeneratePortalKeyAsync(DepartmentId, UserId, cancellationToken);
				Flash(L("Saved"));
			}
			catch (InvalidOperationException ex)
			{
				Flash(error: Code(ex.Message));
			}
			return RedirectToAction("Settings");
		}

		[HttpGet]
		public async Task<IActionResult> Courses()
		{
			return View(await PageAsync(new LmsExternalCoursesModel
			{
				Courses = await DepartmentCoursesAsync(),
				Access = (await _learners.GetCourseAccessAsync(DepartmentId)).GroupBy(a => a.LmsCourseId).ToDictionary(g => g.Key, g => g.First().Access)
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetCourseAccess(int courseId, LmsExternalCourseAccessKinds access, CancellationToken cancellationToken)
		{
			try
			{
				await _learners.SetCourseAccessAsync(DepartmentId, courseId, access, UserId, cancellationToken);
				Flash(L("Saved"));
			}
			catch (InvalidOperationException ex)
			{
				Flash(error: Code(ex.Message));
			}
			return RedirectToAction("Courses");
		}

		#endregion Portal settings and course access

		#region Public classes, fees, roster export and invoices

		[HttpGet]
		public async Task<IActionResult> Class(int id)
		{
			var billing = await _publicClasses.GetBillingAsync(DepartmentId, id);
			if (billing == null)
				return NotFound();
			var customers = billing.InvoicingAvailable ? await _publicClasses.GetCustomersAsync(DepartmentId) : new List<CustomerBillingProfile>();
			return View(await PageAsync(new LmsPublicClassModel
			{
				Billing = billing,
				Customers = customers,
				CustomerNames = CustomerNames(customers),
				RateItems = billing.InvoicingAvailable ? await _publicClasses.GetRateItemsAsync(DepartmentId) : new List<RateCardItem>(),
				CanInvoice = billing.InvoicingAvailable && CanInvoice,
				Branding = await _learners.GetBrandingAsync(await _learners.GetPortalAsync(DepartmentId) ?? new LmsLearnerPortal { DepartmentId = DepartmentId })
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveClass(int id, bool isPublic, string fee, string currency, string rateCardItemId, CancellationToken cancellationToken)
		{
			decimal? amount = null;
			if (!string.IsNullOrWhiteSpace(fee))
			{
				if (!decimal.TryParse(fee.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) &&
					!decimal.TryParse(fee.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
				{
					Flash(error: Code("lms_xl_fee_invalid"));
					return RedirectToAction("Class", new { id });
				}
				amount = parsed;
			}
			try
			{
				await _publicClasses.SavePublicClassAsync(DepartmentId, id, isPublic, amount, currency, rateCardItemId, UserId, cancellationToken);
				Flash(L("Saved"));
			}
			catch (InvalidOperationException ex)
			{
				Flash(error: Code(ex.Message));
			}
			return RedirectToAction("Class", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetFee(int id, int enrollmentId, bool waived, string sponsorContactId, CancellationToken cancellationToken)
		{
			try
			{
				await _publicClasses.SetFeeAsync(DepartmentId, enrollmentId, waived, sponsorContactId, UserId, cancellationToken);
				Flash(L("Saved"));
			}
			catch (InvalidOperationException ex)
			{
				Flash(error: Code(ex.Message));
			}
			return RedirectToAction("Class", new { id });
		}

		/// <summary>The roster export (always available, even without Business Operations or after a lapse): learner, organization, attendance, fee.</summary>
		[HttpGet]
		public async Task<IActionResult> ExportRoster(int id)
		{
			var csv = await _publicClasses.ExportRosterCsvAsync(DepartmentId, id, key => L(key));
			return csv == null ? NotFound() : File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv",
				"class-roster-" + id.ToString(CultureInfo.InvariantCulture) + ".csv");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RaiseInvoices(int id, LmsClassInvoiceModes mode, CancellationToken cancellationToken)
		{
			if (!CanInvoice)
				return Forbid();
			var run = await _publicClasses.RaiseInvoicesAsync(DepartmentId, id, mode, UserId, IpAddressHelper.GetRequestIP(Request, true), Request.Headers["User-Agent"].ToString(),
				cancellationToken);
			if (run.Error != null)
				Flash(error: Code(run.Error));
			else
			{
				var skipped = run.Skipped.Count == 0 ? null : L("Class.Skipped") + " " + string.Join("; ", run.Skipped.Select(s => s.Key + " (" + Code(s.Value) + ")"));
				Flash(L("Class.InvoicesRaised", run.InvoiceIds.Count, run.LearnersInvoiced), skipped);
			}
			return RedirectToAction("Class", new { id });
		}

		#endregion Public classes, fees, roster export and invoices
	}
}
