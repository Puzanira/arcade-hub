using AiGameStudio.ArcadeControls;
using UnityEngine;

namespace DemoGame
{
    // Contract-compliant: reads input only through ArcadeInput.
    public class Player : MonoBehaviour
    {
        void Update()
        {
            if (ArcadeInput.RedButton.IsHeld)
            {
                transform.Translate(ArcadeInput.Joystick.Vector * Time.deltaTime);
            }
        }
    }
}
