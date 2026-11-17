namespace Companion.Character;

/// <summary>What the app is doing; drives the motion (D-043). Only Idle and Thinking are drawn so far (D-051).</summary>
internal enum Activity { Idle, Thinking, Talking, Sleeping }

/// <summary>
/// The animator interface (architecture). Step 2 has SetActivity; SetExpression (happy, teasing, flirty, concerned
/// from the mood tag) is added with the mood work in step 3.
/// </summary>
internal interface IAnimator
{
    void SetActivity(Activity activity);
}
