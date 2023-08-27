using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class EditProfileModel: BaseUserModel
	{
		public string ApiUrl { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public bool Self { get; set; }
		public string Name { get; set; }
		public bool CanEnableVoice { get; set; }
		public UserProfile Profile { get; set; }
		public List<PushUri> PushUris { get; set; }
		public SelectList Groups { get; set; }
		public MobileCarriers Carrier { get; set; }
		public int UserGroup { get; set; }
		public bool IsUserGroupAdmin { get; set; }
		public string UserId { get; set; }
		public bool HasCustomIamge { get; set; }
		public List<PersonnelRole> UsersRoles { get; set; }
		public bool IsOwnProfile { get; set; }
		public bool IsFreePlan { get; set; }
			
		[Required]
		[MaxLength(50)]
		[Display(Name = "First name")]
		public string FirstName { get; set; }

		[Required]
		[MaxLength(50)]
		[Display(Name = "Last name")]
		public string LastName { get; set; }

		[Required]
		[MaxLength(150)]
		[DataType(DataType.EmailAddress)]
		public string Email { get; set; }

		[Display(Name = "Is Department Admin")]
		public bool IsDepartmentAdmin { get; set; }

		[Display(Name = "New Username")]
		public string NewUsername { get; set; }

		[StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
		[DataType(DataType.Password)]
		[Display(Name = "New password")]
		public string NewPassword { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm new password")]
		[System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string OldPassword { get; set; }

		public bool IsDisabled { get; set; }
		public bool IsHidden { get; set; }
		public bool AreYouSure { get; set; }


		[MaxLength(100)]
		public string PhysicalAddress1 { get; set; }

		[MaxLength(100)]
		public string PhysicalAddress2 { get; set; }

		[MaxLength(100)]
		public string PhysicalCity { get; set; }

		[MaxLength(50)]
		public string PhysicalState { get; set; }

		[MaxLength(50)]
		public string PhysicalPostalCode { get; set; }

		[MaxLength(100)]
		public string PhysicalCountry { get; set; }

		public bool MailingAddressSameAsPhysical { get; set; }

		[MaxLength(100)]
		public string MailingAddress1 { get; set; }

		[MaxLength(100)]
		public string MailingAddress2 { get; set; }

		[MaxLength(100)]
		public string MailingCity { get; set; }

		[MaxLength(50)]
		public string MailingState { get; set; }

		[MaxLength(50)]
		public string MailingPostalCode { get; set; }

		[MaxLength(100)]
		public string MailingCountry { get; set; }

		public bool EnableSms { get; set; }
	}
}
