namespace YahtzeeSim.Analysis.Story;

/// <summary>Small inline SVG icons for the badges (24×24, drawn with currentColor), shared by the story and the leaderboard.</summary>
public static class BadgeIcons
{
    public static string Svg(string badgeId, int size = 24)
    {
        string body = badgeId switch
        {
            // Target
            "yahtzee-hunter" => "<circle cx=\"12\" cy=\"12\" r=\"9\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/><circle cx=\"12\" cy=\"12\" r=\"5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/><circle cx=\"12\" cy=\"12\" r=\"1.8\" fill=\"currentColor\"/>",
            // Rising steps
            "straight-shooter" => "<path d=\"M3 19h4v-4h4v-4h4V7h4V3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/><path d=\"M15 3h4v4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
            // Star
            "bonus-banker" => "<path d=\"M12 2.8l2.8 5.8 6.3.9-4.6 4.4 1.1 6.3L12 17.2l-5.6 3 1.1-6.3-4.6-4.4 6.3-.9z\" fill=\"currentColor\"/>",
            // Checked card
            "card-closer" => "<rect x=\"4\" y=\"3\" width=\"16\" height=\"18\" rx=\"2.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/><path d=\"M8 12.5l2.8 2.8L16.5 9\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
            // Fanned cards
            "card-juggler" => "<rect x=\"3\" y=\"6\" width=\"10\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" transform=\"rotate(-12 8 13)\"/><rect x=\"8\" y=\"4\" width=\"10\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\"/><rect x=\"12\" y=\"5\" width=\"10\" height=\"14\" rx=\"2\" fill=\"currentColor\" opacity=\".35\" transform=\"rotate(12 17 12)\"/>",
            // Shield
            "sure-thing" => "<path d=\"M12 2.5l8 3v6c0 5-3.4 8.6-8 10-4.6-1.4-8-5-8-10v-6z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"round\"/><path d=\"M8.5 12l2.5 2.5 4.5-5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
            // Lightning bolt
            "quick-draw" => "<path d=\"M13.5 2L5 13.5h6L10 22l9-12h-6.2z\" fill=\"currentColor\"/>",
            // Hourglass
            "deep-thinker" => "<path d=\"M6 3h12M6 21h12M7 3c0 5 5 6 5 9s-5 4-5 9M17 3c0 5-5 6-5 9s5 4 5 9\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>",
            // Die
            _ => "<rect x=\"3.5\" y=\"3.5\" width=\"17\" height=\"17\" rx=\"4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"12\" cy=\"12\" r=\"1.6\" fill=\"currentColor\"/><circle cx=\"15.5\" cy=\"15.5\" r=\"1.6\" fill=\"currentColor\"/>"
        };

        return $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" aria-hidden=\"true\" focusable=\"false\">{body}</svg>";
    }
}
