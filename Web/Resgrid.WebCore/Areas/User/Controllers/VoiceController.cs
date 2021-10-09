using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.WebCore.Areas.User.Models.Voice;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class VoiceController : SecureBaseController
	{
		private readonly IVoiceService _voiceService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;

		public VoiceController(IVoiceService voiceService, IAuthorizationService authorizationService, IDepartmentsService departmentsService)
		{
			_voiceService = voiceService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_View)]
		public async Task<IActionResult> Index()
		{
			var model = new VoiceIndexModel();
			model.CanUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (model.Voice == null)
			{
				model.Voice = new DepartmentVoice();
				model.Voice.Channels = new List<DepartmentVoiceChannel>();
			}

			//var channels = await _voiceService.GetDepartmentVoiceChannels();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewChannelModel();
			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (!canUseVoice)
				Unauthorized();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> New(NewChannelModel model, CancellationToken cancellationToken)
		{
			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (!canUseVoice)
				Unauthorized();

			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

				var channel = await _voiceService.SaveChannelToVoipProviderAsync(department, model.ChannelName, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}
	}
}
