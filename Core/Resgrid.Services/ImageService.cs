using System;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ImageService : IImageService
	{
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentProfileService _departmentProfileService;

		public ImageService(IUserProfileService userProfileService, IDepartmentProfileService departmentProfileService)
		{
			_userProfileService = userProfileService;
			_departmentProfileService = departmentProfileService;
		}

		public byte[] GetImage(ImageTypes type, string id)
		{
			switch (type)
			{
				case ImageTypes.Avatar:
					var up = _userProfileService.GetProfileByUserId(id, true);

					if (up != null)
						return up.Image;
					break;
				case ImageTypes.Department:
					var di = _departmentProfileService.GetDepartmentProfileByDepartmentId(int.Parse(id));

					if (di != null)
						return di.Logo;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}

			return null;
		}

		public void SaveImage(ImageTypes type, string id, byte[] image)
		{
			switch (type)
			{
				case ImageTypes.Avatar:
					var up = _userProfileService.GetUserProfileForEditing(id);

					if (up != null)
					{
						up.Image = image;
						up.LastUpdated = DateTime.UtcNow;

						_userProfileService.SaveProfile(0, up);
					}
					break;
				case ImageTypes.Department:
					var di = _departmentProfileService.GetOrInitializeDepartmentProfile(int.Parse(id));

					if (di != null)
					{
						di.Logo = image;
						_departmentProfileService.SaveDepartmentProfile(di);
					}
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}
		}
	}
}
