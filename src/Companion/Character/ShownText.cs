using System.Text;

namespace Companion.Character;

/// <summary>
/// What the bubble has actually shown of the current reply. History gets this, not what the brain produced:
/// text that was never on screen must not be saved (D-063). Asked for another turn it answers "", so an
/// unseen reply can never leak into the history by a mix-up of turns.
/// </summary>
internal sealed class ShownText
{
    private readonly StringBuilder _text = new();
    private int _turn;

    public int Length => _text.Length;

    /// <summary>A new turn begins: nothing has been shown yet.</summary>
    public void Start(int turn) { _turn = turn; _text.Clear(); }

    public void Add(string revealed) => _text.Append(revealed);

    /// <summary>The text shown for the given turn; empty for any other turn.</summary>
    public string For(int turn) => turn == _turn ? _text.ToString() : "";
}
