using System.Threading;
using System.Threading.Tasks;
using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Application.Interfaces.Services;

public interface IGroqService
{
    /// <summary>
    /// Sends a raw prompt to Groq and returns the raw text output.
    /// </summary>
    Task<Result<string>> GenerateAsync(string prompt, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Generates a strict JSON summary with shape { summary: string, bullets: string[] }.
    /// </summary>
    Task<Result<string>> GenerateStructuredSummaryAsync(string projectJson, string? model = null, CancellationToken ct = default);
}
