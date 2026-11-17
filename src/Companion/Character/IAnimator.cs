using Companion.Core;

namespace Companion.Character;

/// <summary>What the app is doing; drives the motion (D-043). Idle, Thinking and Talking are drawn (D-051); Sleeping comes in step 4.</summary>
internal enum Activity { Idle, Thinking, Talking, Sleeping }

/// <summary>How she feels, from the mood tag at the end of a reply; drives the colour (D-043). None = no tag, the normal blue.</summary>
internal enum Expression { None, Happy, Teasing, Flirty, Concerned }

internal static class ExpressionMap
{
    /// <summary>[idle] and a missing tag both mean no expression.</summary>
    public static Expression From(Mood? mood) => mood switch
    {
        Mood.Happy => Expression.Happy,
        Mood.Teasing => Expression.Teasing,
        Mood.Flirty => Expression.Flirty,
        Mood.Concerned => Expression.Concerned,
        _ => Expression.None,
    };
}

/// <summary>The animator interface (architecture): the app sets the Activity, the reply's tag sets the Expression.</summary>
internal interface IAnimator
{
    void SetActivity(Activity activity);
    void SetExpression(Expression expression);
}
