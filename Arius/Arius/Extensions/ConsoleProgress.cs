using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Arius.Extensions
{
    internal class ConsoleProgress
    {
        private readonly long _max;
        private long _current;
        private DateTime start;
        private bool _finished;

        private readonly object _lock = new object();

        private readonly Action<long, long, double, DateTime?> _write;


        //public ConsoleProgress(long max, Action<long, long, double, DateTime?> write)
        //{
        //    _max = max;
        //    _current = 0;
        //    start = DateTime.Now;
        //    TimeSpan wait = TimeSpan.FromSeconds(1);

        //    Task.Run(async () =>
        //    {
        //        while (!_finished)
        //        {
        //            var eta = _current == 0 ? 
        //                (DateTime?)null : 
        //                DateTime.Now.AddSeconds((DateTime.Now - start).TotalSeconds / _current * _max);

        //            write(_current, _max, Math.Round(_current / ((float) _max) * 100), eta);

        //            await Task.Delay(wait);
        //        }
        //    });


        //}

        //public void AddProgress(int i)
        //{
        //    // https://github.com/a-luna/console-progress-bar/blob/master/ConsoleProgressBar/ConsoleProgressBar.cs


        //    lock (_lock)
        //    {
        //        _current += i;
        //    }
        //}

        //public void Finished() => _finished = true;

        public ConsoleProgress(long max, Action<long, long, double, DateTime?> write)
        {
            _max = max;
            _current = 0;
            start = DateTime.Now;
            _write = write;
            //TimeSpan wait = TimeSpan.FromSeconds(1);

            //Task.Run(async () =>
            //{
            //    while (!_finished)
            //    {
            //        var eta = _current == 0 ?
            //            (DateTime?)null :
            //            DateTime.Now.AddSeconds((DateTime.Now - start).TotalSeconds / _current * _max);

            //        write(_current, _max, Math.Round(_current / ((float)_max) * 100), eta);

            //        await Task.Delay(wait);
            //    }
            //});


        }

        public void AddProgress(int i)
        {
            // https://github.com/a-luna/console-progress-bar/blob/master/ConsoleProgressBar/ConsoleProgressBar.cs


            lock (_lock)
            {
                _current += i;

                var eta = _current == 0 ?
                                (DateTime?)null :
                                DateTime.Now.AddSeconds((DateTime.Now - start).TotalSeconds / _current * _max);
                _write(_current, _max, Math.Round(_current / ((float)_max) * 100), eta);
            }
        }

        public void Finished() => _finished = true;
    }
}