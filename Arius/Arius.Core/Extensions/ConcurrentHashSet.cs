//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Arius.Core.Extensions
//{
//    internal class ConcurrentHashSet<T> where T : notnull
//    {
//        // https://github.com/dotnet/runtime/blob/main/src/libraries/System.Collections.Concurrent/src/System/Collections/Concurrent/ConcurrentDictionary.cs


//        public ConcurrentHashSet()
//        {
//            dict = new();
//        }
//        public ConcurrentHashSet(IEnumerable<T> collection)
//        {
//            dict = new(collection.Select(item => new KeyValuePair<T, byte>(item, default)));
//        }


//        private readonly ConcurrentDictionary<T, byte> dict;

//        ///
//        /// Summary:
//        ///     Attempts to add the specified key and value to the System.Collections.Concurrent.ConcurrentDictionary`2.
//        ///
//        /// Parameters:
//        ///   item:
//        ///     The element to add.
//        ///
//        /// Returns:
//        ///     true if the item was added to the ConcurrentHashSet
//        ///     successfully; false if it already exists.
//        ///
//        /// Exceptions:
//        ///   T:System.ArgumentNullException:
//        ///     item is null.
//        ///
//        ///   T:System.OverflowException:
//        ///     The hashset contains too many elements.
//        public bool TryAdd(T item)
//        {
//            try
//            {
//                return dict.TryAdd(item, default);

//            }
//            catch (Exception e)
//            {

//                throw;
//            }
            
//        }

//        public void Add(T item)
//        {
//            var r = TryAdd(item);

//            if (!r)
//                throw new ArgumentException("An element with the same key already exists");
//        }

//        public void AddRange(IEnumerable<T> items)
//        {
//            foreach (var item in items)
//                Add(item);
//        }

//        /// <summary>
//        /// Gets the number of element pairs contained in the ConcurrentHashSet
//        /// </summary>
//        public int Count => dict.Count;

//        public ICollection<T> Values => dict.Keys;
//    }
//}
