using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Arius.Extensions
{
    internal class ConsoleProgress
    {
        private readonly long _max;
        private long _current;
        private bool _finished;

        private readonly object _lock = new object();

        public ConsoleProgress(long max, TimeSpan wait, ILogger logger)
        {
            _max = max;
            _current = 0;

            Task.Run(async () =>
            {
                while (!_finished)
                {
                    logger.LogInformation($"{Math.Round(_current / ((float)_max) * 100)}%".PadLeft(4));

                    await Task.Delay(wait);
                }
            });


        }

        public void AddProgress(int i)
        {
            // https://github.com/a-luna/console-progress-bar/blob/master/ConsoleProgressBar/ConsoleProgressBar.cs


            lock (_lock)
            {
                _current += i;
            }
        }

        public void Finished() => _finished = true;
    }
}