//using Arius.Core.Extensions;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;

//namespace Arius.Core.Extensions
//{

//    [DebuggerStepThrough]
//    internal static class IEnumerableExtensions
//    {
//        public static ParallelQuery<TSource> AsParallel<TSource>(this IEnumerable<TSource> source, int withDegreeOfParallelism)
//        {
//            return source.AsParallel().WithDegreeOfParallelism(withDegreeOfParallelism);
//        }

//        public static ParallelQuery<TSource> AsParallelWithParallelism<TSource>(this IEnumerable<TSource> source)
//        {
//#if DEBUG
//            return source.AsParallel().WithDegreeOfParallelism(_degreeOfParallelism);
//#else
//            return source.AsParallel(); //.WithDegreeOfParallelism(_degreeOfParallelism);
//#endif
//        }

//        public static int _degreeOfParallelism = 1;
//    }
//}
