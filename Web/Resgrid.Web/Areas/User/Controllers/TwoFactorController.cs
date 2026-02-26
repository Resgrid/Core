using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using QRCoder;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public class TwoFactorController : SecureBaseController
	{
		private readonly UserManager<IdentityUser> _userManager;
		private readonly SignInManager<IdentityUser> _signInManager;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly UrlEncoder _urlEncoder;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> _localizer;

		private const string StepUpSessionKey = "Resgrid2FAVerifiedAt";

		public TwoFactorController(
			UserManager<IdentityUser> userManager,
			SignInManager<IdentityUser> signInManager,
			ISystemAuditsService systemAuditsService,
			UrlEncoder urlEncoder,
			IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> localizer)
		{
			_userManager = userManager;
			_signInManager = signInManager;
			_systemAuditsService = systemAuditsService;
			_urlEncoder = urlEncoder;
			_localizer = localizer;
		}

		// ── Index ─────────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			var codesLeft = await _userManager.CountRecoveryCodesAsync(user);

			var model = new TwoFactorIndexViewModel
			{
				HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null,
				Is2FAEnabled = await _userManager.GetTwoFactorEnabledAsync(user),
				RecoveryCodesLeft = codesLeft,
				RecoveryCodeWarning = codesLeft <= TwoFactorConfig.RecoveryCodeWarningThreshold
			};

			return View(model);
		}

		// ── Enable 2FA ────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Enable2FA()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			var key = await _userManager.GetAuthenticatorKeyAsync(user);
			if (string.IsNullOrEmpty(key))
			{
				await _userManager.ResetAuthenticatorKeyAsync(user);
				key = await _userManager.GetAuthenticatorKeyAsync(user);
			}

			var formattedKey = FormatKey(key);
			var email = await _userManager.GetEmailAsync(user);
			var uri = GenerateQrCodeUri(email, key);

			var model = new EnableAuthenticatorViewModel
			{
				SharedKey = formattedKey,
				AuthenticatorUri = uri,
				QrCodeDataUrl = GenerateQrCodeDataUrl(uri)
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Enable2FA(EnableAuthenticatorViewModel model, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!ModelState.IsValid)
			{
				// Re-populate QR data
				var key2 = await _userManager.GetAuthenticatorKeyAsync(user);
				model.SharedKey = FormatKey(key2);
				model.AuthenticatorUri = GenerateQrCodeUri(await _userManager.GetEmailAsync(user), key2);
				model.QrCodeDataUrl = GenerateQrCodeDataUrl(model.AuthenticatorUri);
				return View(model);
			}

			var verificationCode = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var isValid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

			if (!isValid)
			{
				ModelState.AddModelError(nameof(model.Code), _localizer["InvalidCodeError"]);
				var key2 = await _userManager.GetAuthenticatorKeyAsync(user);
				model.SharedKey = FormatKey(key2);
				model.AuthenticatorUri = GenerateQrCodeUri(await _userManager.GetEmailAsync(user), key2);
				model.QrCodeDataUrl = GenerateQrCodeDataUrl(model.AuthenticatorUri);
				return View(model);
			}

			await _userManager.SetTwoFactorEnabledAsync(user, true);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorEnabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"2FA enabled via web. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount);

			TempData["RecoveryCodes"] = recoveryCodes.ToArray();
			TempData["StatusMessage"] = "Your authenticator app has been verified. Save your recovery codes below.";

			return RedirectToAction(nameof(ShowRecoveryCodes));
		}

		// ── Recovery Codes ────────────────────────────────────────────────────────

		[HttpGet]
		public IActionResult ShowRecoveryCodes()
		{
			var codes = TempData["RecoveryCodes"] as string[];
			if (codes == null || codes.Length == 0)
				return RedirectToAction(nameof(Index));

			return View(new ShowRecoveryCodesViewModel { RecoveryCodes = codes });
		}

		[HttpGet]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> ViewRecoveryCodes()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			// Recovery codes are hashed; we can only show count — regenerate to view
			return RedirectToAction(nameof(RegenerateRecoveryCodes));
		}

		[HttpGet]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> RegenerateRecoveryCodes()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount);
			TempData["RecoveryCodes"] = recoveryCodes.ToArray();
			TempData["StatusMessage"] = "Your recovery codes have been regenerated.";

			return RedirectToAction(nameof(ShowRecoveryCodes));
		}

		// ── Disable 2FA ───────────────────────────────────────────────────────────

		[HttpGet]
		public IActionResult Disable2FA() => View(new Disable2FAViewModel());

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Disable2FA(Disable2FAViewModel model, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!ModelState.IsValid) return View(model);

			var verificationCode = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var isValid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

			if (!isValid)
			{
				ModelState.AddModelError(nameof(model.Code), _localizer["InvalidCodeError"]);
				return View(model);
			}

			await _userManager.SetTwoFactorEnabledAsync(user, false);
			await _userManager.ResetAuthenticatorKeyAsync(user);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorDisabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"2FA disabled via web. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			TempData["StatusMessage"] = "Two-factor authentication has been disabled.";
			return RedirectToAction(nameof(Index));
		}

		// ── Step-Up Verification ──────────────────────────────────────────────────

		[HttpGet]
		[AllowAnonymous]
		public IActionResult Verify2FA(string returnUrl = null)
		{
			return View(new StepUpVerifyViewModel { ReturnUrl = returnUrl });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowAnonymous]
		public async Task<IActionResult> Verify2FA(StepUpVerifyViewModel model, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid) return View(model);

			var user = await _userManager.GetUserAsync(User);
			if (user == null) return Challenge();

			var verificationCode = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var isValid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

			if (!isValid)
			{
				ModelState.AddModelError(nameof(model.Code), "Verification code is invalid.");
				return View(model);
			}

			// Stamp session with verified-at time
			HttpContext.Session.SetString(StepUpSessionKey, DateTime.UtcNow.ToString("O"));

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Step-up 2FA verified. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
				return Redirect(model.ReturnUrl);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		// ── Helpers ───────────────────────────────────────────────────────────────

		private static string FormatKey(string unformattedKey)
		{
			var result = new StringBuilder();
			int currentPosition = 0;
			while (currentPosition + 4 < unformattedKey.Length)
			{
				result.Append(unformattedKey.Substring(currentPosition, 4)).Append(' ');
				currentPosition += 4;
			}
			if (currentPosition < unformattedKey.Length)
				result.Append(unformattedKey.Substring(currentPosition));
			return result.ToString().ToLowerInvariant();
		}

		private string GenerateQrCodeUri(string email, string unformattedKey)
		{
			return $"otpauth://totp/{_urlEncoder.Encode(TwoFactorConfig.TotpIssuerName)}:{_urlEncoder.Encode(email)}?secret={unformattedKey}&issuer={_urlEncoder.Encode(TwoFactorConfig.TotpIssuerName)}&digits=6";
		}

		private static string GenerateQrCodeDataUrl(string uri)
		{
			using var qrGenerator = new QRCodeGenerator();
			using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
			using var qrCode = new PngByteQRCode(qrCodeData);
			var bytes = qrCode.GetGraphic(5);
			return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
		}
	}
}




