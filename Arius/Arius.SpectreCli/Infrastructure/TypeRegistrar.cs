using System;
using Arius.CliSpectre.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Spectre.Console.Examples
{
    public sealed class TypeRegistrar : ITypeRegistrar
    {
        public TypeRegistrar(IServiceCollection builder)
        {
            this.builder = builder;
        }
        private readonly IServiceCollection builder;


        public ITypeResolver Build()
        {
            return new TypeResolver(builder.BuildServiceProvider());
        }

        public void Register(Type service, Type implementation)
        {
            builder.AddSingleton(service, implementation);
        }

        public void RegisterInstance(Type service, object implementation)
        {
            builder.AddSingleton(service, implementation);
        }

        public void RegisterLazy(Type service, Func<object> func)
        {
            if (func is null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            builder.AddSingleton(service, _ => func());
        }
    }
}