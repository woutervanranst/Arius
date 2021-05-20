using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Arius.Extensions
{
    internal static class DataFlowBlockExtensions
    {
        public static IDisposable LinkTo<TInput, TOutput>(
            this ISourceBlock<TInput> source,
            ITargetBlock<TOutput> target,
            DataflowLinkOptions linkOptions,
            Predicate<TInput> predicate,
            Func<TInput, TOutput> transform)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (linkOptions == null)
                throw new ArgumentNullException(nameof(linkOptions));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (transform == null)
                throw new ArgumentNullException(nameof(transform));

            var transformBlock = new TransformBlock<TInput, TOutput>(transform);

            source.LinkTo(transformBlock, linkOptions, predicate);
            return transformBlock.LinkTo(target, linkOptions);
        }

        public static IDisposable LinkTo<TInput, TOutput>(
            this ISourceBlock<TInput> source,
            ITargetBlock<TOutput> target,
            DataflowLinkOptions linkOptions,
            Func<TInput, TOutput> transform)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (linkOptions == null)
                throw new ArgumentNullException(nameof(linkOptions));
            if (transform == null)
                throw new ArgumentNullException(nameof(transform));

            var transformBlock = new TransformBlock<TInput, TOutput>(transform);

            source.LinkTo(transformBlock, linkOptions);
            return transformBlock.LinkTo(target, linkOptions);
        }

        public static void JoinCompletion(this IDataflowBlock target, Action beforeCompletion, Action beforeFault, params IDataflowBlock[] sources)
        {
            JoinCompletion(target, beforeCompletion, beforeFault, sources.Select(s => s.Completion).ToArray());
        }
        public static void JoinCompletion(this IDataflowBlock target, Action beforeCompletion, Action beforeFault, params Task[] sources)
        {
            Task.WhenAll(sources)
                .ContinueWith(_ =>
                {
                    beforeCompletion();
                    target.Complete();
                });

            foreach (var s in sources)
            {
                s.ContinueWith(t =>
                {
                    beforeFault();
                    target.Fault(t.Exception);
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
