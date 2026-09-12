using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace SimpleJson.Internal
{
    /// <summary>
    /// Reflection over a type - which properties it has, whether they're
    /// readable/writable, whether it has a parameterless constructor - is
    /// the same answer every single time for a given Type. Computing it with
    /// Type.GetProperties()/GetConstructor() on every Serialize/Deserialize
    /// call is the main cost in a naive reflection-based serializer; this
    /// cache computes it once per Type and reuses it for the lifetime of the
    /// process. See PERFORMANCE.md for a before/after benchmark.
    /// </summary>
    internal sealed class TypeMetadata
    {
        public Type Type { get; }
        public PropertyInfo[] ReadableProperties { get; }
        public PropertyInfo[] WritableProperties { get; }
        public ConstructorInfo? ParameterlessConstructor { get; }

        public TypeMetadata(Type type)
        {
            Type = type;

            var allProps = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0) // skip indexers
                .ToArray();

            ReadableProperties = allProps.Where(p => p.CanRead).ToArray();
            WritableProperties = allProps.Where(p => p.CanWrite).ToArray();
            ParameterlessConstructor = type.GetConstructor(Type.EmptyTypes);
        }
    }

    internal static class TypeMetadataCache
    {
        private static readonly ConcurrentDictionary<Type, TypeMetadata> Cache =
            new ConcurrentDictionary<Type, TypeMetadata>();

        public static TypeMetadata Get(Type type) =>
            Cache.GetOrAdd(type, t => new TypeMetadata(t));
    }
}
