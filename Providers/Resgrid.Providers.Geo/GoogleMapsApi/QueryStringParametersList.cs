using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GoogleMapsApi
{
	public class QueryStringParametersList
	{
		private List<KeyValuePair<string,string>> list { get; set; }

		public QueryStringParametersList()
		{
			list = new List<KeyValuePair<string, string>>();
		}

		public void Add(string key, string value)
		{
			list.Add(new KeyValuePair<string, string>(key, value));
		}

		public string GetQueryStringPostfix()
		{
			return string.Join("&", list.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
		}
	}
}
