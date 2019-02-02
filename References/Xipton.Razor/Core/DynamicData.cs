#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace Xipton.Razor.Core
{
    /// <summary>
    /// This class is used for both the template's Model (with anonymous model types only) and ViewBag property
    /// </summary>
    public class DynamicData : DynamicObject
    {
        private readonly ConcurrentDictionary<string, object>
            _data;

        public DynamicData(object target)
        : this(target == null ? null : (IDictionary<string,object>)target.GetType()
                .GetProperties()
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToDictionary(property => property.Name, property => property.GetValue(target, null)))
        {
        }

        public DynamicData(IDictionary data)
        {
            _data = new ConcurrentDictionary<string, object>();
            if (data == null) 
                return;

            foreach (var key in data.Keys)
                _data[key.ToString()] = data[key];
        }

        public DynamicData(IEnumerable<KeyValuePair<string, object>> data = null)
        {
            _data = new ConcurrentDictionary<string, object>();
            if (data != null)
                foreach (var pair in data)
                    _data[pair.Key] = pair.Value;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _data.Keys;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = null;

            return binder != null && _data.TryGetValue(binder.Name, out result);
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            if (binder != null)
            {
                _data[binder.Name] = value;

                return true;
            }

            return false;
        }


    }
}