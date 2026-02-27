using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
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

			bool sent;

			switch (model.Type)
			{
				case ContactVerificationType.Email:
					sent = await _contactVerificationService.SendEmailVerificationCodeAsync(UserId, DepartmentId, cancellationToken);
					break;

				case ContactVerificationType.MobileNumber:
					var mobileDepNumber = model.DepartmentNumber
						?? await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
					sent = await _contactVerificationService.SendMobileVerificationCodeAsync(UserId, DepartmentId, mobileDepNumber, cancellationToken);
					break;

				case ContactVerificationType.HomeNumber:
					var homeDepNumber = model.DepartmentNumber
						?? await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
					sent = await _contactVerificationService.SendHomeVerificationCodeAsync(UserId, DepartmentId, homeDepNumber, cancellationToken);
					break;

				default:
					return BadRequest();
			}

			if (!sent)
				return Ok(new SendVerificationCodeResult { Successful = false, ErrorMessage = "Unable to send verification code. You may have exceeded the rate limit or the contact method is not set." });

			return Ok(new SendVerificationCodeResult { Successful = true });
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

			string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

			bool confirmed = await _contactVerificationService.ConfirmVerificationCodeAsync(
				UserId, DepartmentId, model.Type, model.Code, ipAddress, cancellationToken);

			if (!confirmed)
				return Ok(new ConfirmVerificationCodeResult { Successful = false, ErrorMessage = "Verification failed. The code may be incorrect, expired, or you have exceeded the daily attempt limit." });

			return Ok(new ConfirmVerificationCodeResult { Successful = true });
		}
	}
}




