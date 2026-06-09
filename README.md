# GRPC Code-First Contract Guard

Schema validation for gRPC Code-First services in .NET. Generates `.proto` schema files from your code-first service interfaces and detects backwards-incompatible changes before they reach production.

Code-first gRPC uses .NET types to define service and message contracts and is a very convenient choice when your entire ecosystem uses .NET. Instead of the schema being persisted in a `.proto` file, the schema is defined in the .NET types. This is a great way to define the schema as it allows for easy updates and refactoring via C# tooling. You can also define your validation with `System.ComponentModel.DataAnnotations` and validate with [GrpcCodeFirstDataAnnotations](https://github.com/andyvans/GrpcCodeFirstDataAnnotations).

However, when refactoring code / modifying gRPC models it can be difficult to guarantee that the schema is still backwards compatible. It is not always obvious if the schema has changed in a way that is not backwards compatible with existing clients. Your unit tests will be using the latest code-first models, so they'll pass even if you make a breaking schema change. This can lead to breaking changes being merged and deployed without anyone realizing the impact until clients start breaking in production.


**Proto schema generation to the rescue** — a handy feature of [gRPC schema generation](https://protobuf-net.github.io/protobuf-net.Grpc/createProtoFile.html) is that it can generate a `.proto` file from the .NET types. This package persists those schemas and compares them on each test run, surfacing any differences so you can make an informed decision about whether the change is safe.

For more background, see [Automatic gRPC Schema Validation](https://www.codify.nz/automatic-grpc-schema-validation/).

## Packages

| Package | Description |
|---|---|
| [**Codify.GrpcCodeFirstContractGuard**](https://www.nuget.org/packages/Codify.GrpcCodeFirstContractGuard) | Generates and validates `.proto` schemas from code-first gRPC service interfaces |

## Getting Started

1. Open your gRPC test project and install the NuGet package:

```shell
dotnet add package Codify.GrpcCodeFirstContractGuard
```

2. Add a test class called `ProtoContractGuardTest` (or similar) to your test project. This class will contain the tests for generating the baseline schema and verifying it.

3. Add a test to generate the baseline `.proto` schema files. Mark it with `Skip = "Run manually to regenerate baseline"` or for NUnit `[Explicit]` so it doesn't run automatically — only run it manually when you need to create or update the baseline.

4. Add a test to verify that the schema hasn't changed. This test runs on every build and will fail if the schema has drifted:

```csharp
public class ProtoContractGuardTest
{
    [Fact(Skip = "Run manually to regenerate baseline")] 
    public void GenerateProtoSchema()
    {
        var generatedFiles = ProtoContractGuard.GenerateBaselineFiles(
        [
            // One of the contract interfaces from each namespace. The rest will be discovered.
            typeof(IGreeterCodeFirst) 
        ]);

        // Assert that the expected number of files were generated
        generatedFiles.Should().NotBeEmpty("Baseline files should be generated");
    }

    [Fact]
    public void VerifyProtobufSchema()
    {
        var schemaDifferences = ProtoContractGuard.CompareCurrentToBaseline(
        [
            // One of the contract interfaces from each namespace. The rest will be discovered.
            typeof(IGreeterCodeFirst)
        ]);

        // Assert that there are no differences in the schema
        schemaDifferences.Should().BeNull("Schema differences should be empty for stable protobuf schemas");
    }
}
```

5. When you run the `GenerateProtoSchema` test, it will create folder structure like this in your test project:
```csharp
TestProject/
    ProtoContractGuardTest.cs
    ProtoContractGuard/
        IGreeterCodeFirst.proto
        IGreeterCodeFirstV2.proto
```


6. When the `VerifyProtobufSchema` test is run, it will compare the current schema against the baseline. If a schema change is detected, the test will fail with a list of differences. The differences include the line number, change type, and the text of the line. For example:

```
[IGreeterCodeFirst.proto] ProtoContractGuard detected differences for IGreeterCodeFirst
  -    string FirstName = 1;
 6+    string Name = 1;
  -    string Infomation = 1;
 8+    string Info = 1;
  -    rpc SayHowdy (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
22+    rpc SayHello (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
[IGreeterCodeFirstV2.proto] ProtoContractGuard issue: No baseline found. Run GenerateBaselineFiles to create baseline for IGreeterCodeFirstV2.
```

If the schema change is intentional, regenerate the baseline files by running `GenerateBaselineFiles` again and commit the updated `.proto` files. If not, then review your changes and fix any unintended breaking changes before committing.

## How It Works

1. **Generate** — `GenerateBaselineFiles` uses `protobuf-net.Grpc` to generate `.proto` schemas from your code-first service interfaces and writes them to disk.
2. **Verify** — `CompareCurrentToBaseline` generates the current schema in memory and compares it against the persisted `.proto` files using an inline diff.
3. **Review** — Any differences are returned as a list of diff lines, making it easy to assert in your tests and review in CI output.

A test failure indicates a schema change, but not all changes break compatibility. Each should be manually reviewed.

## License

See [license](LICENSE) for details.
