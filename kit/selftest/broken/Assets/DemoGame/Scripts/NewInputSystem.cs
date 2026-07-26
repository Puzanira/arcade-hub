using UnityEngine;
using UnityEngine.InputSystem;

namespace DemoGame
{
    // Broken fixture: new Input System device accessors are raw input too.
    public class NewInputSystem : MonoBehaviour
    {
        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                transform.Translate(Vector3.up);
            }
            if (Gamepad.current != null)
            {
                transform.Translate(Vector3.down);
            }
        }
    }
}
