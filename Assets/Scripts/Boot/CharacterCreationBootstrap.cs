using UnityEngine;

namespace SheepGate.Boot
{
    /// <summary>
    /// Scene entry point for CharacterCreation.unity. All of the character creation UI is
    /// built programmatically by the composer; nothing is authored in the scene.
    /// </summary>
    public class CharacterCreationBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            SheepGate.UI.CharacterCreationScreen.Compose();
        }
    }
}
