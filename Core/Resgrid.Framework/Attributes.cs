using System;

namespace Resgrid.Framework
{
	/// <summary>
	/// An attribute that will test coverage tools to ingore a method
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class CoverageIgnoreAttribute : Attribute
	{
	}

    /// <summary>
    /// Attribute to set the precision and scale of a decimal for Entity Framework
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public class DecimalPrecisionAttribute : Attribute
    {
        public byte Precision { get; set; }
        public byte Scale { get; set; }

        public DecimalPrecisionAttribute(byte precision, byte scale)
        {
            Precision = precision;
            Scale = scale;
        }
    }
}
