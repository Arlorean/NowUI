using System;

namespace NowUI.TrimmingConsumer;

// No compile-time dependency on the configured type's assembly. This represents
// an application receiving an assembly-qualified type name from external config.
public static class ConfiguredConsumer
{
    public static Type Find(string configuredAssemblyQualifiedName) =>
        Type.GetType(configuredAssemblyQualifiedName, throwOnError: true);
}
