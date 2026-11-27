using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.LmsLearn;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The branded learner portal (Training T5 item 2, §13a branded learner portal): /Learn/{key}. External learners sign in here, and only
	/// here, with their own cookie (<see cref="LmsLearnerAuth"/>, path /Learn), which the member app never reads; this controller never puts
	/// the learner in HttpContext.User and depends on learner-facing services only (a guard test pins its constructor), so a learner can
	/// reach nothing but their own catalog, courses, classes, transcript and certificates. Per-address limits on every signed-out form
	/// (the shared login throttle and public form limiter); round 7B: every signed-out form that creates or e-mails something (registration
	/// and public class sign-up, the password-reset request, the verification/invitation resend) also carries the Account/Register
	/// reCAPTCHA standard (GoogleReCaptchaModelBase, a guard test pins it). No personal data in URLs (tokens only), and no step-up: learners
	/// have no second factor. The portal answers only while it is on, the operator flag and module are on and the add-on works (lapse rule 7
	/// suspends external learners).
	/// </summary>
	[AllowAnonymous]
	[Route("Learn/{portal}")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class LmsLearnController : Controller
	{
		private const long MaxLessonFileBytes = 10 * 1024 * 1024;

		private readonly ILmsExternalLearnerService _learners;
		private readonly ILmsLearnerService _runtime;
		private readonly ILmsAssessmentService _assessments;
		private readonly ILmsMediaService _media;
		private readonly ILmsGateService _gate;
		private readonly ILmsTrainingService _lmsTraining;
		private readonly ILoginThrottleService _throttle;
		private readonly IPublicFormRateLimiter _limits;
		private readonly IPdfProvider _pdf;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _trainingLocalizer;

		private LmsLearnerPortal _portal;
		private LmsLearnerPortalBranding _branding;
		private LmsExternalLearner _learner;

		public LmsLearnController(ILmsExternalLearnerService learners, ILmsLearnerService runtime, ILmsAssessmentService assessments, ILmsMediaService media,
			ILmsGateService gate, ILmsTrainingService lmsTraining, ILoginThrottleService throttle, IPublicFormRateLimiter limits, IPdfProvider pdf,
			IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners> localizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> trainingLocalizer)
		{
			_learners = learners;
			_runtime = runtime;
			_assessments = assessments;
			_media = media;
			_gate = gate;
			_lmsTraining = lmsTraining;
			_throttle = throttle;
			_limits = limits;
			_pdf = pdf;
			_localizer = localizer;
			_trainingLocalizer = trainingLocalizer;
		}

		#region Portal, session and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			_portal = await _learners.GetPortalByKeyAsync(RouteData.Values["portal"] as string);
			if (_portal == null)
			{
				context.Result = NotFound();
				return;
			}

			_branding = await _learners.GetBrandingAsync(_portal);
			ViewData["Branding"] = _branding;
			ViewData["PortalKey"] = _portal.PortalKey;
			if (!await _learners.IsPortalOpenAsync(_portal))
			{
				context.Result = new ViewResult { ViewName = "Unavailable", ViewData = ViewData, StatusCode = StatusCodes.Status503ServiceUnavailable };
				return;
			}

			// The learner's own cookie, read explicitly; the member principal (HttpContext.User) is never replaced.
			var auth = await HttpContext.AuthenticateAsync(LmsLearnerAuth.Scheme);
			var session = auth?.Succeeded == true ? LmsLearnerAuth.Read(auth.Principal) : null;
			if (session != null)
			{
				_learner = session.Value.DepartmentId == _portal.DepartmentId
					? await _learners.ValidateSessionAsync(_portal.DepartmentId, session.Value.LearnerId, session.Value.Stamp)
					: null;
				if (_learner == null)
					await HttpContext.SignOutAsync(LmsLearnerAuth.Scheme);
			}
			ViewData["Learner"] = _learner;
			ViewData["PublicListing"] = _portal.PublicListing;
			ViewData["SelfRegistration"] = _portal.SelfRegistration;
			await next();
		}

		private int DepartmentId => _portal.DepartmentId;

		private string Ip()
		{
			try { return IpAddressHelper.GetRequestIP(Request, true); }
			catch (Exception) { return null; }
		}

		private string L(string key, params object[] args)
		{
			var value = _localizer?[key];
			var text = value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
			return args == null || args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
		}

		private string ErrorText(string code, int packs = 0) => string.IsNullOrEmpty(code) ? null : L(code, packs);

		private IActionResult Home() => RedirectToAction(nameof(Index), new { portal = _portal.PortalKey });

		private IActionResult SignInFirst() => RedirectToAction(nameof(SignIn), new { portal = _portal.PortalKey });

		private void Flash(string message = null, string error = null)
		{
			if (message != null)
				TempData["LearnMessage"] = message;
			if (error != null)
				TempData["LearnError"] = error;
		}

		private IActionResult Limited(object model, string viewName)
		{
			Response.StatusCode = StatusCodes.Status429TooManyRequests;
			ModelState.Clear();
			ModelState.AddModelError(string.Empty, L("Portal.Throttled"));
			return View(viewName, model);
		}

		#endregion Portal, session and helpers

		#region Catalog and account

		[HttpGet("")]
		public async Task<IActionResult> Index()
		{
			if (_learner == null && !_portal.PublicListing)
				return SignInFirst();
			return View("Index", new LmsLearnCatalogModel { Catalog = await _learners.GetCatalogAsync(DepartmentId, _learner, DateTime.UtcNow), Learner = _learner });
		}

		[HttpGet("SignIn")]
		public IActionResult SignIn() => _learner != null ? Home() : View("SignIn", new LmsLearnSignInModel());

		[HttpPost("SignIn")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SignIn(LmsLearnSignInModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnSignInModel();
			var ip = Ip();
			if (await _throttle.IsThrottledAsync(ip, cancellationToken))
				return Limited(model, "SignIn");
			if (!ModelState.IsValid)
				return View("SignIn", model);

			var result = await _learners.SignInAsync(DepartmentId, model.Email, model.Password, cancellationToken);
			if (!result.Succeeded)
			{
				if (result.Error == "lms_xl_signin_failed" || result.Error == "lms_xl_locked")
					await _throttle.RecordFailureAsync(ip, model.Email, cancellationToken);
				model.Password = null;
				ModelState.AddModelError(string.Empty, ErrorText(result.Error));
				return View("SignIn", model);
			}

			await HttpContext.SignInAsync(LmsLearnerAuth.Scheme, LmsLearnerAuth.CreatePrincipal(result.Learner),
				new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
			return RedirectToAction(nameof(My), new { portal = _portal.PortalKey });
		}

		[HttpPost("SignOut")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SignOutLearner(CancellationToken cancellationToken)
		{
			// Security review L2 (2026-11-25): a new security stamp (checked on every request), so a copied cookie dies with the sign-out too.
			if (_learner != null)
				await _learners.SignOutAsync(DepartmentId, _learner.LmsExternalLearnerId, cancellationToken);
			await HttpContext.SignOutAsync(LmsLearnerAuth.Scheme);
			return Home();
		}

		[HttpGet("Register")]
		public async Task<IActionResult> Register(int? classId)
		{
			if (!_portal.SelfRegistration)
				return NotFound();
			if (_learner != null)
				return Home();
			var model = new LmsLearnRegisterModel { ClassId = classId, SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey };
			if (classId.HasValue)
				model.ClassItem = (await _learners.GetCatalogAsync(DepartmentId, null, DateTime.UtcNow)).Classes.FirstOrDefault(c => c.Session.LmsClassSessionId == classId.Value);
			return View("Register", model);
		}

		[HttpPost("Register")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Register(LmsLearnRegisterModel model, CancellationToken cancellationToken)
		{
			if (!_portal.SelfRegistration)
				return NotFound();
			model ??= new LmsLearnRegisterModel();
			model.SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey;
			var ip = Ip();
			// Every post counts against the address, valid or not: a script failing reCAPTCHA over and over is stopped here too.
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Register, ip, cancellationToken) || await _limits.IsLimitedAsync(PublicFormLimit.RegisterCreated, ip, cancellationToken))
				return Limited(model, "Register");
			if (!string.Equals(model.Password, model.ConfirmPassword, StringComparison.Ordinal))
				ModelState.AddModelError(nameof(model.ConfirmPassword), L("Portal.PasswordMismatch"));
			if (!ModelState.IsValid)
				return View("Register", model);

			var result = await _learners.RegisterAsync(DepartmentId, new LmsLearnerRegistration
			{
				Email = model.Email,
				FirstName = model.FirstName,
				LastName = model.LastName,
				Organization = model.Organization,
				Password = model.Password,
				LmsClassSessionId = model.ClassId,
				Language = CultureInfo.CurrentUICulture.Name
			}, cancellationToken);
			if (!result.Succeeded)
			{
				model.Password = model.ConfirmPassword = null;
				ModelState.AddModelError(string.Empty, ErrorText(result.Error));
				return View("Register", model);
			}
			if (result.Learner != null)
				await _limits.RecordAsync(PublicFormLimit.RegisterCreated, ip, cancellationToken);
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Message = L("Portal.RegisterSent") });
		}

		/// <summary>
		/// Security review M2 (2026-11-25): the e-mailed link only shows a Confirm page (a mail gateway or link preview that opens it activates
		/// nothing, the Unsubscribe and /Sign pattern); the button posts the token with antiforgery and that POST activates the account.
		/// </summary>
		[HttpGet("Verify")]
		public async Task<IActionResult> Verify(string token, CancellationToken cancellationToken)
		{
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Error = L("Portal.Throttled") });
			var pending = await _learners.GetVerificationAsync(DepartmentId, token);
			if (!pending.Succeeded)
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Error = ErrorText(pending.Error) });
			return View("Verify", new LmsLearnVerifyModel
			{
				Token = token, RegisteredOn = pending.Learner.CreatedOn, RegisteredName = pending.Learner.Name, FirstName = pending.Learner.FirstName, LastName = pending.Learner.LastName
			});
		}

		/// <summary>
		/// The Confirm POST (follow-up, M1 attacker-first order): the person who opened the e-mailed link chooses the password and confirms the
		/// name; the password typed at registration is not kept. Token-gated (no CAPTCHA), antiforgery, rate limited per address.
		/// </summary>
		[HttpPost("Verify")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify(LmsLearnVerifyModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnVerifyModel();
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Error = L("Portal.Throttled") });
			if (!string.Equals(model.Password, model.ConfirmPassword, StringComparison.Ordinal))
				ModelState.AddModelError(nameof(model.ConfirmPassword), L("Portal.PasswordMismatch"));
			if (!ModelState.IsValid)
				return await VerifyAgainAsync(model, null);
			var result = await _learners.VerifyEmailAsync(DepartmentId, model.Token, model.Password, model.FirstName, model.LastName, cancellationToken);
			if (!result.Succeeded && result.Error != "lms_xl_token_invalid" && result.Error != "lms_xl_portal_full")
				return await VerifyAgainAsync(model, ErrorText(result.Error));
			if (!result.Succeeded)
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Error = ErrorText(result.Error) });
			var classNote = result.Value is string code ? ErrorText(code) : null;
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Message = L("Portal.Verified"), Error = classNote, ShowSignIn = true });
		}

		/// <summary>The Confirm page again with the error; the registration's name and date are read again from the token (never posted).</summary>
		private async Task<IActionResult> VerifyAgainAsync(LmsLearnVerifyModel model, string error)
		{
			var pending = await _learners.GetVerificationAsync(DepartmentId, model.Token);
			if (!pending.Succeeded)
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.Register"), Error = ErrorText(pending.Error) });
			if (error != null)
				ModelState.AddModelError(string.Empty, error);
			model.RegisteredOn = pending.Learner.CreatedOn;
			model.RegisteredName = pending.Learner.Name;
			model.Password = model.ConfirmPassword = null;
			return View("Verify", model);
		}

		[HttpGet("Accept")]
		public async Task<IActionResult> Accept(string token)
		{
			var invite = await _learners.GetInviteAsync(DepartmentId, token);
			if (!invite.Succeeded)
				return View("Message", new LmsLearnMessageModel { Title = L("Portal.ChoosePassword"), Error = ErrorText(invite.Error) });
			return View("SetPassword", new LmsLearnPasswordModel { Token = token, Email = invite.Learner.Email, Invite = true });
		}

		[HttpPost("Accept")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Accept(LmsLearnPasswordModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnPasswordModel();
			model.Invite = true;
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return Limited(model, "SetPassword");
			if (!string.Equals(model.Password, model.ConfirmPassword, StringComparison.Ordinal))
				ModelState.AddModelError(nameof(model.ConfirmPassword), L("Portal.PasswordMismatch"));
			if (!ModelState.IsValid)
				return View("SetPassword", model);
			var result = await _learners.AcceptInviteAsync(DepartmentId, model.Token, model.Password, cancellationToken);
			if (!result.Succeeded)
			{
				ModelState.AddModelError(string.Empty, ErrorText(result.Error));
				return View("SetPassword", model);
			}
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.ChoosePassword"), Message = L("Portal.PasswordSet"), ShowSignIn = true });
		}

		[HttpGet("Forgot")]
		public IActionResult Forgot() => View("Forgot", new LmsLearnForgotModel { SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey });

		[HttpPost("Forgot")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Forgot(LmsLearnForgotModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnForgotModel();
			model.SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey;
			if (!await _limits.TryAcquireAsync(PublicFormLimit.PasswordReset, Ip(), cancellationToken))
				return Limited(model, "Forgot");
			if (!ModelState.IsValid)
				return View("Forgot", model);
			await _learners.BeginPasswordResetAsync(DepartmentId, model.Email, cancellationToken);
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.Forgot"), Message = L("Portal.ForgotSent") });
		}

		/// <summary>
		/// Round 9C: how the learner's details are used (GDPR / PIPEDA-friendly): the department or organization running the portal answers for
		/// them; what is kept and why; how to get a copy or delete the account. Readable before signing in or registering.
		/// </summary>
		[HttpGet("Privacy")]
		public IActionResult Privacy() => View("Privacy");

		/// <summary>Round 7B: the verification or invitation e-mail again (reCAPTCHA, rate limited, the same answer whatever the address).</summary>
		[HttpGet("Resend")]
		public IActionResult Resend() => _learner != null ? Home() : View("Resend", new LmsLearnResendModel { SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey });

		[HttpPost("Resend")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Resend(LmsLearnResendModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnResendModel();
			model.SiteKey = Resgrid.Config.WebConfig.RecaptchaPublicKey;
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return Limited(model, "Resend");
			if (!ModelState.IsValid)
				return View("Resend", model);
			await _learners.ResendAccessEmailAsync(DepartmentId, model.Email, cancellationToken);
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.Resend"), Message = L("Portal.ResendSent") });
		}

		/// <summary>Round 7B: the class-reminder unsubscribe link. The GET only shows the page (a link preview never unsubscribes); the POST does it.</summary>
		[HttpGet("Unsubscribe")]
		public async Task<IActionResult> Unsubscribe(string key, CancellationToken cancellationToken)
		{
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return View("Unsubscribe", new LmsLearnUnsubscribeModel { Error = L("Portal.Throttled") });
			var learner = await _learners.GetByUnsubscribeKeyAsync(DepartmentId, key);
			return View("Unsubscribe", learner == null
				? new LmsLearnUnsubscribeModel { Error = L("lms_xl_token_invalid") }
				: new LmsLearnUnsubscribeModel { Key = key, Done = learner.RemindersOptOut });
		}

		[HttpPost("Unsubscribe")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Unsubscribe(LmsLearnUnsubscribeModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnUnsubscribeModel();
			if (!await _limits.TryAcquireAsync(PublicFormLimit.Invite, Ip(), cancellationToken))
				return View("Unsubscribe", new LmsLearnUnsubscribeModel { Error = L("Portal.Throttled") });
			var result = await _learners.UnsubscribeAsync(DepartmentId, model.Key, cancellationToken);
			return View("Unsubscribe", result.Succeeded ? new LmsLearnUnsubscribeModel { Done = true } : new LmsLearnUnsubscribeModel { Error = ErrorText(result.Error) });
		}

		/// <summary>Round 7B: the signed-in learner's e-mail language and class reminders.</summary>
		[HttpPost("Preferences")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Preferences(string language, bool remindersOff, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var result = await _learners.SetPreferencesAsync(DepartmentId, _learner, language, remindersOff, cancellationToken);
			if (result.Succeeded)
				Flash(L("Portal.PreferencesSaved"));
			else
				Flash(error: ErrorText(result.Error));
			return RedirectToAction(nameof(My), new { portal = _portal.PortalKey });
		}

		[HttpGet("Reset")]
		public IActionResult Reset(string token) => View("SetPassword", new LmsLearnPasswordModel { Token = token });

		[HttpPost("Reset")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Reset(LmsLearnPasswordModel model, CancellationToken cancellationToken)
		{
			model ??= new LmsLearnPasswordModel();
			if (!await _limits.TryAcquireAsync(PublicFormLimit.PasswordReset, Ip(), cancellationToken))
				return Limited(model, "SetPassword");
			if (!string.Equals(model.Password, model.ConfirmPassword, StringComparison.Ordinal))
				ModelState.AddModelError(nameof(model.ConfirmPassword), L("Portal.PasswordMismatch"));
			if (!ModelState.IsValid)
				return View("SetPassword", model);
			var result = await _learners.ResetPasswordAsync(DepartmentId, model.Token, model.Password, cancellationToken);
			if (!result.Succeeded)
			{
				ModelState.AddModelError(string.Empty, ErrorText(result.Error));
				return View("SetPassword", model);
			}
			await HttpContext.SignOutAsync(LmsLearnerAuth.Scheme);
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.ChoosePassword"), Message = L("Portal.PasswordSet"), ShowSignIn = true });
		}

		#endregion Catalog and account

		#region My learning, classes and courses

		[HttpGet("My")]
		public async Task<IActionResult> My()
		{
			if (_learner == null)
				return SignInFirst();
			return View("My", new LmsLearnMyModel
			{
				Learner = _learner,
				Mine = await _learners.GetMyLearningAsync(DepartmentId, _learner, DateTime.UtcNow),
				Languages = Resgrid.Localization.SupportedLocales.GetSupportedCultures().OrderBy(c => c, StringComparer.Ordinal).ToList()
			});
		}

		[HttpPost("Class/{id:int}/Register")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RegisterClass(int id, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var result = await _learners.RegisterForClassAsync(DepartmentId, _learner, id, cancellationToken);
			if (result.Succeeded)
				Flash(L("Portal.RegisteredMessage"));
			else
				Flash(error: ErrorText(result.Error));
			return RedirectToAction(nameof(My), new { portal = _portal.PortalKey });
		}

		[HttpPost("Class/{id:int}/Withdraw")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> WithdrawClass(int id, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var result = await _learners.WithdrawFromClassAsync(DepartmentId, _learner, id, cancellationToken);
			if (result.Succeeded)
				Flash(L("Portal.WithdrawnMessage"));
			else
				Flash(error: ErrorText(result.Error));
			return RedirectToAction(nameof(My), new { portal = _portal.PortalKey });
		}

		[HttpPost("Course/{id:int}/Enroll")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EnrollCourse(int id, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var result = await _learners.EnrollSelfInCourseAsync(DepartmentId, _learner, id, cancellationToken);
			if (!result.Succeeded)
			{
				Flash(error: ErrorText(result.Error));
				return Home();
			}
			Flash(L("Portal.EnrolledMessage"));
			var enrollment = result.Value as LmsEnrollment;
			return enrollment == null ? RedirectToAction(nameof(My), new { portal = _portal.PortalKey }) : RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id = enrollment.LmsEnrollmentId });
		}

		#endregion My learning, classes and courses

		#region Player

		[HttpGet("Play/{id:int}")]
		public async Task<IActionResult> Play(int id, int? lesson)
		{
			if (_learner == null)
				return SignInFirst();
			var player = await _runtime.GetPlayerAsync(DepartmentId, _learner.LearnerUserId, id, lesson);
			if (player.Error == LmsErrors.NotFound || player.Outline == null)
				return NotFound();
			var model = new LmsLearnPlayerModel
			{
				Player = player,
				Error = player.Error == LmsErrors.None ? null : LessonError(player.Error),
				SurveyQuestions = player.Lesson?.Kind == (int)LmsLessonKinds.Survey ? LmsSurveyQuestion.Parse(player.Lesson.SurveyJson) : new List<LmsSurveyQuestion>()
			};
			return View("Play", model);
		}

		/// <summary>Seat time and video progress (JSON heartbeat); the server caps the time by its own clock.</summary>
		[HttpPost("Play/{id:int}/Progress")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Progress(int id, int lesson, int seconds, int? position, int? watched, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return Unauthorized();
			var result = await _runtime.SaveProgressAsync(DepartmentId, _learner.LearnerUserId, id, lesson,
				new LmsProgressInput { Seconds = seconds, VideoPositionSeconds = position, WatchedPercent = watched }, cancellationToken);
			return Json(new { success = result.Succeeded });
		}

		[HttpPost("Play/{id:int}/Complete")]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxLessonFileBytes + 1024 * 1024)]
		public async Task<IActionResult> Complete(int id, int lesson, string responseText, IFormFile file, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var input = new LmsLessonCompletionInput { ResponseText = responseText };
			if (file != null && file.Length > 0)
			{
				if (file.Length > LmsLessonFile.MaxBytes)
				{
					Flash(error: LessonError(LmsErrors.FileTooLarge));
					return RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id, lesson });
				}
				using var buffer = new MemoryStream();
				await file.CopyToAsync(buffer, cancellationToken);
				input.FileData = buffer.ToArray();
				input.FileName = file.FileName;
				input.ContentType = file.ContentType;
			}
			var result = await _runtime.CompleteLessonAsync(DepartmentId, _learner.LearnerUserId, id, lesson, input, cancellationToken);
			if (!result.Succeeded)
			{
				Flash(error: LessonError(result.Error));
				return RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id, lesson });
			}
			Flash(L(result.Count > 0 ? "Portal.CourseDone" : "Portal.LessonDone"));
			return RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id });
		}

		[HttpPost("Play/{id:int}/Survey")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Survey(int id, int lesson, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var answers = Request.Form.Keys.Where(k => k.StartsWith("q_", StringComparison.Ordinal))
				.ToDictionary(k => k.Substring(2), k => Request.Form[k].ToString(), StringComparer.Ordinal);
			var result = await _runtime.SubmitSurveyAsync(DepartmentId, _learner.LearnerUserId, id, lesson, answers, cancellationToken);
			if (!result.Succeeded)
				Flash(error: LessonError(result.Error));
			else
				Flash(L("Portal.LessonDone"));
			return RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id, lesson = result.Succeeded ? (int?)null : lesson });
		}

		[HttpGet("Video/{id:int}/{lesson:int}")]
		public async Task<IActionResult> Video(int id, int lesson)
		{
			if (_learner == null)
				return SignInFirst();
			var player = await _runtime.GetPlayerAsync(DepartmentId, _learner.LearnerUserId, id, lesson);
			if (player.Error != LmsErrors.None || player.Lesson?.LmsLessonId != lesson || !player.HostedVideoPlayable || player.Media == null)
				return NotFound();
			var url = await _media.GetSignedMp4UrlAsync((await _gate.GetGateAsync(DepartmentId)).For(player.Enrollment?.LmsCourseId ?? 0), player.Media, DateTime.UtcNow);
			return url == null ? NotFound() : Redirect(url);
		}

		[HttpPost("Play/{id:int}/Test")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> StartTest(int id, int lesson, bool attested, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var attempt = await _assessments.StartAttemptAsync(DepartmentId, _learner.LearnerUserId, id, lesson, attested, Ip(), Request.Headers["User-Agent"].ToString(),
				CultureInfo.CurrentUICulture.Name, cancellationToken);
			if (attempt.Error != LmsErrors.None || attempt.Attempt == null)
			{
				Flash(error: LessonError(attempt.Error));
				return RedirectToAction(nameof(Play), new { portal = _portal.PortalKey, id, lesson });
			}
			return RedirectToAction(nameof(Test), new { portal = _portal.PortalKey, id = attempt.Attempt.LmsAssessmentAttemptId });
		}

		[HttpGet("Test/{id:int}")]
		public async Task<IActionResult> Test(int id)
		{
			if (_learner == null)
				return SignInFirst();
			var attempt = await _assessments.GetAttemptAsync(DepartmentId, _learner.LearnerUserId, id);
			if (attempt.Error == LmsErrors.NotFound || attempt.Attempt == null)
				return NotFound();
			return View("Test", new LmsLearnTestModel { Attempt = attempt });
		}

		[HttpPost("Test/{id:int}")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Test(int id, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			var answers = new List<LmsAttemptAnswer>();
			foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("a_", StringComparison.Ordinal)))
			{
				var parts = key.Split('_');
				if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var questionId))
					continue;
				var answer = answers.FirstOrDefault(a => a.QuestionId == questionId);
				if (answer == null)
					answers.Add(answer = new LmsAttemptAnswer { QuestionId = questionId });
				if (parts.Length == 3 && parts[2] == "text")
					answer.Text = Request.Form[key].ToString();
				else if (parts.Length == 3 && parts[2] == "order")
					answer.Keys = Request.Form[key].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList();
				else
					answer.Keys.AddRange(Request.Form[key].Where(v => !string.IsNullOrWhiteSpace(v)));
			}
			var result = await _assessments.SubmitAttemptAsync(DepartmentId, _learner.LearnerUserId, id, answers, cancellationToken);
			if (result.Error == LmsErrors.NotFound || result.Attempt == null)
				return NotFound();
			return View("TestResult", new LmsLearnTestResultModel { Result = result, Error = result.Error == LmsErrors.None || result.Error == LmsErrors.AlreadySubmitted ? null : LessonError(result.Error) });
		}

		private string LessonError(LmsErrors error) => error switch
		{
			LmsErrors.SeatTimeNotMet => L("Portal.Err.SeatTime"),
			LmsErrors.RequirementNotMet => L("Portal.Err.Requirement"),
			LmsErrors.ResponseRequired => L("Portal.Err.Response"),
			LmsErrors.LessonLocked => L("Portal.Err.Locked"),
			LmsErrors.FileTooLarge => L("Portal.Err.FileTooLarge"),
			LmsErrors.AddonRequired or LmsErrors.LapseWindowEnded => L("lms_xl_addon_inactive"),
			_ => L("Portal.Err.Lesson")
		};

		#endregion Player

		#region Certificates and my data (GDPR)

		[HttpGet("Certificate/{id:int}")]
		public async Task<IActionResult> Certificate(int id)
		{
			if (_learner == null)
				return SignInFirst();
			var document = await _learners.GetCertificateAsync(DepartmentId, _learner, id);
			if (document == null)
				return NotFound();
			var culture = CultureInfo.CurrentUICulture;
			document.Context.Culture = culture.Name;
			document.Context.RightToLeft = culture.TextInfo.IsRightToLeft;
			document.Context.Labels = ((IEnumerable<string>)Resgrid.Services.TrainingDocuments.LabelKeys ?? Array.Empty<string>()).Where(k => !string.IsNullOrEmpty(k))
				.Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => _trainingLocalizer?[k]?.Value ?? k, StringComparer.Ordinal);
			try
			{
				// The verification code (LMS): printed with the public verify URL, which names the learner.
				var code = document.VerificationCode ?? await _lmsTraining.EnsureVerificationCodeAsync(DepartmentId, id);
				if (!string.IsNullOrWhiteSpace(code))
				{
					document.VerificationCode = code;
					document.VerificationUrl = (Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/verify/certificate/" + Uri.EscapeDataString(code);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A learner certificate's verification code could not be issued.");
				document.VerificationCode = null;
				document.VerificationUrl = null;
			}

			var html = Resgrid.Services.TrainingDocuments.CertificateHtml(document);
			try
			{
				return File(_pdf.ConvertHtmlToPdf(html), "application/pdf", "certificate-" + id.ToString(CultureInfo.InvariantCulture) + ".pdf");
			}
			catch (Exception ex)
			{
				// The PDF engine is down: the printable page still works.
				Logging.LogException(ex, "A learner certificate PDF could not be generated.");
				return Content(html, "text/html", Encoding.UTF8);
			}
		}

		[HttpGet("MyData")]
		public async Task<IActionResult> MyData()
		{
			if (_learner == null)
				return SignInFirst();
			var json = await _learners.ExportAsync(DepartmentId, _learner.LmsExternalLearnerId);
			return json == null ? NotFound() : File(Encoding.UTF8.GetBytes(json), "application/json", "my-training-data.json");
		}

		[HttpPost("MyData/Erase")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EraseMine(string password, CancellationToken cancellationToken)
		{
			if (_learner == null)
				return SignInFirst();
			// The learner proves it is them with the password (learners have no second factor; step-up does not apply).
			var check = await _learners.SignInAsync(DepartmentId, _learner.Email, password, cancellationToken);
			if (!check.Succeeded || check.Learner?.LmsExternalLearnerId != _learner.LmsExternalLearnerId)
			{
				Flash(error: ErrorText(check.Error ?? "lms_xl_signin_failed"));
				return RedirectToAction(nameof(My), new { portal = _portal.PortalKey });
			}
			await _learners.EraseAsync(DepartmentId, _learner.LmsExternalLearnerId, null, cancellationToken);
			await HttpContext.SignOutAsync(LmsLearnerAuth.Scheme);
			return View("Message", new LmsLearnMessageModel { Title = L("Portal.MyData"), Message = L("Portal.EraseMineDone") });
		}

		#endregion Certificates and my data (GDPR)
	}
}
