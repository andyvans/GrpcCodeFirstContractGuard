using AwesomeAssertions;
using Codify.GrpcCodeFirstContractGuard.TestServer.Models;
using Xunit;

namespace Codify.GrpcCodeFirstContractGuard.Test;

public class ProtoSchemaTest
{
    [Fact]
    public void GenerateBaselineFiles()
    {
        var generatedFiles = ProtoContractGuard.GenerateBaselineFiles(
        [
            typeof(IGreeterCodeFirst)
        ]);

        generatedFiles.Should().HaveCount(2);
        generatedFiles[0].Should().EndWith(Path.Combine("ProtoContractGuard", "IGreeterCodeFirst.proto"));
        generatedFiles[1].Should().EndWith(Path.Combine("ProtoContractGuard", "IGreeterCodeFirstV2.proto"));

        var protoSchema = File.ReadAllText(generatedFiles[0]);
        protoSchema.Should().Contain("syntax = \"proto3\";");
        protoSchema.Should().Contain("package Codify.GrpcCodeFirstContractGuard.TestServer.Models;");
    }

    [Fact]
    public void VerifyProtoSchema_Stable()
    {
        var schemaDifferences = ProtoContractGuard.CompareCurrentToBaseline(
        [
            typeof(IGreeterCodeFirst)
        ]);

        schemaDifferences.Should().BeNull("schema should be stable when code matches baseline");
    }

    [Fact]
    public void VerifyProtoSchema_Altered()
    {
        var schemaDifferences = ProtoContractGuard.CompareCurrentToBaseline(
        [
            typeof(IGreeterCodeFirst)
        ], protoFilePath: "ProtoContractGuard_Altered");

        schemaDifferences.Should().BeEquivalentTo("""
            [IGreeterCodeFirst.proto] ProtoContractGuard detected differences for IGreeterCodeFirst:
              -    string FirstName = 1;
             6+    string Name = 1;
              -    int32 Length = 3;
             8+    int32 Age = 3;
            12+    repeated MoreInfo MoreInfoArray = 7;
              -    string Information = 1;
            18+    string Info = 1;
              -    string Metadata = 3;
              -    rpc SayHowdy (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
            22+    rpc SayHello (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);
            [IGreeterCodeFirstV2.proto] ProtoContractGuard issue: No baseline found. Run GenerateBaselineFiles to create baseline for IGreeterCodeFirstV2.
            """);
    }
}
