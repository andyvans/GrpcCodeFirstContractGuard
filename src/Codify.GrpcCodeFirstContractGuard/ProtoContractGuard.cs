using System.Reflection;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ProtoBuf.Grpc.Reflection;

namespace Codify.GrpcCodeFirstContractGuard;

/// <summary>
///     Generates and validates .proto schemas from code-first gRPC service interfaces.
///     Detects schema changes that could break backwards compatibility with existing clients.
/// </summary>
public static class ProtoContractGuard
{
    private const string DefaultProtoFilePath = "ProtoContractGuard";

    /// <summary>
    ///     Compares the current code-first gRPC schema against persisted .proto baseline files
    ///     and returns a list of differences. An empty list indicates the schema is stable.
    /// </summary>
    /// <param name="grpcContractTypes">A sample of the gRPC contract types to verify. The namespace of these types will be used to find related gRPC service interfaces.</param>
    /// <param name="grpcAssemblies">The assemblies containing the gRPC services. If null, all loaded assemblies are used.</param>
    /// <param name="protoFilePath">The path to the .proto files.</param>
    /// <param name="callerFilePath">The file path of the caller. This is used to locate the .proto files relative to the caller's location e.g. the unit test.</param>
    /// <returns>A list of schema differences. An empty list indicates the schema is stable.</returns>
    public static List<string> VerifyProtobufSchemaStable(
        IEnumerable<Type> grpcContractTypes,
        IEnumerable<Assembly>? grpcAssemblies = null,
        string protoFilePath = DefaultProtoFilePath,
        [CallerFilePath] string callerFilePath = "")
    {
        var grpcServiceInterfaces = GetGrpcServiceInterfaces(grpcContractTypes, grpcAssemblies);
        var persistedSchemas = GetPersistedSchemas(protoFilePath, callerFilePath);
        var schemaGenerator = new SchemaGenerator();
        var schemaDifferences = new List<string>();

        foreach (var grpcServiceInterface in grpcServiceInterfaces)
        {
            var newSchema = schemaGenerator.GetSchema(grpcServiceInterface)
                ?? throw new InvalidOperationException($"Proto schema should be present for {grpcServiceInterface.Name}");

            var currentSchemaFile = $"{grpcServiceInterface.Name}.proto";

            persistedSchemas.TryGetValue(currentSchemaFile, out var currentSchema);
            if (currentSchema == null)
            {
                schemaDifferences.Add($"ProtoContractGuard {grpcServiceInterface.Name}: no baseline found. Run GenerateResourceFiles to create baseline for {currentSchemaFile}.");
                continue;
            }

            var diff = InlineDiffBuilder.Diff(currentSchema, newSchema);

            var maxPositionWidth = diff.Lines.Max(line => line.Position ?? 0).ToString().Length;
            var differences = diff.Lines
                    .Where(diffPiece => diffPiece.Type != ChangeType.Unchanged)
                    .Select(diffLine => new
                    {
                        DiffLine = diffLine,
                        ChangeType = diffLine.Type switch
                        {
                            ChangeType.Deleted => "-",
                            ChangeType.Inserted => "+",
                            ChangeType.Modified => "!",
                            _ => "?"
                        },
                        Position = diffLine.Position.HasValue ? $"{diffLine.Position.Value}".PadLeft(maxPositionWidth) : new string(' ', maxPositionWidth)
                    })
                    .Select(t => $"{t.Position}{t.ChangeType} {t.DiffLine.Text}")
                    .ToList();

            if (differences.Any())
            {
                schemaDifferences.Add($"ProtoContractGuard {grpcServiceInterface.Name} differences:");
                schemaDifferences.AddRange(differences);
            }
        }

        return schemaDifferences;
    }

    /// <summary>
    ///     Generates baseline .proto schema files from code-first gRPC service interfaces.
    ///     Run this when adding new services or after intentionally changing the schema.
    /// </summary>
    /// <param name="grpcContractTypes">A sample of the gRPC contract types to verify. The namespace of these types will be used to find related gRPC service interfaces.</param>
    /// <param name="grpcAssemblies">The assemblies containing the gRPC services. If null, all loaded assemblies are used.</param>
    /// <param name="protoFilePath">The path to the .proto files.</param>
    /// <param name="callerFilePath">The file path of the caller. This is used to locate the .proto files relative to the caller's location e.g. the unit test.</param>
    /// <returns>A list of generated .proto file paths.</returns>
    public static List<string> GenerateResourceFiles(
        IEnumerable<Type> grpcContractTypes,
        IEnumerable<Assembly>? grpcAssemblies = null,
        string protoFilePath = DefaultProtoFilePath,
        [CallerFilePath] string callerFilePath = "")
    {
        var grpcServiceInterfaces = GetGrpcServiceInterfaces(grpcContractTypes, grpcAssemblies);
        var schemaGenerator = new SchemaGenerator();
        var generatedFiles = new List<string>();
        var protoPath = GetGeneratedProtoPath(protoFilePath, callerFilePath);

        foreach (var grpcServiceInterface in grpcServiceInterfaces)
        {
            var protoSchema = schemaGenerator.GetSchema(grpcServiceInterface)
                ?? throw new InvalidOperationException($"Proto schema should be present for {grpcServiceInterface.Name}");

            // Write the generated schema to the project directory
            var filePath = Path.Combine(protoPath, $"{grpcServiceInterface.Name}.proto");
            File.WriteAllText(filePath, protoSchema);
            generatedFiles.Add(filePath);
        }

        return generatedFiles;
    }

    /// <summary>
    ///    Retrieves all gRPC service interfaces from the specified assemblies that match the namespaces of the provided contract types.
    /// </summary>
    private static IEnumerable<Type> GetGrpcServiceInterfaces(
        IEnumerable<Type> grpcContractTypes,
        IEnumerable<Assembly>? grpcAssemblies)
    {
        var contractNamespaces = new HashSet<string>(grpcContractTypes.Select(t => t.Namespace).Where(ns => ns != null)!);
        grpcAssemblies ??= AppDomain.CurrentDomain.GetAssemblies();

        return grpcAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace != null && contractNamespaces.Contains(type.Namespace))
            .Where(type => type.IsInterface && type.GetCustomAttribute<ServiceContractAttribute>() != null);
    }

    /// <summary>
    ///     Determines the path to the generated .proto files based on the caller's file path and the specified proto file path.
    /// </summary>
    private static string GetGeneratedProtoPath(string protoFilePath, string callerFilePath)
    {
        var callerDirectory = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Unable to determine caller directory.");
        var protoPath = Path.Combine(callerDirectory, protoFilePath);
        Directory.CreateDirectory(protoPath);
        return protoPath;
    }

    /// <summary>
    ///     Loads the persisted .proto schemas from the specified path and returns a mapping of service interface names to their corresponding schema content.
    /// </summary>
    private static Dictionary<string, string> GetPersistedSchemas(string protoFilePath, string callerFilePath)
    {
        var typeToContract = new Dictionary<string, string>();
        var protoPath = GetGeneratedProtoPath(protoFilePath, callerFilePath);

        foreach (var file in Directory.EnumerateFiles(protoPath, "*.proto"))
        {
            typeToContract[Path.GetFileName(file)] = File.ReadAllText(file);
        }

        return typeToContract;
    }
}