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
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
    public class FileExtensionAttribute : Attribute
    {
        public FileExtensionAttribute(string extension, bool excludeOthers = false)
        {
            Extension = extension;
            ExcludeOthers = excludeOthers;
        }
        public string Extension { get; init; }
        public bool ExcludeOthers { get; init; }

        public static FileInfo[] GetFilesWithExtension(DirectoryInfo dir, FileExtensionAttribute attr)
        {
            var otherExtensions = new Lazy<ImmutableArray<string>>(() => Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetCustomAttributes<FileExtensionAttribute>(true)
                    .Select(fea => fea.Extension))
                .Except(new[] {attr.Extension})
                .ToImmutableArray());

            return dir.GetFiles(attr.Extension)
                .Where(fi => !attr.ExcludeOthers ||
                             otherExtensions.Value.Any(extToExclude => !fi.Name.EndsWith(extToExclude))).ToArray();
        }
    }
}
