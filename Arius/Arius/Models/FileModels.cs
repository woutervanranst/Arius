using System;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Services;

namespace Arius.Models
{
    internal abstract class LocalFile : ILocalFile //, IFile
    {

        protected LocalFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider)
        {
            if (!fi.Exists)
                throw new ArgumentException("The LocalFile does not exist");

            Root = root;

            _fi = fi;

            _hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly Lazy<HashValue> _hash;

        protected readonly FileInfo _fi;

        public HashValue Hash => _hash.Value;

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        public string DirectoryName => _fi.DirectoryName;
        public IRepository Root { get; }

        public void Delete()
        {
            _fi.Delete();
        }

        public string NameWithoutExtension => Name.TrimEnd(this.GetType().GetCustomAttribute<ExtensionAttribute>().Extension);
    }

    [Extension(".pointer.arius", encryptedType: typeof(LocalEncryptedManifestFile))]
    internal class LocalPointerFile : LocalFile, IPointerFile
    {
        public LocalPointerFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
            _manifestFileName = new Lazy<string>(() => File.ReadAllText(fi.FullName));
        }

        private readonly Lazy<string> _manifestFileName;

        public FileInfo LocalContentFileInfo => new FileInfo(Path.Combine(DirectoryName, NameWithoutExtension));
        public string ManifestFileName => _manifestFileName.Value;

        public string RelativeName => Path.GetRelativePath(Root.FullName, Path.Combine(DirectoryName, Name));

        public DateTime CreationTimeUtc { get => _fi.CreationTimeUtc; set => _fi.CreationTimeUtc = value; }
        public DateTime LastWriteTimeUtc { get => _fi.LastWriteTimeUtc; set => _fi.LastWriteTimeUtc = value; }
    }

    [Extension(".*", encryptedType: typeof(RemoteEncryptedChunkBlob))]
    internal class LocalContentFile : LocalFile, ILocalContentFile, IChunkFile
    {
        public LocalContentFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }

        private static readonly string _pointerFileExtension = typeof(LocalPointerFile).GetCustomAttribute<ExtensionAttribute>()!.Extension;

        public FileInfo PointerFileInfo => new FileInfo($"{FullName}{_pointerFileExtension}");

        public string RelativeName => Path.GetRelativePath(Root.FullName, FullName);

        public DateTime CreationTimeUtc { get => _fi.CreationTimeUtc; set => _fi.CreationTimeUtc = value; }
        public DateTime LastWriteTimeUtc { get => _fi.LastWriteTimeUtc; set => _fi.LastWriteTimeUtc = value; }
    }

    [Extension(".7z.arius")]
    internal class EncryptedChunkFile : LocalFile, IEncryptedChunkFile
    {
        public EncryptedChunkFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }




    [Extension(".manifest.arius", encryptedType: typeof(LocalEncryptedManifestFile))]
    internal class LocalManifestFile : LocalFile, IManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalManifestFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }
    [Extension(".manifest.7z.arius", decryptedType: typeof(LocalManifestFile))]
    internal class LocalEncryptedManifestFile : LocalFile, IEncryptedManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalEncryptedManifestFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }

    


}