using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.CliSpectre.Utils
{
    /// <summary>
    /// Marks a field as needed to be obfuscated in logs
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    sealed class LogObfuscateAttribute : Attribute
    {
        // See the attribute guidelines at 
        //  http://go.microsoft.com/fwlink/?LinkId=85236
        public LogObfuscateAttribute()
        {
        }
    }
}
