using System.ComponentModel.DataAnnotations;
using ProtoBuf;

namespace Codify.GrpcCodeFirstContractGuard.TestServer.Models;

[ProtoContract]
public class HelloCodeFirstResponse
{
    [ProtoMember(1)]
    [Required]
    public required string Name { get; init; }
}