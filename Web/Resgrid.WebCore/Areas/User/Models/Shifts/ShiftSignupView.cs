using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftSignupView
	{
		public ShiftDay Day { get; set; }
		public ShiftSignup Signup { get; set; }
		public List<PersonnelRole> Roles { get; set; }
		public Dictionary<int, Dictionary<int, int>> Needs { get; set; }
		public bool UserSignedUp { get; set; }
		public List<ShiftSignup> Signups { get; set; }
		public Dictionary<string, List<PersonnelRole>> PersonnelRoles { get; set; }
		public Dictionary<string, UserProfile> UserProfiles { get; set; }
		public Dictionary<int, bool> ShiftGroupSignups { get; set; }

		public ShiftSignupView()
		{
			ShiftGroupSignups = new Dictionary<int, bool>();
		}
	}
}
