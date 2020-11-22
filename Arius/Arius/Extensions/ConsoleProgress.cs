using System;

namespace Arius.Extensions
{
    internal class ConsoleProgress
    {
        private readonly long _max;
        private long _current;

        private readonly object _lock = new object();

        public ConsoleProgress(long max)
        {
            _max = max;
            _current = 0;
        }

        public void AddProgress(int i)
        {
            lock (_lock)
            {
                _current += i;
                Console.Write($"\b\b\b\b{Math.Round(_current / ((float)_max) * 100)}%".PadLeft(4));

            }
        }
    }
}