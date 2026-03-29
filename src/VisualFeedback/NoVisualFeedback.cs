namespace OpenClawPTT.VisualFeedback;

internal sealed class NoVisualFeedback : IVisualFeedback
{
    public void Show() { }
    public void Hide() { }
    public void Dispose() { }
}