//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using GeoCoordinatePortable;
//using Microsoft.AspNetCore.Mvc;
//using Resgrid.Model;
//using Resgrid.Model.Services;
//using Resgrid.Web.Services.Controllers.Version3.Models.Connect;
//using DepartmentProfile = Resgrid.Web.Services.Controllers.Version3.Models.Connect.DepartmentProfile;

//namespace Resgrid.Web.Services.Controllers.Version3
//{
//	/// <summary>
//	/// Operations to perform against the connect sub-system. Connect allows the public to communicate with a department.
//	/// </summary>
//	public class ConnectController : ControllerBase
//	{
//		private readonly IDepartmentsService _departmentsService;
//		private readonly IDepartmentProfileService _departmentProfileService;

//		/// <summary>
//		/// Operations to perform against the connect sub-system
//		/// </summary>
//		public ConnectController(IDepartmentsService departmentsService, IDepartmentProfileService departmentProfileService)
//		{
//			_departmentsService = departmentsService;
//			_departmentProfileService = departmentProfileService;
//		}

//		[HttpGet]
//		public List<DepartmentProfile> ListDepartments(string latitude = null, string longitude = null)
//		{
//			var departments = new List<DepartmentProfile>();
//			var profiles = _departmentProfileService.GetAllActive();

//			if (profiles != null && profiles.Any())
//			{
//				GeoCoordinate source;
//				if (!String.IsNullOrWhiteSpace(latitude) && !String.IsNullOrWhiteSpace(longitude))
//					source = new GeoCoordinate(double.Parse(latitude), double.Parse(longitude));
//				else
//				  source = new GeoCoordinate(0, 0);

//				foreach (var profile in profiles.OrderBy(x => x.Coordinate.GetDistanceTo(source)))
//				{
//					var department = new DepartmentProfile();
//					department.ProfileId = profile.DepartmentProfileId;
//					department.Did = profile.DepartmentId;
//					department.Name = profile.Name;
//					department.ShortName = profile.ShortName;
//					department.Description = profile.Description;
//					department.InCaseOfEmergency = profile.InCaseOfEmergency;
//					department.ServiceArea = profile.ServiceArea;
//					department.ServicesProvided = profile.ServicesProvided;
//					department.Founded = profile.Founded;
//					//department.Logo = profile.Logo;
//					department.Keywords = profile.Keywords;
//					department.InviteOnly = profile.InviteOnly;
//					department.AllowMessages = profile.AllowMessages;
//					department.VolunteerPositionsAvailable = profile.VolunteerPositionsAvailable;
//					department.ShareStats = profile.ShareStats;
//					department.VolunteerKeywords = profile.VolunteerKeywords;
//					department.VolunteerDescription = profile.VolunteerDescription;
//					department.VolunteerContactName = profile.VolunteerContactName;
//					department.VolunteerContactInfo = profile.VolunteerContactInfo;
//					department.Geofence = profile.Geofence;
//					department.Address = profile.Address;
//					department.Latitude = profile.Latitude;
//					department.Longitude = profile.Longitude;
//					department.What3Words = profile.What3Words;
//					department.Facebook = profile.Facebook;
//					department.Twitter = profile.Twitter;
//					department.GooglePlus = profile.GooglePlus;
//					department.LinkedIn = profile.LinkedIn;
//					department.Instagram = profile.Instagram;
//					department.YouTube = profile.YouTube;
//					department.Website = profile.Website;
//					department.PhoneNumber = profile.PhoneNumber;

//					departments.Add(department);
//				}
//			}

//			return departments;
//		}

//		[HttpGet]
//		public DepartmentProfile GetDepartment(string profileId, string userId)
//		{
//			int id;
//			Guid uid;

//			if (!int.TryParse(profileId, out id))
//				return null;

//			var department = new DepartmentProfile();
//			var profile = _departmentProfileService.GetProfileById(id);

