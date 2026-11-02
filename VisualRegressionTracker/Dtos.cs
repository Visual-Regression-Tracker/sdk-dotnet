using System.Collections.Generic;

namespace VisualRegressionTracker
{
    internal record CreateBuildDto(string BranchName, string Project, string CiBuildId);

    internal record ModifyBuildDto(bool IsRunning);

    internal record BuildDto(string Id, string ProjectId);

    internal record CreateTestRequestDto
    {
        public string Name { get; init; }
        public string ImageBase64 { get; init; }
        public string BuildId { get; init; }
        public string ProjectId { get; init; }
        public string BranchName { get; init; }
        public string Os { get; init; }
        public string Browser { get; init; }
        public string Viewport { get; init; }
        public string Device { get; init; }
        public string CustomTags { get; init; }
        public double? DiffTollerancePercent { get; init; }
        public string Comment { get; init; }
        public IEnumerable<IgnoreAreaDto> IgnoreAreas { get; init; }
        public string SourceBranch { get; init; }
        public bool? Merge { get; init; }
    }

    internal record TestRunResultDto(string Status, string Url, string ImageName, string DiffName, string BaselineName);

    internal record ErrorDto(string Message);
}
