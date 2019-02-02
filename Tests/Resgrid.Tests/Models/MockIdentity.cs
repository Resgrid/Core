using System;
using System.Security.Principal;

namespace Resgrid.Tests.Models
{
	public class MockIdentity : IIdentity
	{
		private readonly string _name;

		public MockIdentity(string userName)
		{
			_name = userName;
		}

		public string AuthenticationType
		{
			get { throw new System.NotImplementedException(); }
		}

		public bool IsAuthenticated
		{
			get { return !String.IsNullOrEmpty(_name); }
		}

		public string Name
		{
			get { return _name; }
		}
	}
}
