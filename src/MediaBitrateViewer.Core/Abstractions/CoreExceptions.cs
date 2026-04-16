namespace MediaBitrateViewer.Core.Abstractions;

public class FileProbeException : Exception
{
    public FileProbeException(string message) : base(message) { }
    public FileProbeException(string message, Exception inner) : base(message, inner) { }
}

public class FrameAnalysisException : Exception
{
    public FrameAnalysisException(string message) : base(message) { }
    public FrameAnalysisException(string message, Exception inner) : base(message, inner) { }
}
