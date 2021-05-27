//using System;

//namespace Arius.Extensions
//{
//    public static class CommandHandlerExtensions
//    {
//        public static System.CommandLine.Invocation.ICommandHandler Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, System.Threading.Tasks.Task<int>> action) => System.CommandLine.Binding.HandlerDescriptor.FromDelegate(action).GetCommandHandler();
//        public static System.CommandLine.Invocation.ICommandHandler Create<T1, T2, T3, T4, T5, T6, T7, T8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, System.Threading.Tasks.Task<int>> action) => System.CommandLine.Binding.HandlerDescriptor.FromDelegate(action).GetCommandHandler();
//    }
//}