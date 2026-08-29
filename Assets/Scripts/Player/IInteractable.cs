namespace SheepGate.Player
{
    /// <summary>
    /// Optional contract for anything the player can tap. World objects are free to ignore it:
    /// <see cref="InteractBridge"/> also recognises any component exposing a public parameterless
    /// <c>Interact()</c> method, which is how the world module declares its interactables.
    /// </summary>
    public interface IInteractable
    {
        void Interact();
    }
}
