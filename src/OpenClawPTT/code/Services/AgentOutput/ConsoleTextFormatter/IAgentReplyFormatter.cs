namespace OpenClawPTT;

/// <summary>
/// Interface for streaming agent reply formatters with word wrap and right margin indent.
/// </summary>
public interface IAgentReplyFormatter
{
    /// <summary>
    /// Process a delta chunk and write formatted output.
    /// </summary>
    void ProcessDelta(string delta);

    /// <summary>
    /// Flush any remaining word buffer and finish the reply.
    /// </summary>
    void Finish();
}
