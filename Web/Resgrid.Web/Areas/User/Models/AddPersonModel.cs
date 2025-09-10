using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models
{
	public class AddPersonModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public UserProfile Profile { get; set; }
		public MobileCarriers Carrier { get; set; }
		public int UserGroup { get; set; }
		public bool IsUserGroupAdmin { get; set; }
		public string UserId { get; set; }
		public List<PersonnelRole> UsersRoles { get; set; }
		public SelectList Groups { get; set; }
		public string GroupName { get; set; }
		public bool GroupAdmin { get; set; }
		public bool IsGroupAdminAdding { get; set; }

		[Required]
		[MaxLength(50)]
		[Display(Name = "User name", Description = "You must supply a username.")]
		public string Username { get; set; }

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

		[StringLength(100, ErrorMessage = "The password must be at least 8 characters long", MinimumLength = 8)]
		[PasswordComplexity(
			MinLength = 8,
			RequireUppercase = true,
			RequireLowercase = true,
			RequireDigit = true,
			RequireSpecialChar = false)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		[Required]
		public string NewPassword { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm Password")]
		[System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
		[Required]
		public string ConfirmPassword { get; set; }

		public bool SendAccountCreationNotification { get; set; }
	}
}
