using System;
using System.CommandLine;

namespace Arius.CommandLine
{
    internal interface ICliCommand
    {
        Command GetCommand();
    }
}
