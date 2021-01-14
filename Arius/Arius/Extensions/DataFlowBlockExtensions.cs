using System;
using System.Threading.Tasks.Dataflow;

namespace Arius.Extensions
{
    internal static class DataFlowBlockExtensions
    {
        /// <summary>Links the <see cref="T:System.Threading.Tasks.Dataflow.ISourceBlock`1" /> to the specified <see cref="T:System.Threading.Tasks.Dataflow.ITargetBlock`1" /> using the specified filter.</summary>
        /// <returns>An <see cref="T:System.IDisposable" /> that, upon calling Dispose, will unlink the source from the target.</returns>
        /// <param name="source">The source from which to link.</param>
        /// <param name="target">The <see cref="T:System.Threading.Tasks.Dataflow.ITargetBlock`1" /> to which to connect the source.</param>
        /// <param name="linkOptions">One of the enumeration values that specifies how to configure a link between dataflow blocks.</param>
        /// <param name="predicate">The filter a message must pass in order for it to propagate from the source to the target.</param>
        /// <param name="transform"></param>
        /// <typeparam name="TInput"></typeparam>
        /// <typeparam name="TOutput">Specifies the type of data contained in the source.</typeparam>
        /// <exception cref="T:System.ArgumentNullException">The <paramref name="source" /> is null (Nothing in Visual Basic).-or-The <paramref name="target" /> is null (Nothing in Visual Basic).-or-The <paramref name="linkOptions" /> is null (Nothing in Visual Basic).-or-The <paramref name="predicate" /> is null (Nothing in Visual Basic).</exception>
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

            //DataflowBlock.FilteredLinkPropagator<TOutput> filteredLinkPropagator = new DataflowBlock.FilteredLinkPropagator<TOutput>(source, target, predicate);
            //return source.LinkTo((ITargetBlock<TOutput>)filteredLinkPropagator, linkOptions);
        }


    }
}
