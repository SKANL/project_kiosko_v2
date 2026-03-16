namespace Nodus.Judge.Infrastructure.Ble;

/// <summary>Canonical GATT UUIDs — Decision #25.</summary>
public static class NodusGatt
{
    public const string MainServiceUuid       = "6E6F6400-0000-0000-0000-000000000001";
    public const string DataWriteCharUuid     = "6E6F6400-0000-0000-0000-000000000002";
    public const string AckNotifyCharUuid     = "6E6F6400-0000-0000-0000-000000000003";
    public const string BootstrapReadCharUuid = "6E6F6400-0000-0000-0000-000000000004";  // Read — Admin/LINK-source only

    /// <summary>Minimum RSSI to consider a peer (Decision #3).</summary>
    public const int MinRssiDbm = -75;
}

/// <summary>BLE payload byte prefixes (Doc 02 §4).</summary>
public static class NodusPrefix
{
    public const byte Vote          = 0x01;
    public const byte MediaChunk    = 0x02;
    public const byte Bootstrap     = 0x03;
    public const byte ProjectSync   = 0x04;
    public const byte Blocklist     = 0x05;
    public const byte SyncRequest   = 0x06;
    public const byte EventChanged  = 0x07;   // Admin→Judge push — active event changed
    public const byte JudgeRegister = 0x08;   // Internal onboarding envelope
    public const byte Ack           = 0xA1;
    public const byte Nack          = 0xA2;
    public const byte ChunkedPayload = 0x09;
}
