using System;

namespace Resgrid.Web.Services.ModelBinders
{
	[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
	internal sealed class AliasAttribute : Attribute
	{
		public string Name { get; set; }

		public AliasAttribute(string name)
		{
			Name = name;
		}
	}
}