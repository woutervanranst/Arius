using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Extensions
{
    class ConcurrentHashSet<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> dict = new();

        ///
        /// Summary:
        ///     Attempts to add the specified key and value to the System.Collections.Concurrent.ConcurrentDictionary`2.
        ///
        /// Parameters:
        ///   item:
        ///     The element to add.
        ///
        /// Returns:
        ///     true if the item was added to the ConcurrentHashSet
        ///     successfully; false if it already exists.
        ///
        /// Exceptions:
        ///   T:System.ArgumentNullException:
        ///     item is null.
        ///
        ///   T:System.OverflowException:
        ///     The hashset contains too many elements.
        public bool TryAdd(T item)
        {
            return dict.TryAdd(item, default);
        }
    }
}
