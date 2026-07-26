using UnityEngine;

namespace DemoGame
{
    // Broken fixture: reads raw input directly, bypassing ArcadeInput.
    public class Player : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                transform.Translate(Vector3.up);
            }
        }
    }
}
