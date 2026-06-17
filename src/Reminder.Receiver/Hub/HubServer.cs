using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reminder.Protocol;
using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Receiver.Config;
using Reminder.Receiver.Logging;
using Reminder.Receiver.Stats;

namespace Reminder.Receiver.Hub;

/// <summary>内嵌的消息路由 Hub（Kestrel + 最小 API）。在后台启动，不阻塞 WPF UI 线程。</summary>
public sealed class HubServer
{
    private const long MaxEnvelopeBytes = 64 * 1024;
    private WebApplication? _app;

    public async Task StartAsync(ReceiverConfig cfg, byte[] masterKey, X509Certificate2 cert, ReminderRouter router, StatsStore stats, DeviceRegistry registry)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestBodySize = MaxEnvelopeBytes;
            o.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificate = cert;
                // 限定 TLS 1.2：自签证书私钥(经 CSP 导入)不支持 TLS 1.3 的 RSA-PSS 签名，
                // .NET 客户端默认尝试 1.3 会握手失败(server EOF)。1.2 + RSA-2048 + AES-GCM 已足够安全。
                https.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            });
        });

        var app = builder.Build();
        app.Urls.Clear();
        app.Urls.Add($"https://0.0.0.0:{cfg.HubPort}"); // 自签 TLS，绑定所有网卡供 LAN 发送端连接

        app.MapGet("/v1/health", () => Results.Json(new { ok = true, version = ProtocolConstants.Version }));

        app.MapPost("/v1/enroll", async (HttpContext ctx) =>
        {
            var req = await ReadEnrollRequest(ctx);
            if (req is null) return Results.StatusCode(400);

            byte[] secret;
            try { secret = EnvelopeCrypto.DecodeCanonicalBase64(req.Secret, ProtocolConstants.KeyBytes, "secret"); }
            catch { return Results.Json(new { status = "bad_secret" }, statusCode: StatusCodes.Status400BadRequest); }

            if (!registry.TryConsumeEnrollment(req.Did, secret, masterKey, out var msgKey, out var reason))
                return Results.Json(new { status = reason }, statusCode: StatusCodes.Status403Forbidden);

            return Results.Json(new EnrollResponse
            {
                Hub = "",
                Kid = cfg.Kid,
                Did = req.Did,
                MessageKey = Convert.ToBase64String(msgKey),
                CertThumbprint = cfg.CertThumbprint,
            }, ProtocolJson.Options);
        });

        app.MapPost("/v1/reminders", async (HttpContext ctx) =>
        {
            var env = await ReadEnvelope(ctx);
            if (env is null) return Results.StatusCode(400);
            var r = router.Ingest(env);
            return Results.Json(new { status = r.Status }, statusCode: r.Code);
        });

        app.MapPost("/v1/ack", async (HttpContext ctx) =>
        {
            var env = await ReadEnvelope(ctx);
            if (env is null) return Results.StatusCode(400);
            var r = router.IngestAck(env);
            return Results.Json(new { status = r.Status }, statusCode: r.Code);
        });

        app.MapGet("/v1/stats", (HttpContext ctx) =>
            IsLoopback(ctx.Connection.RemoteIpAddress)
                ? Results.Json(stats.Snapshot(), ProtocolJson.Options)
                : Results.StatusCode(StatusCodes.Status403Forbidden));

        await app.StartAsync();
        _app = app;
        Log.Info($"Hub 已监听 0.0.0.0:{cfg.HubPort}（{cfg.Scheme}）");
    }

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private static async Task<EncryptedEnvelope?> ReadEnvelope(HttpContext ctx)
    {
        if (ctx.Request.ContentLength is > MaxEnvelopeBytes) return null;
        try { return await JsonSerializer.DeserializeAsync<EncryptedEnvelope>(ctx.Request.Body, ProtocolJson.Options); }
        catch { return null; }
    }

    private static async Task<EnrollRequest?> ReadEnrollRequest(HttpContext ctx)
    {
        if (ctx.Request.ContentLength is > 8 * 1024) return null;
        try { return await JsonSerializer.DeserializeAsync<EnrollRequest>(ctx.Request.Body, ProtocolJson.Options); }
        catch { return null; }
    }

    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        return address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
    }
}
