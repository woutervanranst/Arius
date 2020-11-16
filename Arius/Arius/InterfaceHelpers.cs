using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;


public static class Helpers
{
    public static bool Implements<I>(this Type type, I @interface) where I : class
    {
        // https://stackoverflow.com/questions/503263/how-to-determine-if-a-type-implements-a-specific-generic-interface-type


        if (((@interface as Type) == null) || !(@interface as Type).IsInterface)
            throw new ArgumentException("Only interfaces can be 'implemented'.");

        return (@interface as Type).IsAssignableFrom(type);
    }
}

