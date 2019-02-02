#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;

namespace Xipton.Razor {
    /// <summary>
    /// a LiteralString represents a string that should not be encoded (again)
    /// </summary>
    public class LiteralString{

        private readonly string 
            _literalString;

        public LiteralString(string literalString){
            _literalString = literalString;
        }

        public override bool Equals(object obj) {
            var other = obj as LiteralString;
            return Equals(_literalString, other != null ? other._literalString : obj);
        }

        public override int GetHashCode() {
            return _literalString != null ? _literalString.GetHashCode() : 0;
        }

        public static implicit operator String(LiteralString s){
            return s == null ? null : s._literalString;
        }
        public static implicit operator LiteralString(string s) {
            return new LiteralString(s);
        }

        public static bool operator ==(LiteralString s1, string s){
            return Equals(s1, null) ? Equals(s, null) : Equals(s1._literalString, s);
        }
        public static bool operator !=(LiteralString s1, string s){
            return !(s1 == s);
        }

        public override string ToString() {
            // should never return null
            return _literalString ?? string.Empty;
        }
    }
}
