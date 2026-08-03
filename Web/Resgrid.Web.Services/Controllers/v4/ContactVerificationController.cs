using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.ContactVerification;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Endpoints for sending and confirming contact-method verification codes.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ContactVerificationController : V4AuthenticatedApiControllerbase
	{
		private readonly IContactVerificationService _contactVerificationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		/// <summary>
		/// Initializes a new instance of <see cref="ContactVerificationController"/>.
		/// </summary>
		public ContactVerificationController(
			IContactVerificationService contactVerificationService,
			IDepartmentSettingsService departmentSettingsService)
		{
			_contactVerificationService = contactVerificationService;
			_departmentSettingsService = departmentSettingsService;
		}

		/// <summary>
		/// Generates and sends a verification code to the specified contact method.
		/// </summary>
		[HttpPost("SendVerificationCode")]
		[ProducesResponseType(typeof(SendVerificationCodeResult), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		public async Task<ActionResult<SendVerificationCodeResult>> SendVerificationCode(
			[FromBody] SendVerificationCodeInput model,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			ContactVerificationSendStatus sendStatus;

			switch (model.Type)
			{
				case ContactVerificationType.Email:
					sendStatus = await _contactVerificationService.SendEmailVerificationCodeAsync(UserId, DepartmentId, cancellationToken);
					break;

				case ContactVerificationType.MobileNumber:
					var mobileDepNumber = model.DepartmentNumber
						?? await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
					sendStatus = await _contactVerificationService.SendMobileVerificationCodeAsync(UserId, DepartmentId, mobileDepNumber, cancellationToken);
					break;

				case ContactVerificationType.HomeNumber:
					var homeDepNumber = model.DepartmentNumber
						?? await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
					sendStatus = await _contactVerificationService.SendHomeVerificationCodeAsync(UserId, DepartmentId, homeDepNumber, cancellationToken);
					break;

				default:
					return BadRequest();
			}

			if (sendStatus != ContactVerificationSendStatus.Sent)
			{
				return Ok(new SendVerificationCodeResult
				{
					Successful = false,
					ErrorCode = sendStatus.ToString(),
					ErrorMessage = GetSendErrorMessage(sendStatus)
				});
			}

			return Ok(new SendVerificationCodeResult { Successful = true });
		}

		private static string GetSendErrorMessage(ContactVerificationSendStatus sendStatus)
		{
			switch (sendStatus)
			{
				case ContactVerificationSendStatus.ContactNotConfigured:
					return "The selected contact method is not configured.";
				case ContactVerificationSendStatus.InvalidContact:
					return "The selected contact method is not valid for verification delivery.";
				case ContactVerificationSendStatus.RateLimited:
					return "Too many verification attempts. Please try again later.";
				default:
					return "Unable to deliver the verification code. Please try again.";
			}
		}

		/// <summary>
		/// Confirms a verification code previously sent to the specified contact method.
		/// </summary>
		[HttpPost("ConfirmVerificationCode")]
		[ProducesResponseType(typeof(ConfirmVerificationCodeResult), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<ConfirmVerificationCodeResult>> ConfirmVerificationCode(
			[FromBody] ConfirmVerificationCodeInput model,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			// Use the X-Forwarded-For aware helper so the audit log records the real client IP
			// rather than the reverse-proxy / load-balancer address. IP resolution can throw when no
			// address is resolvable; fall back to empty so verification still proceeds (the IP is only
			// recorded for auditing).
			string ipAddress;
			try
			{
				ipAddress = IpAddressHelper.GetRequestIP(Request, true);
			}
			catch (System.Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "ContactVerification.ConfirmVerificationCode: unable to resolve client IP; continuing without it.");
				ipAddress = string.Empty;
			}

			bool confirmed = await _contactVerificationService.ConfirmVerificationCodeAsync(
				UserId, DepartmentId, model.Type, model.Code, ipAddress, cancellationToken);

			if (!confirmed)
				return Ok(new ConfirmVerificationCodeResult { Successful = false, ErrorMessage = "Verification failed. The code may be incorrect, expired, or you have exceeded the daily attempt limit." });

			return Ok(new ConfirmVerificationCodeResult { Successful = true });
		}
	}
}




