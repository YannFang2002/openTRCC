namespace OpenTRCC.Probe;

internal static class ProbeExit
{
    public const int Ok = 0;
    public const int DeviceError = 1;
    public const int NotFound = 2;
    public const int Busy = 3;
    public const int AccessDenied = 4;
    public const int HandshakeFailed = 5;
    public const int WriteFailed = 6;
    public const int Usage = 64;
    public const int Cancelled = 130;
}
