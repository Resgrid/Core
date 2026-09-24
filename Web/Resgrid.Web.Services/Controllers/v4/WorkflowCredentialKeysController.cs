using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Public signing keys of OAuth2 private_key_jwt workflow credentials (SMART Backend Services). An EHR registers this
	/// JWKS URL for the Resgrid client and fetches it to verify the client assertions Resgrid signs. Unauthenticated by
	/// design and PUBLIC KEYS ONLY: the private keys never leave the credential's encrypted data.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/workflow-credentials")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class WorkflowCredentialKeysController : V4AuthenticatedApiControllerbase
	{
		private readonly IWorkflowService _workflowService;

		public WorkflowCredentialKeysController(IWorkflowService workflowService)
		{
			_workflowService = workflowService;
		}

		/// <summary>
		/// The credential's JWKS: its current signing key plus any key rotated out within the overlap window
		/// (DataProtectionConfig.WorkflowJwksOverlapDays). 404 for anything that is not a private_key_jwt credential.
		/// </summary>
		[HttpGet("{credentialId}/jwks.json")]
		[AllowAnonymous]
		[Produces("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> Jwks(string credentialId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(credentialId) || credentialId.Length > 128)
				return NotFound();

			var credential = await _workflowService.GetCredentialByIdAsync(credentialId, cancellationToken);
			if (credential == null || credential.CredentialType != (int)WorkflowCredentialType.OAuth2ClientCredentials ||
				string.IsNullOrWhiteSpace(credential.PublicJwks))
				return NotFound();

			// Short cache: a rotation must reach the EHR well inside the overlap window.
			Response.Headers["Cache-Control"] = "public, max-age=300";
			return Content(WorkflowJwtKeys.BuildJwks(credential.PublicJwks, DateTime.UtcNow, DataProtectionConfig.WorkflowJwksOverlapDays),
				"application/json");
		}
	}
}
