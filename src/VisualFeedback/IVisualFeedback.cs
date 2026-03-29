using System;

namespace OpenClawPTT.VisualFeedback;

public interface IVisualFeedback : IDisposable
{
    void Show();
    void Hide();
}