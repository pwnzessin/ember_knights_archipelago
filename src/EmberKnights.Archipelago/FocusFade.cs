namespace EmberKnights.Archipelago;

/// <summary>
/// Decides how visible the HUD log window should be: it comes forward when a new message arrives, stays for a
/// while, then fades away. Pure logic with no game dependencies, so it can be unit tested.
/// Time is passed in by the caller, so the behaviour never depends on a clock.
/// </summary>
internal sealed class FocusFade
{
    private readonly float holdSeconds;
    private readonly float fadeInSeconds;
    private readonly float fadeOutSeconds;
    private readonly float restingAlpha;

    private float lastActivityAt = float.NegativeInfinity;
    private float alpha;
    private float lastTickAt = float.NaN;

    /// <param name="holdSeconds">How long the window stays fully visible after a message.</param>
    /// <param name="fadeInSeconds">How long it takes to appear. Zero shows it at once.</param>
    /// <param name="fadeOutSeconds">How long it takes to disappear after the hold. Zero hides it at once.</param>
    /// <param name="restingAlpha">Opacity once faded out. 0 hides it completely.</param>
    public FocusFade(float holdSeconds, float fadeInSeconds, float fadeOutSeconds, float restingAlpha = 0f)
    {
        this.holdSeconds = Math.Max(0f, holdSeconds);
        this.fadeInSeconds = Math.Max(0f, fadeInSeconds);
        this.fadeOutSeconds = Math.Max(0f, fadeOutSeconds);
        this.restingAlpha = Math.Clamp(restingAlpha, 0f, 1f);
        alpha = this.restingAlpha;
    }

    /// <summary>Current opacity from 0 (hidden) to 1 (fully visible).</summary>
    public float Alpha => alpha;

    /// <summary>True while the window is drawn at all, including mid-fade.</summary>
    public bool IsVisible => alpha > 0.001f;

    /// <summary>Call when a new message arrives. Restarts the hold, and brings the window forward.</summary>
    public void Poke(float now) => lastActivityAt = now;

    /// <summary>Advances the fade to <paramref name="now"/> and returns the new opacity.</summary>
    public float Tick(float now)
    {
        var elapsedSinceTick = float.IsNaN(lastTickAt) ? 0f : Math.Max(0f, now - lastTickAt);
        lastTickAt = now;

        var sinceActivity = now - lastActivityAt;
        var target = sinceActivity <= holdSeconds ? 1f : restingAlpha;

        if (target > alpha)
            alpha = fadeInSeconds <= 0f ? target : Math.Min(target, alpha + elapsedSinceTick / fadeInSeconds);
        else if (target < alpha)
            alpha = fadeOutSeconds <= 0f ? target : Math.Max(target, alpha - elapsedSinceTick / fadeOutSeconds);

        return alpha;
    }

    /// <summary>Shows the window immediately and holds it, for example when the player asks to see it.</summary>
    public void Pin(float now)
    {
        lastActivityAt = now;
        alpha = 1f;
        lastTickAt = now;
    }
}
