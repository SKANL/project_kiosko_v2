using Nodus.Judge.Application.Interfaces.Services;

namespace Nodus.Judge.Infrastructure.Settings;

public sealed class ProjectScanBuffer : IProjectScanBuffer
{
    public string? PendingQr { get; set; }
}
