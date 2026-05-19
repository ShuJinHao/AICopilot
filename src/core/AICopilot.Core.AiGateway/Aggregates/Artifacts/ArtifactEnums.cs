namespace AICopilot.Core.AiGateway.Aggregates.Artifacts;

public enum ArtifactWorkspaceStatus
{
    Draft = 0,
    Active = 1,
    Finalized = 2,
    Archived = 3
}

public enum ArtifactType
{
    Folder = 0,
    Chart = 1,
    Markdown = 2,
    Html = 3,
    Pdf = 4,
    Pptx = 5,
    Xlsx = 6,
    Csv = 7,
    Json = 8,
    Image = 9,
    Log = 10
}

public enum ArtifactStatus
{
    Draft = 0,
    Reviewing = 1,
    Approved = 2,
    Final = 3,
    Rejected = 4,
    Deleted = 5
}
