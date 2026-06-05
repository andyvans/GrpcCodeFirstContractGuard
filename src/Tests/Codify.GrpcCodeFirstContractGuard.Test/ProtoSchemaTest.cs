using AwesomeAssertions;
using Codify.GrpcCodeFirstContractGuard.TestServer.Models;
using Xunit;

namespace Codify.GrpcCodeFirstContractGuard.Test;

public class ProtoSchemaTest
{
    [Fact]
    public void GenerateBaselineProtoSchema()
    {
        var generatedFiles = ProtoContractGuard.GenerateResourceFiles(
        [
            typeof(IGreeterCodeFirst)
        ]);

        generatedFiles.Should().HaveCount(1);
        generatedFiles.First().Should().EndWith(Path.Combine("ProtoContractGuard", "IGreeterCodeFirst.proto"));

        foreach (var generatedFile in generatedFiles)
        {
            File.Exists(generatedFile).Should().BeTrue($"Generated file {generatedFile} should exist");
            var protoSchema = File.ReadAllText(generatedFile);
            protoSchema.Should().Contain("syntax = \"proto3\";");
            protoSchema.Should().Contain("package Codify.GrpcCodeFirstContractGuard.TestServer.Models;");
        }
    }

    [Fact]
    public void VerifyProtoSchema_Stable()
    {
        var schemaDifferences = ProtoContractGuard.VerifyProtobufSchemaStable(
        [
            typeof(IGreeterCodeFirst)
        ]);

        schemaDifferences.Should().BeEmpty("Schema differences should be empty for stable protobuf schemas");
    }

    [Fact]
    public void VerifyProtoSchema_Altered()
    {
        var schemaDifferences = ProtoContractGuard.VerifyProtobufSchemaStable(
        [
            typeof(IGreeterCodeFirst)
        ], protoFilePath: "ProtoContractGuard_Altered");

        schemaDifferences.Should().BeEquivalentTo(
            [
                "ProtoContractGuard IGreeterCodeFirst differences:",
                "  -    string FirstName = 1;",
                " 6+    string Name = 1;",
                "  -    int32 Length = 3;",
                " 8+    int32 Age = 3;",
                "12+    repeated MoreInfo MoreInfoArray = 7;",
                "  -    string Information = 1;",
                "18+    string Info = 1;",
                "  -    string Metadata = 3;",
                "  -    rpc SayHowdy (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);",
                "22+    rpc SayHello (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);"
            ]);
    }
}
