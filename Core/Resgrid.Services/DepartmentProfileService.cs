using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	//public class DepartmentProfileService: IDepartmentProfileService
	//{
	//	private readonly IGenericDataRepository<DepartmentProfile> _departmentProfileRepository;
	//	private readonly IGenericDataRepository<DepartmentProfileArticle> _departmentProfileArticleRepository;
	//	private readonly IGenericDataRepository<DepartmentProfileInvite> _departmentProfileInviteRepository;
	//	private readonly IGenericDataRepository<DepartmentProfileMessage> _departmentProfileMessageRepository;

	//	private readonly IGenericDataRepository<DepartmentProfileUser> _departmentProfileUserRepository;
	//	private readonly IGenericDataRepository<DepartmentProfileUserFollow> _departmentProfileUserFollowRepository;

	//	private readonly IDepartmentsService _departmentsService;
	//	private readonly IDepartmentSettingsService _departmentSettingsService;

	//	public DepartmentProfileService(IGenericDataRepository<DepartmentProfile> departmentProfileRepository, IGenericDataRepository<DepartmentProfileArticle> departmentProfileArticleRepository,
	//		IGenericDataRepository<DepartmentProfileInvite> departmentProfileInviteRepository, IGenericDataRepository<DepartmentProfileMessage> departmentProfileMessageRepository,
	//		IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService, IGenericDataRepository<DepartmentProfileUser> departmentProfileUserRepository,
	//		IGenericDataRepository<DepartmentProfileUserFollow> departmentProfileUserFollowRepository)
	//	{
	//		_departmentProfileRepository = departmentProfileRepository;
	//		_departmentProfileArticleRepository = departmentProfileArticleRepository;
	//		_departmentProfileInviteRepository = departmentProfileInviteRepository;
	//		_departmentProfileMessageRepository = departmentProfileMessageRepository;
	//		_departmentsService = departmentsService;
	//		_departmentSettingsService = departmentSettingsService;
	//		_departmentProfileUserRepository = departmentProfileUserRepository;
	//		_departmentProfileUserFollowRepository = departmentProfileUserFollowRepository;
	//	}

	//	public List<DepartmentProfile> GetAll()
	//	{
	//		return _departmentProfileRepository.GetAll().ToList();
	//	}

	//	public List<DepartmentProfile> GetAllActive()
	//	{
	//		return _departmentProfileRepository.GetAll().Where(x => !x.Disabled).ToList();
	//	}

	//	public DepartmentProfile GetDepartmentProfileByDepartmentId(int departmentId)
	//	{
	//		return _departmentProfileRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId);
	//	}

	//	public DepartmentProfile SaveDepartmentProfile(DepartmentProfile profile)
	//	{
	//		_departmentProfileRepository.SaveOrUpdate(profile);

	//		return profile;
	//	}

	//	public DepartmentProfile GetProfileById(int departmentProfileId)
	//	{
	//		return _departmentProfileRepository.GetAll().FirstOrDefault(x => x.DepartmentProfileId == departmentProfileId);
	//	}

	//	public DepartmentProfile GetOrInitializeDepartmentProfile(int departmentId)
	//	{
	//		DepartmentProfile profile = GetDepartmentProfileByDepartmentId(departmentId);

	//		if (profile != null)
	//			return profile;

	//		Department department = _departmentsService.GetDepartmentById(departmentId);

	//		profile = new DepartmentProfile();
	//		profile.Code = $"{RandomGenerator.CreateCode(2)}-{RandomGenerator.CreateCode(4)}";
	//		profile.DepartmentId = departmentId;
	//		profile.Name = department.Name;

	//		var mapCenterGpsCoordinates = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(departmentId);

	//		if (!String.IsNullOrWhiteSpace(mapCenterGpsCoordinates))
	//		{
	//			string[] coordinates = mapCenterGpsCoordinates.Split(char.Parse(","));

	//			if (coordinates.Count() == 2)
	//			{
	//				profile.Latitude = coordinates[0];
	//				profile.Longitude = coordinates[1];
	//			}
	//		}
	//		else
	//		{
	//			profile.AddressId = department.AddressId;
	//		}

	//		return SaveDepartmentProfile(profile);
	//	}

	//	public List<DepartmentProfileArticle> GetArticlesForDepartment(int departmentProfileId)
	//	{
	//		return _departmentProfileArticleRepository.GetAll().Where(x => x.DepartmentProfileId == departmentProfileId).ToList();
	//	}

	//	public List<DepartmentProfileArticle> GetVisibleArticlesForDepartment(int departmentProfileId)
	//	{
	//		var posts = _departmentProfileArticleRepository.GetAll().Where(x => x.DepartmentProfileId == departmentProfileId).ToList();

	//		return
	//			posts.Where(
	//				x =>
	//					x.StartOn >= DateTime.UtcNow &&
	//					(!x.ExpiresOn.HasValue == false || (x.ExpiresOn.HasValue && x.ExpiresOn.Value <= DateTime.UtcNow))).ToList();
	//	}

	//	public DepartmentProfileArticle SaveArticle(DepartmentProfileArticle article)
	//	{
	//		_departmentProfileArticleRepository.SaveOrUpdate(article);

	//		return article;
	//	}

	//	public List<DepartmentProfileMessage> GetVisibleMessagesForDepartment(int departmentProfileId)
	//	{
	//		return _departmentProfileMessageRepository.GetAll().Where(x => x.Deleted == false && x.DepartmentProfileId == departmentProfileId).ToList();
	//	}

	//	public DepartmentProfileUser GetUserByIdentity(string id)
	//	{
	//		return _departmentProfileUserRepository.GetAll().FirstOrDefault(x => x.Identity == id);
	//	}

	//	public DepartmentProfileUser SaveUser(DepartmentProfileUser user)
	//	{
	//		_departmentProfileUserRepository.SaveOrUpdate(user);

	//		return user;
	//	}

	//	public DepartmentProfileUserFollow SaveFollow(DepartmentProfileUserFollow follow)
	//	{
	//		_departmentProfileUserFollowRepository.SaveOrUpdate(follow);

	//		return follow;
	//	}

	//	public void DeleteFollow(DepartmentProfileUserFollow follow)
	//	{
	//		_departmentProfileUserFollowRepository.DeleteOnSubmit(follow);
	//	}

	//	public List<DepartmentProfileUserFollow> GetFollowsForUser(string userId)
	//	{
	//		return _departmentProfileUserFollowRepository.GetAll().Where(x => x.DepartmentProfileUserId == userId).ToList();
	//	}

	//	public DepartmentProfileUserFollow GetFollowForUserDepartment(string userId, int departmentProfileId)
	//	{
	//		return _departmentProfileUserFollowRepository.GetAll().FirstOrDefault(x => x.DepartmentProfileUserId == userId && x.DepartmentProfileId == departmentProfileId);
	//	}

	//	public List<DepartmentProfileArticle> GetArticlesForUser(string userId)
	//	{
	//		var followingDepartments = GetFollowsForUser(userId);

	//		if (followingDepartments == null || !followingDepartments.Any())
	//			return new List<DepartmentProfileArticle>();

	//		var ids = followingDepartments.Select(x => x.DepartmentProfileId);

	//		var articles = (from a in _departmentProfileArticleRepository.GetAll().ToList()
	//			where ids.Contains(a.DepartmentProfileId)
	//			select a).ToList();

	//		return articles;
	//	}

	//	public DepartmentProfileUserFollow FollowDepartment(string userId, int departmentProfileId, string code)
	//	{
	//		var profile = GetProfileById(departmentProfileId);

	//		if (profile == null)
	//			return null;

	//		if (profile.Disabled)
	//			return null;

	//		if (profile.InviteOnly && profile.Code != code)
	//			return null;

	//		var follow = new DepartmentProfileUserFollow();
	//		follow.DepartmentProfileId = departmentProfileId;
	//		follow.DepartmentProfileUserId = userId;

	//		follow = SaveFollow(follow);

	//		return follow;
	//	}

	//	public void UnFollowDepartment(string userId, int departmentProfileId)
	//	{
	//		var follow = GetFollowForUserDepartment(userId, departmentProfileId);

	//		if (follow == null)
	//			return;

	//		DeleteFollow(follow);
	//	}
	//}
}
