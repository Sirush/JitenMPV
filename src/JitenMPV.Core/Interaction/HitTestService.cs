using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Interaction;

public sealed class HitTestService
{
    /// Weights vertical distance so a point in the padding between two lines resolves to the
    /// nearer line before horizontal position is considered at all.
    private const float LinePenalty = 10_000f;

    private List<WordRect> _layout = [];

    public void UpdateLayout(List<WordRect> layout) => _layout = layout;

    public WordRect? HitTest(double mouseX, double mouseY, int osdWidth, int osdHeight)
    {
        if (_layout.Count == 0 || osdHeight <= 0) return null;

        float scale = OverlayRenderer.ResY / osdHeight;
        float ox = (float)(mouseX * scale);
        float oy = (float)(mouseY * scale);

        WordRect? best = null;
        float bestScore = float.MaxValue;

        foreach (var rect in _layout)
        {
            if (ox < rect.HitX0 || ox > rect.HitX1 || oy < rect.HitY0 || oy > rect.HitY1)
                continue;

            // Zero inside the glyph box, else distance to it: a point on a word's ink always beats
            // a neighbour's padding, however narrow that neighbour is.
            float dx = Math.Max(Math.Max(rect.X - ox, ox - rect.Right), 0f);
            float dy = Math.Max(Math.Max(rect.Y - oy, oy - rect.Bottom), 0f);
            float score = dy * LinePenalty + dx;

            if (score < bestScore)
            {
                bestScore = score;
                best = rect;
            }
        }

        return best;
    }
}
