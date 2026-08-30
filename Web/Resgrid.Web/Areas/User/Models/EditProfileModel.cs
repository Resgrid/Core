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
		public string UdfFormHtml { get; set; }

		/// <summary>
		/// True when this department encrypts member data: the identification number, addresses,
		/// emergency contacts and custom fields on this page render as placeholders until the
		/// member steps up. Drives the reveal banner.
		/// </summary>
		public bool IsProtectedProfile { get; set; }
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
		public bool CanManageLocalCredentials { get; set; }
		public bool CanResetPassword { get; set; }
		public bool IsSsoManaged { get; set; }
		public bool IsEmailExternallyManaged { get; set; }
		public bool RequirePasswordResetViaEmail { get; set; }

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

		public bool IsDisabled { get; set; }
		public bool IsHidden { get; set; }
		public bool AreYouSure { get; set; }

		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		public string PhysicalAddress1 { get; set; }

		[StringLength(500, ErrorMessage = "Street address line 2 cannot exceed 500 characters.")]
		public string PhysicalAddress2 { get; set; }

		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		public string PhysicalCity { get; set; }

		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		public string PhysicalState { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		public string PhysicalPostalCode { get; set; }

		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
		public string PhysicalCountry { get; set; }

		public bool MailingAddressSameAsPhysical { get; set; }

		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		public string MailingAddress1 { get; set; }

		[StringLength(500, ErrorMessage = "Street address line 2 cannot exceed 500 characters.")]
		public string MailingAddress2 { get; set; }

		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		public string MailingCity { get; set; }

		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		public string MailingState { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		public string MailingPostalCode { get; set; }

		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
		public string MailingCountry { get; set; }

		public bool EnableSms { get; set; }

		// ── Security PIN (step-up auth for dangerous chatbot/SMS actions; own profile only) ──

		/// <summary>The user's decrypted 4-digit security PIN (only populated for the own-profile view).</summary>
		[Display(Name = "Security PIN")]
		public string SecurityPin { get; set; }

		[Display(Name = "Require my security PIN for dangerous chatbot/text actions")]
		public bool SecurityPinEnabled { get; set; }

		/// <summary>True when the department forces PIN usage for all members (personal opt-in is then moot).</summary>
		public bool DepartmentForcesSecurityPin { get; set; }

		// ── Contact verification status (tri-state: null = grandfathered, false = pending, true = verified) ──
		public bool? EmailVerified => Profile?.EmailVerified;
		public bool? MobileNumberVerified => Profile?.MobileNumberVerified;
		public bool? HomeNumberVerified => Profile?.HomeNumberVerified;

		/// <summary>True when the profile belongs to the currently authenticated user (only own profile can self-verify).
		/// Checks both <see cref="IsOwnProfile"/> and <see cref="Self"/> because different controller paths set one or the other.</summary>
		public bool CanSelfVerify => Self || IsOwnProfile;

		public GdprDataExportRequest ActiveDataExportRequest { get; set; }
	}
}