//			if (profile != null)
//			{
//				department.ProfileId = profile.DepartmentProfileId;
//				department.Did = profile.DepartmentId;
//				department.Name = profile.Name;
//				department.ShortName = profile.ShortName;
//				department.Description = profile.Description;
//				department.InCaseOfEmergency = profile.InCaseOfEmergency;
//				department.ServiceArea = profile.ServiceArea;
//				department.ServicesProvided = profile.ServicesProvided;
//				department.Founded = profile.Founded;
//				//department.Logo = profile.Logo;
//				department.Keywords = profile.Keywords;
//				department.InviteOnly = profile.InviteOnly;
//				department.AllowMessages = profile.AllowMessages;
//				department.VolunteerPositionsAvailable = profile.VolunteerPositionsAvailable;
//				department.ShareStats = profile.ShareStats;
//				department.VolunteerKeywords = profile.VolunteerKeywords;
//				department.VolunteerDescription = profile.VolunteerDescription;
//				department.VolunteerContactName = profile.VolunteerContactName;
//				department.VolunteerContactInfo = profile.VolunteerContactInfo;
//				department.Geofence = profile.Geofence;
//				department.Address = profile.Address;
//				department.Latitude = profile.Latitude;
//				department.Longitude = profile.Longitude;
//				department.What3Words = profile.What3Words;
//				department.Facebook = profile.Facebook;
//				department.Twitter = profile.Twitter;
//				department.GooglePlus = profile.GooglePlus;
//				department.LinkedIn = profile.LinkedIn;
//				department.Instagram = profile.Instagram;
//				department.YouTube = profile.YouTube;
//				department.Website = profile.Website;
//				department.PhoneNumber = profile.PhoneNumber;

//				var userFollows = _departmentProfileService.GetFollowsForUser(userId);

//				if (userFollows != null && userFollows.Any(x => x.DepartmentProfileId == id))
//					department.Following = true;
//			}

//			return department;
//		}

//		[HttpPost]
//		public RegisterResult Register(RegisterInput input)
//		{
//			var result = new RegisterResult();
//			var user = _departmentProfileService.GetUserByIdentity(input.Id);

//			if (user != null)
//			{
//				result.Success = false;
//				result.Message = "User was already created";
//				result.UserId = user.DepartmentProfileUserId;
//			}
//			else
//			{
//				var profileUser = new DepartmentProfileUser();
//				profileUser.Identity = input.Id;
//				profileUser.Name = input.Name;
//				profileUser.Email = input.Email;

//				_departmentProfileService.SaveUser(profileUser);

//				result.Success = true;
//				result.UserId = profileUser.DepartmentProfileUserId;
//			}

//			return result;
//		}

//		[HttpGet]
//		public List<DepartmentListResult> GetFollows(string userId)
//		{
//			var list = new List<DepartmentListResult>();
//			var follows = _departmentProfileService.GetFollowsForUser(userId);

//			foreach (var follow in follows)
//			{
//				var result = new DepartmentListResult();
//				result.Id = follow.DepartmentProfileId;
//				result.Name = follow.DepartmentProfile.Name;

//				list.Add(result);
//			}

//			return list;
//		}

//		public List<ArticleResult> GetFeed(string userId)
//		{
//			var result = new List<ArticleResult>();
//			var articles = _departmentProfileService.GetArticlesForUser(userId);

//			foreach (var article in articles)
//			{
//				var articleResult = new ArticleResult();
//				articleResult.Id = article.DepartmentProfileArticleId;
//				articleResult.Title = article.Title;
//				articleResult.Body = article.Body;
//				articleResult.SmallImage = article.SmallImage;
//				articleResult.LargeImage = article.LargeImage;
//				articleResult.Keywords = article.Keywords;
//				articleResult.CreatedOn = article.CreatedOn;
//				articleResult.CreatedByUserId = article.CreatedByUserId;
//				articleResult.StartOn = article.StartOn;
//				articleResult.ExpiresOn = article.ExpiresOn;

//				result.Add(articleResult);
//			}

//			return result;
//		}

//		public FollowResult Follow(FollowInput input)
//		{
//			var result = new FollowResult();
//			var follow = _departmentProfileService.FollowDepartment(input.UserId, input.Id, input.Code);

//			if (follow != null)
//				result.Successful = true;
//			else
//			{
//				result.Successful = false;
//				result.Message = "Could not follow department. It's either Disabled, Missing or you supplied the incorrect code.";
//			}

//			return result;
//		}

//		public FollowResult UnFollow(FollowInput input)
//		{
//			var result = new FollowResult();
//			_departmentProfileService.UnFollowDepartment(input.UserId, input.Id);

//			result.Successful = true;

//			return result;
//		}

//		public ActionResult Options()
//		{
//			var response = new HttpResponseMessage();
//			response.StatusCode = HttpStatusCode.OK;
//			response.Headers.Add("Access-Control-Allow-Origin", "*");
//			response.Headers.Add("Access-Control-Request-Headers", "*");
//			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

//			return response;
//		}
//	}
//}
