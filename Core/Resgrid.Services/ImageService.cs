using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ImageService : IImageService
	{
		private readonly IUserProfileService _userProfileService;
		//private readonly IDepartmentProfileService _departmentProfileService;

		public ImageService(IUserProfileService userProfileService/*, IDepartmentProfileService departmentProfileService*/)
		{
			_userProfileService = userProfileService;
			//_departmentProfileService = departmentProfileService;
		}

		public async Task<byte[]> GetImageAsync(ImageTypes type, string id)
		{
			switch (type)
			{
				case ImageTypes.Avatar:
					var up = await _userProfileService.GetProfileByUserIdAsync(id, true);

					if (up != null)
						return up.Image;
					break;
				//case ImageTypes.Department:
				//	var di = _departmentProfileService.GetDepartmentProfileByDepartmentId(int.Parse(id));

				//	if (di != null)
				//		return di.Logo;
				//	break;
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}

			return null;
		}

		public async Task<bool> SaveImageAsync(ImageTypes type, string id, byte[] image, CancellationToken cancellationToken = default(CancellationToken))
		{
			switch (type)
			{
				case ImageTypes.Avatar:
					var up = await _userProfileService.GetProfileByUserIdAsync(id);

					if (up != null)
					{
						up.Image = image;
						up.LastUpdated = DateTime.UtcNow;

						await _userProfileService.SaveProfileAsync(0, up, cancellationToken);
					}
					break;
				//case ImageTypes.Department:
				//	var di = _departmentProfileService.GetOrInitializeDepartmentProfile(int.Parse(id));

				//	if (di != null)
				//	{
				//		di.Logo = image;
				//		_departmentProfileService.SaveDepartmentProfile(di);
				//	}
				//	break;
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}

			return true;
		}
	}
}
