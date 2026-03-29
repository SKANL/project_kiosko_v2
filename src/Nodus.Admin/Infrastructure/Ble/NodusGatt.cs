namespace Nodus.Admin.Infrastructure.Ble;

/// <summary>
/// Canonical GATT UUIDs — Decision #25.
/// These must match exactly between Admin and all Judge nodes.
/// </summary>
public static class NodusGatt
{
    public const string MainServiceUuid       = "6E6F6400-0000-0000-0000-000000000001";
    public const string DataWriteCharUuid     = "6E6F6400-0000-0000-0000-000000000002";  // WriteWithoutResponse
    public const string AckNotifyCharUuid     = "6E6F6400-0000-0000-0000-000000000003";  // Notify
    public const string BootstrapReadCharUuid = "6E6F6400-0000-0000-0000-000000000004";  // Read — Admin only
}

/// <summary>BLE payload byte prefixes (Doc 02 §4).</summary>
public static class NodusPrefix
{
    public const byte Vote         = 0x01;   // Vote/JSON            Judge→Admin
    public const byte MediaChunk   = 0x02;   // Media chunk          Judge→Admin (Mule Mode)
    public const byte Bootstrap    = 0x03;   // Bootstrap payload    Admin→Judge
    public const byte ProjectSync  = 0x04;   // Project sync         Admin→Judge
    public const byte Blocklist    = 0x05;   // Blocklist            Admin→LINK via Notify
    public const byte SyncRequest  = 0x06;   // Sync request         Judge→Admin (before Bootstrap read)
    public const byte EventChanged  = 0x07;   // Event changed        Admin→Judge via Notify (push)
    public const byte JudgeRegister = 0x08;   // Judge registration   Judge→Admin
    public const byte Ack          = 0xA1;   // ACK                  Admin→Judge via Notify
    public const byte Nack         = 0xA2;   // NACK (Reject)        Admin→Judge via Notify
    public const byte ChunkedPayload = 0x09; // L2CAP-like chunked data
    public const byte EventDiscoveryRequest = 0x0A; // Discover onboarding events Judge→Admin
    public const byte EventDiscovery        = 0x0B; // Discover onboarding events Admin→Judge
}
