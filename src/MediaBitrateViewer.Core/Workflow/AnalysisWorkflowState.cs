namespace MediaBitrateViewer.Core.Workflow;

public enum WorkflowStatus
{
    Idle,
    ProbingFile,
    SelectingStream,
    LoadingCachedAnalysis,
    RunningFrameAnalysis,
    FrameAnalysisCanceled,
    Ready,
    FfprobeMissing,
    NoVideoStreams,
    ProbeFailed,
    FrameAnalysisFailed
}
