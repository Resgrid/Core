using System;
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
			model.Voice = await _voiceService.GetVoiceSettingsForDepartmentAsync(DepartmentId);

			if (model.Voice == null)
			{
				model.Voice = new DepartmentVoice();
				model.Voice.Channels = new List<DepartmentVoiceChannel>();
			}

			model.Audios = await _voiceService.GetDepartmentAudiosByDepartmentIdAsync(DepartmentId);

			if (model.Audios == null)
				model.Audios = new List<DepartmentAudio>();

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
				var voiceRecord = await _voiceService.GetOrCreateDepartmentVoiceRecordAsync(department);
				var channel = await _voiceService.SaveChannelToVoipProviderAsync(department, model.ChannelName, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> Edit(string id)
		{
			var model = new NewChannelModel();

			if (String.IsNullOrWhiteSpace(id))
				Unauthorized();

			var voiceChannel = await _voiceService.GetVoiceChannelByIdAsync(id);

			if (voiceChannel == null)
				Unauthorized();

			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (!canUseVoice)
				Unauthorized();

			if (voiceChannel.DepartmentId != DepartmentId)
				Unauthorized();

			model.Id = voiceChannel.DepartmentVoiceChannelId;
			model.ChannelName = voiceChannel.Name;
			model.IsDefault = voiceChannel.IsDefault;

			return View(model);
		}
		
		[HttpPost]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> Edit(NewChannelModel model, CancellationToken cancellationToken)
		{
			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (!canUseVoice)
				Unauthorized();

			if (ModelState.IsValid)
			{
				var voiceChannel = await _voiceService.GetVoiceChannelByIdAsync(model.Id);

				if (voiceChannel == null)
					Unauthorized();

				if (voiceChannel.DepartmentId != DepartmentId)
					Unauthorized();

				voiceChannel.Name = model.ChannelName;
				voiceChannel.IsDefault = model.IsDefault;

				await _voiceService.SaveOrUpdateVoiceChannelAsync(voiceChannel, DepartmentId, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> Resync()
		{
			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (!canUseVoice)
				Unauthorized();

			var result = await _voiceService.InitializeDepartmentUsersWithVoipProviderAsync(DepartmentId);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Delete)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var canUseVoice = await _voiceService.CanDepartmentUseVoiceAsync(DepartmentId);
			
			if (!canUseVoice)
				Unauthorized();

			var channel = await _voiceService.GetDepartmentVoiceChannelByIdAsync(id);
			if (channel != null && channel.DepartmentId == DepartmentId)
			{
				var result = await _voiceService.DeleteDepartmentVoiceChannelAsync(channel, cancellationToken);
			}

			return RedirectToAction("Index");
		}


		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> NewAudio()
		{
			var model = new NewAudioStreamModel();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> NewAudio(NewAudioStreamModel model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

				DepartmentAudio audio = new DepartmentAudio();
				audio.DepartmentId = DepartmentId;
				audio.AddedOn = DateTime.UtcNow;
				audio.AddedByUserId = UserId;
				audio.Name = model.Name;
				audio.Data = model.Url;
				audio.DepartmentAudioType = 1;

				var savedAudio = await _voiceService.SaveDepartmentAudioAsync(audio, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> EditAudio(string id)
		{
			var model = new NewAudioStreamModel();

			if (String.IsNullOrWhiteSpace(id))
				Unauthorized();

			var audio = await _voiceService.GetDepartmentAudioByIdAsync(id);

			if (audio == null)
				Unauthorized();

			if (audio.DepartmentId != DepartmentId)
				Unauthorized();

			model.Id = audio.DepartmentAudioId;
			model.Name = audio.Name;
			model.Url = audio.Data;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Voice_Create)]
		public async Task<IActionResult> EditAudio(NewAudioStreamModel model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var audio = await _voiceService.GetDepartmentAudioByIdAsync(model.Id);

				if (audio == null)
					Unauthorized();

				if (audio.DepartmentId != DepartmentId)
					Unauthorized();

				audio.Name = model.Name;
				audio.Data = model.Url;

				await _voiceService.SaveDepartmentAudioAsync(audio, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Voice_Delete)]
		public async Task<IActionResult> DeleteAudio(string id, CancellationToken cancellationToken)
		{
			var audio = await _voiceService.GetDepartmentAudioByIdAsync(id);
			if (audio != null && audio.DepartmentId == DepartmentId)
			{
				var result = await _voiceService.DeleteDepartmentAudioAsync(audio, cancellationToken);
			}

			return RedirectToAction("Index");
		}
	}
}
