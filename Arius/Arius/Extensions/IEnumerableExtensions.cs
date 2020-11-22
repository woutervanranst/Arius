﻿using System.Collections.Generic;
using System.Linq;

namespace Arius.Extensions
{
    internal static class IEnumerableExtensions
    {
        public static ParallelQuery<TSource> AsParallel<TSource>(this IEnumerable<TSource> source, int withDegreeOfParallelism)
        {
            return source.AsParallel().WithDegreeOfParallelism(withDegreeOfParallelism);
        }

        public static ParallelQuery<TSource> AsParallelWithParallelism<TSource>(this IEnumerable<TSource> source)
        {
            return source.AsParallel().WithDegreeOfParallelism(_degreeOfParallelism);
        }

        public static int _degreeOfParallelism = 1;
    }
}