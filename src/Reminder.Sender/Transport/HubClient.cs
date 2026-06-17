using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Reminder.Protocol;
using Reminder.Protocol.Dtos;

namespace Reminder.Sender.Transport;

/// <summary>
/// 把加密信封 POST 到 Hub。短超时、fire-and-forget 风格。
/// 自签 TLS 通过指纹 pinning 信任（仅接受指纹匹配的 Hub 证书，防 LAN 内 MITM/冒充）。
/// </summary>
public sealed class HubClient
{
    private readonly HttpClient _http;
    private readonly string _hub;

    public HubClient(string hub, string? pinnedThumbprint = null)
    {
        _hub = hub.TrimEnd('/');

        var handler = new HttpClientHandler();
        // 直连 Hub，绝不走系统/环境变量代理：Hub 是 LAN/回环地址，若经代理(如 Clash 的
        // HTTP_PROXY=127.0.0.1:7897)转发会被拦/重置，导致 TLS 握手 "unexpected EOF"。
        handler.UseProxy = false;
        if (!string.IsNullOrEmpty(pinnedThumbprint))
        {
            string pin = pinnedThumbprint.Replace(":", "").Replace(" ", "").Trim();
            // SHA-256 指纹 pinning（对 cert.RawData 取 SHA-256），与接收端 SelfSignedCert.Sha256Thumbprint 一致。
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && string.Equals(
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)),
                    pin, StringComparison.OrdinalIgnoreCase);
        }
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public Task<(bool ok, string status)> SendReminder(EncryptedEnvelope env) => Post("/v1/reminders", env);
    public Task<(bool ok, string status)> SendAck(EncryptedEnvelope env) => Post("/v1/ack", env);

    public async Task<(bool ok, string status, EnrollResponse? response)> EnrollAsync(EnrollRequest req)
    {
        try
        {
            string json = ProtocolJson.ToJson(req);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(_hub + "/v1/enroll", content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, $"{(int)resp.StatusCode} {TrimStatus(body)}", null);
            return (true, "ok", ProtocolJson.FromJson<EnrollResponse>(body));
        }
        catch (Exception e)
        {
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            return (false, $"{e.Message} :: 内层 {inner.GetType().Name}: {inner.Message}", null);
        }
    }

    /// <summary>心跳：GET /v1/health（含 TLS 指纹 pinning），用于探测接收端是否可达。</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            using var resp = await _http.GetAsync(_hub + "/v1/health").ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<(bool ok, string status)> Post(string path, EncryptedEnvelope env)
    {
        try
        {
            string json = ProtocolJson.ToJson(env);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(_hub + path, content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, $"{(int)resp.StatusCode} {TrimStatus(body)}");
        }
        catch (Exception e)
        {
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            return (false, $"{e.Message} :: 内层 {inner.GetType().Name}: {inner.Message}");
        }
    }

    private static string TrimStatus(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 500 ? text : text[..500] + "…";
    }
}
