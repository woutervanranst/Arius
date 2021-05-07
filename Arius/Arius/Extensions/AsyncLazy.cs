using System;
using System.Threading.Tasks;

namespace Arius.Extensions
{
    public class AsyncLazy<T> : Lazy<Task<T>>
    {
        public AsyncLazy(Func<T> valueFactory) : base(() => Task.Factory.StartNew(valueFactory))
        {
        }

        public AsyncLazy(Func<Task<T>> taskFactory) : base(() => Task.Factory.StartNew(() => taskFactory()).Unwrap())
        {
        }

        public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter() { return Value.GetAwaiter(); }
    }
}