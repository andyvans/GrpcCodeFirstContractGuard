using System.Runtime.CompilerServices;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ProtoBuf.Grpc.Configuration;
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
    public static List<string> VerifyProtobufSchemaStable(
        IEnumerable<Type> grpcContractTypes,
        AppDomain? grpcServiceAppDomain = null,
        string protoFilePath = DefaultProtoFilePath,
        [CallerFilePath] string callerFilePath = "")
    {
        grpcServiceAppDomain ??= AppDomain.CurrentDomain;
        var persistedSchemas = GetPersistedSchemas(protoFilePath, callerFilePath);

        var schemaGenerator = new SchemaGenerator();
        var grpcService = typeof(IGrpcService);

        var contractNamespaces = grpcContractTypes.Select(t => t.Namespace);

        // Find all gRPC service interfaces in the same namespaces as the provided contract types
        var grpcServiceInterfaces = grpcServiceAppDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace != null && contractNamespaces.Contains(type.Namespace))
            .Where(type => type.IsInterface && !type.Name.Equals(grpcService.Name));

        var schemaDifferences = new List<string>();

        foreach (var grpcServiceInterface in grpcServiceInterfaces)
        {
            var newSchema = schemaGenerator.GetSchema(grpcServiceInterface);
            var currentSchemaFile = $"{grpcServiceInterface.Name}.proto";

            persistedSchemas.TryGetValue(currentSchemaFile, out var currentSchema);
            if (currentSchema == null)
            {
                throw new InvalidOperationException($"Persisted schema should be present for {currentSchemaFile}");
            }

            var diff = InlineDiffBuilder.Diff(currentSchema, newSchema);

            var differences = (from diffLine in diff.Lines.Where(d => d.Type != ChangeType.Unchanged)
                               let changeType = diffLine.Type switch
                               {
                                   ChangeType.Deleted => "-",
                                   ChangeType.Inserted => "+",
                                   ChangeType.Modified => "!",
                                   ChangeType.Imaginary => "?",
                                   _ => throw new ArgumentOutOfRangeException($"Unhandled change type: {diffLine.Type}")
                               }
                               select $"{changeType} {diffLine.Text}")
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
    public static List<string> GenerateResourceFiles(
        IEnumerable<Type> grpcContractTypes,
        AppDomain? grpcServiceAppDomain = null,
        string protoFilePath = DefaultProtoFilePath,
        [CallerFilePath] string callerFilePath = "")
    {
        grpcServiceAppDomain ??= AppDomain.CurrentDomain;

        var schemaGenerator = new SchemaGenerator();
        var grpcService = typeof(IGrpcService);

        var contractNamespaces = grpcContractTypes.Select(t => t.Namespace);

        var grpcServiceInterfaces = grpcServiceAppDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace != null && contractNamespaces.Contains(type.Namespace))
            .Where(type => type.IsInterface && !type.Name.Equals(grpcService.Name));

        var generatedFiles = new List<string>();

        foreach (var grpcServiceInterface in grpcServiceInterfaces)
        {
            var protoSchema = schemaGenerator.GetSchema(grpcServiceInterface);
            if (protoSchema == null) throw new InvalidOperationException($"Proto schema should be present for {grpcServiceInterface.Name}");

            // Write the generated schema to the project directory
            var protoPath = GetGeneratedProtoPath(protoFilePath, callerFilePath);
            var filePath = Path.Combine(protoPath, $"{grpcServiceInterface.Name}.proto");
            File.WriteAllText(filePath, protoSchema);
            generatedFiles.Add(filePath);
        }

        return generatedFiles;
    }

    private static string GetGeneratedProtoPath(string protoFilePath, string callerFilePath)
    {
        var callerDirectory = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Unable to determine caller directory.");
        var protoPath = Path.Combine(callerDirectory, protoFilePath);
        Directory.CreateDirectory(protoPath);
        return protoPath;
    }

    private static Dictionary<string, string> GetPersistedSchemas(string protoFilePath, string callerFilePath)
    {
        var typeToContract = new Dictionary<string, string>();
        var protoPath = GetGeneratedProtoPath(protoFilePath, callerFilePath);

        foreach (var file in Directory.EnumerateFiles(protoPath, "*.proto"))
        {
            typeToContract[file.Split('\\').Last()] = File.ReadAllText(file);
        }

        return typeToContract;
    }
}