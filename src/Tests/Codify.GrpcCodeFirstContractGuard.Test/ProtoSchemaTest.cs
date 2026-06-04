using AwesomeAssertions;
using Codify.GrpcCodeFirstContractGuard.TestServer.Models;
using Xunit;

namespace Codify.GrpcCodeFirstContractGuard.Test;

public class ProtoSchemaTest
{
    [Fact]
    public void GenerateProtoSchema()
    {
        var generatedFiles = ProtoContractGuard.GenerateResourceFiles(
        [
            typeof(IGreeterCodeFirst)
        ]);

        generatedFiles.Should().HaveCount(1);
        generatedFiles.First().Should().EndWith(Path.Combine("ProtoContractGuard", "IGreeterCodeFirst.proto"));
    }

    [Fact]
    public void VerifyProtobufSchema_Stable()
    {
        var schemaDifferences = ProtoContractGuard.VerifyProtobufSchemaStable(
        [
            typeof(IGreeterCodeFirst)
        ]);

        schemaDifferences.Should().BeEmpty("Schema differences should be empty for stable protobuf schemas");
    }

    [Fact]
    public void VerifyProtobufSchema_Altered()
    {
        var schemaDifferences = ProtoContractGuard.VerifyProtobufSchemaStable(
        [
            typeof(IGreeterCodeFirst)
        ], protoFilePath: "ProtoContractGuard_Altered");

        schemaDifferences.Should().BeEquivalentTo(
            [
                "ProtoContractGuard IGreeterCodeFirst differences:",
                "-    string FirstName = 1;",
                "+    string Name = 1;",
                "-    int32 Length = 3;",
                "+    int32 Age = 3;",
                "+    repeated MoreInfo MoreInfoArray = 7;",
                "-    string Infomation = 1;",
                "+    string Info = 1;",
                "-    string Metadata = 3;",
                "-    rpc SayHowdy (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);",
                "+    rpc SayHello (HelloCodeFirstRequest) returns (HelloCodeFirstResponse);"
            ]);
    }
}
