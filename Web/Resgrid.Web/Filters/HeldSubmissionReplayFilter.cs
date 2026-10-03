using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model.Providers;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Replays a submission a verification gate held (<see cref="StepUpFormReplay"/>): when the resume page posts a held
	/// submission's id back to the action it was made to, this puts the held fields and files in place as the request's form
	/// before model binding, so the action runs on exactly what the user submitted.
	/// </summary>
	/// <remarks>
	/// Global rather than on the gated actions, so no gate (2FA step-up, password re-confirmation, or one added later) can hold a
	/// submission whose replay then reaches an action that does not restore it: a post carrying the id is either restored or
	/// refused, never bound bare. Only the user, session and active department that made it may replay it, once, to the address
	/// it was made to. Posts that cannot be a replay (<see cref="StepUpFormReplay.MayBeReplay"/>) are passed through unread.
	/// </remarks>
	public sealed class HeldSubmissionReplayFilter : IAsyncResourceFilter
	{
		private readonly ICacheProvider _cacheProvider;
		private readonly IDataProtectionProvider _dataProtection;
		private readonly UserManager<IdentityUser> _userManager;

		public HeldSubmissionReplayFilter(ICacheProvider cacheProvider, IDataProtectionProvider dataProtection, UserManager<IdentityUser> userManager)
		{
			_cacheProvider = cacheProvider;
			_dataProtection = dataProtection;
			_userManager = userManager;
		}

		public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
		{
			var httpContext = context.HttpContext;
			if (!StepUpFormReplay.MayBeReplay(httpContext))
			{
				await next();
				return;
			}

			// Buffered and rewound, so an action that reads the raw body still sees all of it.
			var request = httpContext.Request;
			request.EnableBuffering();
			var form = await request.ReadFormAsync(httpContext.RequestAborted);
			request.Body.Position = 0;

			var heldId = form[StepUpFormReplay.FieldName].ToString();
			if (string.IsNullOrEmpty(heldId))
			{
				await next();
				return;
			}

			var held = await StepUpFormReplay.PeekAsync(_cacheProvider, _dataProtection, heldId);
			var owned = StepUpFormReplay.BelongsTo(held, _userManager.GetUserId(httpContext.User),
				MfaEvidenceSession.KeyFor(httpContext.User, httpContext), StepUpFormReplay.ActiveDepartmentOf(httpContext.User));
			var state = owned && StepUpFormReplay.IsFor(held, request)
				? await StepUpFormReplay.ClaimAsync(_cacheProvider, _dataProtection, heldId)
				: null;

			if (state == null)
			{
				// Never run the action on the bare replay post: its form holds nothing but the id, and binding it would save blanks.
				if (StepUpFormReplay.IsScriptRequest(request))
					context.Result = new JsonResult(new { success = false, error = "step_up_replay_unavailable" }) { StatusCode = StatusCodes.Status409Conflict };
				else
					context.Result = new RedirectResult(StepUpFormReplay.ResumeUrl(request.PathBase, heldId, owned ? held.Back : null));
				return;
			}

			httpContext.Items[StepUpFormReplay.HttpItemKey] = state;
			StepUpFormReplay.UseAsRequestForm(httpContext, StepUpFormReplay.Restore(state, form["__RequestVerificationToken"]));

			await next();
		}
	}
}
