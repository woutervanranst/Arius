using System;

namespace Arius.Core.Models
{
    internal record PointerFileEntry
    {
        internal BinaryHash BinaryHash { get; init; }
        public string RelativeName { get; init; }

        /// <summary>
        /// Version (in Universal Time)
        /// </summary>
        public DateTime VersionUtc { get; init; }
        public bool IsDeleted { get; init; }
        public DateTime? CreationTimeUtc { get; init; }
        public DateTime? LastWriteTimeUtc { get; init; }
    }
}