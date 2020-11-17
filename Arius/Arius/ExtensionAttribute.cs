using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ExtensionAttribute : Attribute
    {
        public ExtensionAttribute(string extension, bool excludeOthers = false)
        {
            Extension = extension;
            ExcludeOthers = excludeOthers;
        }
        public string Extension { get; init; }
        public bool ExcludeOthers { get; init; }

        public bool IsMatch(FileInfo fi)
        {
            if (ExcludeOthers &&
                OtherExtensions(this).Any(extToExclude => fi.Name.EndsWith(extToExclude)))
                return false;

            if (Extension == ".*")
                return true;

            return fi.Name.EndsWith(Extension);
        }


        static ExtensionAttribute()
        {
            _extensions = new Lazy<ImmutableArray<string>>(() => Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetCustomAttributes<ExtensionAttribute>(true)
                    .Select(fea => fea.Extension)).ToImmutableArray());
        }

        private static readonly Lazy<ImmutableArray<string>> _extensions;
        public static FileInfo[] GetFilesWithExtension(DirectoryInfo dir, ExtensionAttribute attr)
        {
            return dir.GetFiles($"*{attr.Extension}")
                .Where(fi => !attr.ExcludeOthers ||
                             !OtherExtensions(attr).Any(extToExclude => fi.Name.EndsWith(extToExclude))).ToArray();
        }

        private static ImmutableArray<string> OtherExtensions(ExtensionAttribute attr)
        {
            return _extensions.Value
                .Except(new[] { attr.Extension })
                .ToImmutableArray();
        }
    }
}
