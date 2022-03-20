using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Connect;
using Resgrid.Web.Helpers;
using Resgrid.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ConnectController : SecureBaseController
	{
		private readonly IDepartmentProfileService _departmentProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAddressService _addressService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IOptions<AppOptions> _appOptionsAccessor;

		public ConnectController(IDepartmentProfileService departmentProfileService, IDepartmentsService departmentsService, IAddressService addressService, 
			IGeoLocationProvider geoLocationProvider, IOptions<AppOptions> appOptionsAccessor)
		{
			_departmentProfileService = departmentProfileService;
			_departmentsService = departmentsService;
			_addressService = addressService;
			_geoLocationProvider = geoLocationProvider;
			_appOptionsAccessor = appOptionsAccessor;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_View)]
		public async Task<IActionResult> Index()
		{
			var model = new IndexView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);
			model.ImageUrl = $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/Avatars/Get?id={model.Profile.DepartmentId}&type=1";

			var posts = _departmentProfileService.GetArticlesForDepartment(model.Profile.DepartmentProfileId);
			var visiblePosts = _departmentProfileService.GetVisibleArticlesForDepartment(model.Profile.DepartmentProfileId);

			if (visiblePosts != null && visiblePosts.Any())
				model.VisiblePosts = visiblePosts.Count;

			if (posts.Any())
				model.Posts = posts.Skip(Math.Max(0, posts.Count() - 3)).ToList();
			else
				model.Posts = new List<DepartmentProfileArticle>();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_Update)]
		public async Task<IActionResult> Profile()
		{
			var model = new ProfileView();

			model.ApiUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.ImageUrl = $"{model.ApiUrl}/api/v3/Avatars/Get?id={model.Department.DepartmentId}&type=1";


			var profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);

			if (profile != null)
			{
				model.Profile = profile;

				if (model.Profile.Address != null)
				{
					model.Address1 = model.Profile.Address.Address1;
					model.City = model.Profile.Address.City;
					model.Country = model.Profile.Address.Country;
					model.PostalCode = model.Profile.Address.PostalCode;
					model.State = model.Profile.Address.State;
				}
			}
			else
				model.Profile = new DepartmentProfile();


			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Connect_Update)]
		public async Task<IActionResult> Profile(ProfileView model)
		{
			model.ApiUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.ImageUrl = $"{model.ApiUrl}/api/v3/Avatars/Get?id={model.Department.DepartmentId}&type=1";

			if (ModelState.IsValid)
			{
				var profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);
				profile.DepartmentId = DepartmentId;

				profile.Disabled = model.Profile.Disabled;
				profile.Name = model.Profile.Name;
				profile.ShortName = model.Profile.ShortName;
				profile.Description = model.Profile.Description;
				profile.InCaseOfEmergency = model.Profile.InCaseOfEmergency;
				profile.ServiceArea = model.Profile.ServiceArea;
				profile.ServicesProvided = model.Profile.ServicesProvided;
				profile.Founded = model.Profile.Founded;
				profile.Keywords = model.Profile.Keywords;
				profile.InviteOnly = model.Profile.InviteOnly;
				profile.AllowMessages = model.Profile.AllowMessages;
				profile.VolunteerPositionsAvailable = model.Profile.VolunteerPositionsAvailable;
				profile.ShareStats = model.Profile.ShareStats;
				profile.VolunteerKeywords = model.Profile.VolunteerKeywords;
				profile.VolunteerDescription = model.Profile.VolunteerDescription;
				profile.VolunteerContactName = model.Profile.VolunteerContactName;
				profile.VolunteerContactInfo = model.Profile.VolunteerContactInfo;
				profile.Latitude = model.Profile.Latitude;
				profile.Longitude = model.Profile.Longitude;
				profile.What3Words = model.Profile.What3Words;

				if (profile.AddressId.HasValue)
				{
					var address = await _addressService.GetAddressByIdAsync(profile.AddressId.Value);
					address.Address1 = model.Address1;
					address.City = model.City;
					address.Country = model.Country;
					address.PostalCode = model.PostalCode;
					address.State = model.State;

					await _addressService.SaveAddressAsync(address);
				}
				else if (!String.IsNullOrWhiteSpace(model.Address1) && !String.IsNullOrWhiteSpace(model.City) &&
								 !String.IsNullOrWhiteSpace(model.PostalCode) && !String.IsNullOrWhiteSpace(model.State) &&
								 !String.IsNullOrWhiteSpace(model.Country))
				{
					var address = new Address();
					address.Address1 = model.Address1;
					address.City = model.City;
					address.Country = model.Country;
					address.PostalCode = model.PostalCode;
					address.State = model.State;

					var savedAddress = await _addressService.SaveAddressAsync(address);
					profile.AddressId = savedAddress.AddressId;
				}

				if (!String.IsNullOrWhiteSpace(profile.What3Words) &&
						(String.IsNullOrWhiteSpace(profile.Latitude) && String.IsNullOrWhiteSpace(profile.Longitude)))
				{
					var result = await _geoLocationProvider.GetCoordinatesFromW3W(profile.What3Words);
					if (result != null)
					{
						profile.Latitude = result.Latitude.ToString();
						profile.Longitude = result.Longitude.ToString();
					}
				}

				if (String.IsNullOrWhiteSpace(profile.Latitude) && String.IsNullOrWhiteSpace(profile.Longitude))
				{
					if (profile.AddressId.HasValue)
					{
						var address = await _addressService.GetAddressByIdAsync(profile.AddressId.Value);
						var location =
							await _geoLocationProvider.GetLatLonFromAddress(
								$"{address.Address1} {address.City} {address.State} {address.PostalCode} {address.Country}");

						if (!string.IsNullOrWhiteSpace(location))
						{
							var points = location.Split(char.Parse(","));
							profile.Latitude = points[0];
							profile.Longitude = points[1];
						}
					}
				}

				_departmentProfileService.SaveDepartmentProfile(profile);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_View)]
		public async Task<IActionResult> Messages()
		{
			var model = new MessagesView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);

			var messages = _departmentProfileService.GetVisibleMessagesForDepartment(model.Profile.DepartmentProfileId);

			if (messages != null && messages.Any())
			{
				var grouped = messages.GroupBy(x => x.UserId);

				foreach (var group in grouped)
				{
					var category = new MessageCategory();
					var userMessage = group.ToList().OrderByDescending(x => x.SentOn).First();

					category.UserId = group.Key;
					category.LastMessage = userMessage.SentOn;
					category.Name = userMessage.Name;
					category.LastMessageText = userMessage.Message;

					model.Messages.Add(category);
				}
			}
			else
			{
				model.Messages = new List<MessageCategory>();
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_View)]
		public async Task<IActionResult> Posts()
		{
			var model = new PostsView();
			var profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);
			model.DepartmentProfileId = profile.DepartmentProfileId;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_Create)]
		public async Task<IActionResult> AddPost()
		{
			var model = new AddPostView();
			model.Profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);
			model.Article = new DepartmentProfileArticle();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Article.StartOn = DateTime.UtcNow.TimeConverter(department);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Connect_Create)]
		public async Task<IActionResult> AddPost(AddPostView model, IFormCollection form)
		{
			model.Profile = _departmentProfileService.GetOrInitializeDepartmentProfile(DepartmentId);

			if (ModelState.IsValid)
			{
				model.Article.DepartmentProfileId = model.Profile.DepartmentProfileId;
				model.Article.CreatedByUserId = UserId;
				model.Article.CreatedOn = DateTime.UtcNow;

				_departmentProfileService.SaveArticle(model.Article);

				return RedirectToAction("Posts");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Connect_View)]
		public async Task<IActionResult> GetPostsList(int departmentProfileId)
		{
			List<PostJson> postJson = new List<PostJson>();
			var posts = _departmentProfileService.GetArticlesForDepartment(departmentProfileId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			foreach (var article in posts)
			{
				var post = new PostJson();
				post.Id = article.DepartmentProfileArticleId;
				post.Title = article.Title;
				post.Body = article.Body;
				post.SmallImage = article.SmallImage;
				post.LargeImage = article.LargeImage;
				post.CreatedOn = article.CreatedOn.FormatForDepartment(department, true);

				if (article.ExpiresOn.HasValue)
					post.ExpiresOn = article.ExpiresOn.Value.FormatForDepartment(department, true);
				else
					post.ExpiresOn = "Never";

				post.CreatedBy = await UserHelper.GetFullNameForUser(article.CreatedByUserId);

				postJson.Add(post);
			}

			return Json(postJson);
		}
	}
}
