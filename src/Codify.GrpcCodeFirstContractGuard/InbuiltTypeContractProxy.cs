using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.ServiceModel;
using ProtoBuf;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace Codify.GrpcCodeFirstContractGuard;

/// <summary>
///     protobuf-net https://github.com/protobuf-net/protobuf-net cannot add inbuilt types (e.g. string, int) to a schema model as messages.
///     protobuf-net.Grpc serializes such request/response values as a message with the value in field 1,
///     which is wire compatible with the google.protobuf wrapper types (e.g. StringValue, Int32Value).
///     This class creates a proxy service interface where inbuilt request/response types are replaced
///     with equivalent wrapper messages so the schema can be generated.
/// </summary>
internal static class InbuiltTypeContractProxy
{
    // Guards the caches and the dynamic module, which are shared across calls
    private static readonly Lock SyncRoot = new();

    // Original service interface -> emitted proxy interface
    private static readonly Dictionary<Type, Type> Proxies = new();

    // (proto package, CLR type) -> emitted wrapper message type
    private static readonly Dictionary<(string? Namespace, Type Type), Type> Wrappers = new();
    private static ModuleBuilder? _moduleBuilder;

    private const string GoogleProtobufPackage = "google.protobuf";
    private const string GoogleWrappersOrigin = "google/protobuf/wrappers.proto";

    // CLR types that have an equivalent message in google/protobuf/wrappers.proto
    private static readonly Dictionary<Type, string> GoogleWrapperNames = new()
    {
        [typeof(double)] = "DoubleValue",
        [typeof(float)] = "FloatValue",
        [typeof(long)] = "Int64Value",
        [typeof(ulong)] = "UInt64Value",
        [typeof(int)] = "Int32Value",
        [typeof(uint)] = "UInt32Value",
        [typeof(bool)] = "BoolValue",
        [typeof(string)] = "StringValue",
        [typeof(byte[])] = "BytesValue",
    };

    /// <summary>
    ///     Returns the service interface to use for schema generation. When the interface does not use any
    ///     inbuilt request/response types the original interface is returned.
    /// </summary>
    public static Type GetSchemaContract(Type serviceInterface, ServiceBinder binder)
    {
        var operations = GetOperations(serviceInterface, binder);
        if (!operations.Any(op => UsesInbuiltType(op.Method)))
        {
            return serviceInterface;
        }

        lock (SyncRoot)
        {
            if (!Proxies.TryGetValue(serviceInterface, out var proxy))
            {
                proxy = CreateProxy(serviceInterface, binder, operations);
                Proxies[serviceInterface] = proxy;
            }

            return proxy;
        }
    }

    /// <summary>
    ///     Fully qualifies google.protobuf wrapper types used in rpc signatures, as protobuf-net emits them unqualified.
    /// </summary>
    public static string QualifyWellKnownTypes(string schema)
    {
        if (!schema.Contains($"import \"{GoogleWrappersOrigin}\";"))
        {
            return schema;
        }

        // Only rpc lines are rewritten, e.g. "rpc X (StringValue) returns (stream Int32Value);"
        // becomes "rpc X (.google.protobuf.StringValue) returns (stream .google.protobuf.Int32Value);"
        var names = string.Join("|", GoogleWrapperNames.Values);
        return Regex.Replace(
            schema,
            $@"(?m)^(\s*rpc .*)$",
            line => Regex.Replace(line.Value, $@"\((stream )?({names})\)", $"($1.{GoogleProtobufPackage}.$2)"));
    }

    /// <summary>
    ///     Gets all operations on the interface (including inherited interfaces) with their resolved rpc names.
    /// </summary>
    private static List<(MethodInfo Method, string Name)> GetOperations(Type serviceInterface, ServiceBinder binder)
    {
        return new[] { serviceInterface }
            .Concat(serviceInterface.GetInterfaces())
            .SelectMany(type => type.GetMethods())
            .Select(method => (Method: method, IsOperation: binder.IsOperationContract(method, out var name), Name: name))
            .Where(op => op.IsOperation)
            .Select(op => (op.Method, op.Name!))
            .ToList();
    }

