using System;
using System.Collections.Generic;

namespace AspNetCore.Dispatcher.Internal;

internal static class ExceptionTypeWalker
{
    /// <summary>
    /// Yields the exception type and each of its base types up to and including <see cref="Exception"/>,
    /// most derived first.
    /// </summary>
    public static IEnumerable<Type> GetExceptionTypes(Type? exceptionType)
    {
        while (exceptionType != null && exceptionType != typeof(object))
        {
            yield return exceptionType;
            exceptionType = exceptionType.BaseType;
        }
    }
}
