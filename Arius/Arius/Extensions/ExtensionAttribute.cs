using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Arius.Models;
using Arius.Repositories;
using Azure.Storage.Blobs.Models;

namespace Arius.Extensions
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    internal class ExtensionAttribute : Attribute
    {
        public ExtensionAttribute(string extension/*, bool excludeOthers = false*/, Type encryptedType = null, Type decryptedType = null)
        {
            Extension = extension;
            //ExcludeOthers = excludeOthers;
            EncryptedType = encryptedType;
            DecryptedType = decryptedType;
        }
        public string Extension { get; init; }
        //public bool ExcludeOthers { get; init; }
        private Type EncryptedType { get; init; }
        private Type DecryptedType { get; init; }

        public FileInfo GetEncryptedFileInfo(ILocalFile lf)
        {
            var encryptedType = lf.GetType().GetCustomAttribute<ExtensionAttribute>()!.EncryptedType;
            var encryptedTypeExtension = encryptedType.GetCustomAttribute<ExtensionAttribute>()!.Extension;

            return new FileInfo(Path.Combine(lf.Root.FullName, $"{lf.Hash}{encryptedTypeExtension}"));
        }
        public FileInfo GetEncryptedFileInfo(ILocalFile lf, RemoteEncryptedChunkRepository chunkRepository)
        {
            var encryptedType = lf.GetType().GetCustomAttribute<ExtensionAttribute>()!.EncryptedType;
            var encryptedTypeExtension = encryptedType.GetCustomAttribute<ExtensionAttribute>()!.Extension;

            return new FileInfo(Path.Combine(chunkRepository.FullName, $"{lf.Hash}{encryptedTypeExtension}"));
        }
        public FileInfo GetDecryptedFileInfo(ILocalFile lf)
        {
            var decryptedType = lf.GetType().GetCustomAttribute<ExtensionAttribute>()!.DecryptedType;
            var decryptedTypeExtension = decryptedType.GetCustomAttribute<ExtensionAttribute>()!.Extension;

            return new FileInfo(Path.Combine(lf.Root.FullName, $"{lf.NameWithoutExtension}{decryptedTypeExtension}"));
        }

        public bool IsMatch(FileInfo fi)
        {
            return IsMatch(fi.Name);
        }
        public bool IsMatch(BlobItem bi)
        {
            return IsMatch(bi.Name);
        }
        public bool IsMatch(string fileName)
        {
            if (Extension == ".*")
            {
                return !OtherExtensions(this).Any(extToExclude => fileName.EndsWith(extToExclude));
            }
            else
            {
                if (fileName.EndsWith(Extension))
                {
                    // True if the extension is not contained within another extension (eg. .manifest.7z.arius should not match with .7z.arius)
                    var r = !OtherExtensions(this)
                        .Where(ext => ext.EndsWith(Extension))
                        .Any(extToExclude => fileName.EndsWith(extToExclude)); ;

                    return r;
                }
                else
                    return false;
            }
        }


        static ExtensionAttribute()
        {
            _extensions = new Lazy<ImmutableArray<string>>(() => Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetCustomAttributes<ExtensionAttribute>(true)
                    .Select(fea => fea.Extension)).ToImmutableArray());
        }

        private static readonly Lazy<ImmutableArray<string>> _extensions;
        //public static FileInfo[] GetFilesWithExtension(DirectoryInfo dir, ExtensionAttribute attr)
        //{
        //    return dir.GetFiles($"*{attr.Extension}")
        //        .Where(fi => !attr.ExcludeOthers ||
        //                     !OtherExtensions(attr).Any(extToExclude => fi.Name.EndsWith(extToExclude))).ToArray();
        //}

        

        private static ImmutableArray<string> OtherExtensions(ExtensionAttribute attr)
        {
            return _extensions.Value
                .Except(new[] { attr.Extension })
                .ToImmutableArray();
        }
    }
}