    private static bool UsesInbuiltType(MethodInfo method)
    {
        return method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)
            .Any(type => IsInbuiltType(UnwrapPayloadType(type)));
    }

    /// <summary>
    ///     Gets the payload type from Task, ValueTask or IAsyncEnumerable; other types are returned unchanged.
    /// </summary>
    private static Type UnwrapPayloadType(Type type)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return type;
    }

    /// <summary>
    ///     True for types protobuf-net serializes natively (string, int, Guid, DateTime, byte[] etc.), which cannot
    ///     be used as top-level messages in the schema. Enums and void are excluded.
    /// </summary>
    private static bool IsInbuiltType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(void) || underlying.IsEnum)
        {
            return false;
        }

        return RuntimeTypeModel.Default.CanSerializeBasicType(underlying);
    }

    /// <summary>
    ///     Emits a proxy interface that mirrors the service contract (same namespace, service name and operation names)
    ///     but with inbuilt request/response types replaced by wrapper message types.
    /// </summary>
    private static Type CreateProxy(Type serviceInterface, ServiceBinder binder, List<(MethodInfo Method, string Name)> operations)
    {
        var module = GetModuleBuilder();
        binder.IsServiceContract(serviceInterface, out var serviceName);

        // Keep the same namespace and name so the generated package and file layout match the original contract
        var typeBuilder = module.DefineType(
            $"{serviceInterface.Namespace}.{serviceInterface.Name}",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

        // Explicitly set the service name resolved from the original contract so the proto service name is unchanged
        typeBuilder.SetCustomAttribute(CreateAttribute<ServiceContractAttribute>(nameof(ServiceContractAttribute.Name), serviceName));

        foreach (var (method, operationName) in operations)
        {
            var parameters = method.GetParameters();

            // Interface methods must be public, abstract and virtual; parameter types (including CallContext/CancellationToken) are kept as-is
            var methodBuilder = typeBuilder.DefineMethod(
                method.Name,
                MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                SubstituteType(method.ReturnType, serviceInterface.Namespace),
                parameters.Select(p => SubstituteType(p.ParameterType, serviceInterface.Namespace)).ToArray());

            for (var i = 0; i < parameters.Length; i++)
            {
                methodBuilder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
            }

            // Preserve the resolved operation name so the rpc names are unchanged
            methodBuilder.SetCustomAttribute(CreateAttribute<OperationContractAttribute>(nameof(OperationContractAttribute.Name), operationName));
        }

        return typeBuilder.CreateType();
    }

    /// <summary>
    ///     Replaces an inbuilt type with its wrapper message type. Task, ValueTask and IAsyncEnumerable are
    ///     unwrapped and rebuilt so that e.g. Task&lt;int&gt; becomes Task&lt;Int32Value&gt;. Other types are returned unchanged.
    /// </summary>
    private static Type SubstituteType(Type type, string? ns)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>))
            {
                return definition.MakeGenericType(SubstituteType(type.GetGenericArguments()[0], ns));
            }
        }

        return IsInbuiltType(type) ? GetWrapperType(type, ns) : type;
    }

    /// <summary>
    ///     Gets or emits a wrapper message type with the inbuilt value in field 1.
    ///     Types with a google.protobuf equivalent (e.g. string, int) are emitted with that origin so the schema
    ///     imports google/protobuf/wrappers.proto instead of declaring the message. Other types (e.g. Guid, DateTime)
    ///     get a local {TypeName}Value message in the service's package.
    ///     Must be called while holding <see cref="SyncRoot"/>.
    /// </summary>
    private static Type GetWrapperType(Type type, string? ns)
    {
        // int? and int share the same wrapper name
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var isWellKnown = GoogleWrapperNames.TryGetValue(underlying, out var name);
        if (isWellKnown)
        {
            ns = GoogleProtobufPackage;
        }
        else
        {
            name = $"{underlying.Name}Value";
        }

        // Wrappers are cached per package so each CLR type name is only defined once in the dynamic module
        if (Wrappers.TryGetValue((ns, type), out var wrapper))
        {
            return wrapper;
        }

        var typeBuilder = GetModuleBuilder().DefineType(
            $"{ns}.{name}",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

        // A different Origin makes protobuf-net emit an import for the file instead of declaring the message
        typeBuilder.SetCustomAttribute(isWellKnown
            ? CreateAttribute<ProtoContractAttribute>(nameof(ProtoContractAttribute.Origin), GoogleWrappersOrigin)
            : CreateAttribute<ProtoContractAttribute>(nameof(ProtoContractAttribute.Name), null));

        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        // Field 1 named "value" matches the layout of the google.protobuf wrapper messages
        var field = typeBuilder.DefineField("value", type, FieldAttributes.Public);
        field.SetCustomAttribute(new CustomAttributeBuilder(typeof(ProtoMemberAttribute).GetConstructor([typeof(int)])!, [1]));

        wrapper = typeBuilder.CreateType();
        Wrappers[(ns, type)] = wrapper;
        return wrapper;
    }

    /// <summary>
    ///     Creates an attribute builder using the parameterless constructor, optionally setting a single string property.
    /// </summary>
    private static CustomAttributeBuilder CreateAttribute<TAttribute>(string propertyName, string? value)
        where TAttribute : Attribute
    {
        var constructor = typeof(TAttribute).GetConstructor(Type.EmptyTypes)!;
        if (value is null)
        {
            return new CustomAttributeBuilder(constructor, []);
        }

        return new CustomAttributeBuilder(constructor, [], [typeof(TAttribute).GetProperty(propertyName)!], [value]);
    }

    /// <summary>
    ///     Lazily creates the in-memory dynamic module that hosts all proxy and wrapper types.
    ///     Dynamic assemblies are excluded from service discovery in <see cref="ProtoContractGuard"/>.
    /// </summary>
    private static ModuleBuilder GetModuleBuilder()
    {
        return _moduleBuilder ??= AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("Codify.GrpcCodeFirstContractGuard.SchemaProxies"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Codify.GrpcCodeFirstContractGuard.SchemaProxies");
    }
}
