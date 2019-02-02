using System;
using System.Collections.Generic;

namespace Resgrid.Model.Cache
{
	public class CustomStatesCacheContainer
	{
		public int DepartmentId { get; set; }
		public DateTime TimeStamp { get; set; }
		public List<CustomState> CustomStates { get; set; } 
	}
}