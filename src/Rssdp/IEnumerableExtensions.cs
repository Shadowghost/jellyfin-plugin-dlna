using System;
using System.Collections.Generic;
using System.Linq;

namespace Rssdp.Infrastructure
{
    internal static class IEnumerableExtensions
    {
        public static IEnumerable<T> SelectManyRecursive<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> selector)
        {
            ArgumentNullException.ThrowIfNull(source);

            ArgumentNullException.ThrowIfNull(selector);

            return !source.Any() ? source :
                source.Concat(
                    source
                    .SelectMany(i => selector(i).EmptyIfNull())
                    .SelectManyRecursive(selector)
                );
        }

        public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T> source)
        {
            return source ?? [];
        }
    }
}
