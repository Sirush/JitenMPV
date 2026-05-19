using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Interaction;

public sealed class HitTestService
{
    private const float YTolerance = 10f;

    private List<WordRect> _layout = [];

    public void UpdateLayout(List<WordRect> layout) => _layout = layout;

    public WordRect? HitTest(double mouseX, double mouseY, int osdWidth, int osdHeight)
    {
        if (_layout.Count == 0 || osdHeight <= 0) return null;

        float scale = OverlayRenderer.ResY / osdHeight;
        float ox = (float)(mouseX * scale);
        float oy = (float)(mouseY * scale);

        WordRect? best = null;
        float bestDist = float.MaxValue;

        foreach (var rect in _layout)
        {
            if (oy < rect.Y - YTolerance || oy > rect.Y + rect.Height + YTolerance)
                continue;

            float cx = rect.X + rect.Width / 2f;
            float dist = Math.Abs(ox - cx);

            if (dist < bestDist && ox >= rect.X - rect.Width * 0.3f
                                && ox <= rect.X + rect.Width * 1.3f)
            {
                bestDist = dist;
                best = rect;
            }
        }

        return best;
    }
}
