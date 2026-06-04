# GRPC Code-First Contract Guard

Schema validation for gRPC Code-First services in .NET. Generates `.proto` schema files from your code-first service interfaces and detects backwards-incompatible changes before they reach production.

Code-first gRPC uses .NET types to define service and message contracts and is a very convenient choice when your entire ecosystem uses .NET. Instead of the schema being persisted in a `.proto` file, the schema is defined in the .NET types. This is a great way to define the schema as it allows for easy updates and refactoring via C# tooling.

However, when refactoring code or modifying gRPC models it can be difficult to guarantee that the schema is still backwards compatible. It is not always obvious if the schema has changed in a way that is not backwards compatible with existing clients. Furthermore, unit tests that start up the gRPC API host and perform operations will also be using the latest code-first models. As a result the tests will pass even if you rename a field or change a type in a way that is not backwards compatible.

**Proto schema generation to the rescue** — a handy feature of gRPC schema generation is that it can generate a `.proto` file from the .NET types. This package persists those schemas and compares them on each test run, surfacing any differences so you can make an informed decision about whether the change is safe.

For more background, see [Automatic gRPC Schema Validation](https://www.codify.nz/automatic-grpc-schema-validation/).

## Packages

| Package | Description |
|---|---|
| [**Codify.GrpcCodeFirstContractGuard**](https://www.nuget.org/packages/Codify.GrpcCodeFirstContractGuard) | Generates and validates `.proto` schemas from code-first gRPC service interfaces |

## Getting Started

1. Install the NuGet package:

```shell
dotnet add package Codify.GrpcCodeFirstContractGuard
```

2. Add a test to generate the baseline `.proto` schema files. Mark it with `Explicit = true` so it doesn't run automatically — only run it manually when you need to create or update the baseline:

```csharp
[Fact(Explicit = true)]
public void GenerateProtoSchema()
{
    var generatedFiles = ProtoContractGuard.GenerateResourceFiles(
    [
        typeof(IGreeterCodeFirst)
    ]);

    generatedFiles.Should().HaveCount(1);
}
```

This generates `.proto` files into a `ProtoContractGuard` folder in the test project directory. Commit these files to source control — they serve as the baseline for future comparisons.

3. Add a test to verify that the schema hasn't changed. This test runs on every build and will fail if the schema has drifted:

```csharp
[Fact]
public void VerifyProtobufSchema_Stable()
{
    var schemaDifferences = ProtoContractGuard.VerifyProtobufSchemaStable(
    [
        typeof(IGreeterCodeFirst)
    ]);

    schemaDifferences.Should().BeEmpty("Schema differences should be empty for stable protobuf schemas");
}
```

If a schema change is detected, the test will fail with a list of differences:

```
IGreeterCodeFirst differences:
-    string FirstName = 1;
+    string Name = 1;
-    int32 Length = 3;
+    int32 Age = 3;
+    repeated MoreInfo MoreInfoArray = 7;
-    string Infomation = 1;
+    string Info = 1;
-    string Metadata = 3;
-    rpc SayHowdy (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
+    rpc SayHello (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
```

4. If the schema change is intentional, regenerate the baseline files by running `GenerateResourceFiles` again and commit the updated `.proto` files.

## How It Works

1. **Generate** — `GenerateResourceFiles` uses `protobuf-net.Grpc` to generate `.proto` schemas from your code-first service interfaces and writes them to disk.
2. **Verify** — `VerifyProtobufSchemaStable` generates the current schema in memory and compares it against the persisted `.proto` files using an inline diff.
3. **Review** — Any differences are returned as a list of diff lines, making it easy to assert in your tests and review in CI output.

Tests failing aren't necessarily indicative of a breaking change, but each failure should be manually inspected to verify that the change is safe to make.

## License

See [license](LICENSE) for details.
