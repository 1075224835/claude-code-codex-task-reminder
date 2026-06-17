using System.Security.Cryptography;
using Reminder.Protocol;
using Reminder.Protocol.Crypto;
using Reminder.Protocol.Dtos;
using Reminder.Protocol.Types;

// 极简、零依赖的断言式测试运行器（离线环境无法还原 xunit）。
int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else    { failed++; Console.WriteLine($"  FAIL  {name}"); }
}
void Throws<TEx>(string name, Action act) where TEx : Exception
{
    try { act(); failed++; Console.WriteLine($"  FAIL  {name} (期望抛出 {typeof(TEx).Name} 但未抛)"); }
    catch (TEx) { passed++; Console.WriteLine($"  PASS  {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"  FAIL  {name} (抛出了 {e.GetType().Name})"); }
}

Console.WriteLine("== Reminder.Protocol 测试 ==");

// 固定输入，便于复现 / 生成跨实现向量。
byte[] master = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
byte[] token  = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
const string kid = "ws1";
const string did = "dev-A";
long ts = 1_750_000_000;
byte[] nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();

byte[] key = KeyDerivation.DeriveMessageKey(master, token, did);
Check("派生密钥长度=32", key.Length == 32);

// HKDF 确定性 & 设备隔离
byte[] key2 = KeyDerivation.DeriveMessageKey(master, token, did);
byte[] keyOther = KeyDerivation.DeriveMessageKey(master, token, "dev-B");
Check("HKDF 确定性（同输入同输出）", key.AsSpan().SequenceEqual(key2));
Check("HKDF 设备隔离（不同 did 不同密钥）", !key.AsSpan().SequenceEqual(keyOther));

var reminder = new ReminderMessage
{
    Id = "fixed-id-0001",
    Type = ReminderTypes.NeedsInput,
    Host = "DESKTOP-ABC",
    Project = "全屏提醒",
    Cwd = @"C:\Users\10752\Desktop\CC-projects\全屏提醒",
    SessionId = "sess-123",
    Agent = "claude_code",
    Title = "需要确认",
    Detail = "是否继续？",
    CreatedAt = "2026-06-16T00:00:00.000Z",
    Nonce = "AAAAAAAAAAAAAAAAAAAAAA==",
};

// 往返
var env = EnvelopeCrypto.Encrypt(reminder, kid, did, key, ts, nonce);
var back = EnvelopeCrypto.Decrypt<ReminderMessage>(env, key);
Check("加解密往返：type 一致", back.Type == reminder.Type);
Check("加解密往返：detail 一致", back.Detail == reminder.Detail);
Check("加解密往返：project 一致", back.Project == reminder.Project);
Check("加解密往返：cwd 一致", back.Cwd == reminder.Cwd);

// 确定性（同 nonce → 同密文/标签）
var env2 = EnvelopeCrypto.Encrypt(reminder, kid, did, key, ts, nonce);
Check("确定性：同 nonce 产生相同密文", env.Ct == env2.Ct && env.Tag == env2.Tag);

// 篡改头部（ts）→ AAD 不符 → 解密失败
var tamperedTs = env with { Ts = ts + 1 };
Throws<CryptographicException>("篡改 ts 应解密失败", () => EnvelopeCrypto.Decrypt<ReminderMessage>(tamperedTs, key));

// 篡改密文 → 标签校验失败
byte[] ctBytes = Convert.FromBase64String(env.Ct);
ctBytes[0] ^= 0xFF;
var tamperedCt = env with { Ct = Convert.ToBase64String(ctBytes) };
Throws<CryptographicException>("篡改密文应解密失败", () => EnvelopeCrypto.Decrypt<ReminderMessage>(tamperedCt, key));

// 错误密钥（不同设备）→ 解密失败
Throws<CryptographicException>("错误密钥应解密失败", () => EnvelopeCrypto.Decrypt<ReminderMessage>(env, keyOther));

// 防重放守卫
var guard = new ReplayGuard(windowSeconds: 120);
long now = ts;
Check("首次消息通过", guard.Check(did, env.Nonce, ts, now, out _));
Check("重复 nonce 被拒", !guard.Check(did, env.Nonce, ts, now, out _));
Check("时间戳超窗被拒", !guard.Check(did, "other-nonce", ts, now + 1000, out _));
Check("窗口内新 nonce 通过", guard.Check(did, "other-nonce", ts, now, out _));

// Ack 也走同一信封
var ack = new AckMessage { Id = reminder.Id, Did = did, AckAt = "2026-06-16T00:01:00.000Z" };
var ackEnv = EnvelopeCrypto.Encrypt(ack, kid, did, key, ts + 1);
var ackBack = EnvelopeCrypto.Decrypt<AckMessage>(ackEnv, key);
Check("Ack 往返：id 一致", ackBack.Id == reminder.Id);

// 渲染占位符
string rendered = reminder.Render(ReminderTypes.DefaultTemplate(ReminderTypes.NeedsInput));
Check("模板渲染替换 {host}", rendered.Contains("DESKTOP-ABC") && !rendered.Contains("{host}"));
Check("模板渲染替换 {path}", reminder.Render("{path}").Contains(@"CC-projects\全屏提醒"));

// 配对码容错解析
var pb = new ProvisioningBlob { Hub = "https://192.168.1.3:8740", Kid = "ws1", Did = "d-abc123", Token = "AAAABBBB", Master = "CCCCDDDD", CertThumbprint = "D1C6", Kind = "sender" };
string pcode = pb.Encode();
Check("配对码：干净解析", ProvisioningBlob.Decode(pcode).Did == "d-abc123");
string wrapped = pcode.Insert(pcode.Length / 2, "\r\n   \t");
Check("配对码：含换行/空格仍可解析", ProvisioningBlob.Decode(wrapped).Did == "d-abc123");
string block = $"Hub 地址: {pb.Hub}\r\n设备 ID: {pb.Did}\r\n配对码（含密钥，勿外传）：\r\n\r\n{pcode}\r\n\r\n在发送端机器上…";
Check("配对码：整段说明也能提取", ProvisioningBlob.Decode(block).Did == "d-abc123");
Throws<FormatException>("配对码：垃圾输入应报错", () => ProvisioningBlob.Decode("这不是配对码 hello world"));

var newPb = new ProvisioningBlob { Hub = "https://192.168.1.3:8740", Kid = "ws1", Did = "d-new", EnrollSecret = Convert.ToBase64String(token), CertThumbprint = "D1C6", Kind = "sender" };
string newPbJson = ProtocolJson.ToJson(newPb);
Check("新配对码：不输出 master/token/msg_key", !newPbJson.Contains("\"master\"") && !newPbJson.Contains("\"token\"") && !newPbJson.Contains("\"msg_key\""));
Check("新配对码：enroll_secret 可解析", ProvisioningBlob.Decode(newPb.Encode()).EnrollSecret == Convert.ToBase64String(token));

var nonceWithWhitespace = env with { Nonce = env.Nonce.Insert(4, " ") };
Throws<FormatException>("信封 nonce 必须是规范 base64", () => EnvelopeCrypto.Decrypt<ReminderMessage>(nonceWithWhitespace, key));

// 打印跨实现向量（供安卓端对齐 docs/PROTOCOL.md）
Console.WriteLine();
Console.WriteLine("== 跨实现测试向量（安卓须复现） ==");
Console.WriteLine($"master(hex) = {Convert.ToHexString(master)}");
Console.WriteLine($"token(hex)  = {Convert.ToHexString(token)}");
Console.WriteLine($"did         = {did}");
Console.WriteLine($"kid         = {kid}");
Console.WriteLine($"ts          = {ts}");
Console.WriteLine($"nonce(hex)  = {Convert.ToHexString(nonce)}");
Console.WriteLine($"msgKey(hex) = {Convert.ToHexString(key)}");
Console.WriteLine($"plaintext   = {ProtocolJson.ToJson(reminder)}");
Console.WriteLine($"ct(b64)     = {env.Ct}");
Console.WriteLine($"tag(b64)    = {env.Tag}");

Console.WriteLine();
Console.WriteLine($"结果：{passed} 通过, {failed} 失败");
return failed == 0 ? 0 : 1;
