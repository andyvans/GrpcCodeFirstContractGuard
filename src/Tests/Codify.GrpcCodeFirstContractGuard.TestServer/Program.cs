using Codify.GrpcCodeFirstContractGuard.TestServer.Services;
using ProtoBuf.Grpc.Server;

namespace Codify.GrpcCodeFirstContractGuard.TestServer;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .ConfigureServices(services =>
                    {
                        // Add Microsoft code first gRPC services
                        services.AddCodeFirstGrpc();
                        services.AddGrpc();
                    });

                webBuilder.Configure(app =>
                {
                    var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
                    if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

                    app.UseRouting();

                    app.UseEndpoints(endpoints =>
                    {
                        // Map gRPC services
                        endpoints.MapGrpcService<GreeterCodeFirstService>();
                    });
                });
            });
    }
}