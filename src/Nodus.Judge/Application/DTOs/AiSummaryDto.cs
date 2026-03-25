using System.Collections.Generic;

namespace Nodus.Judge.Application.DTOs
{
    public sealed class AiSummaryDto
    {
        public string? Summary { get; set; }
        public List<string>? Bullets { get; set; }
    }
}
