using UnityEngine;

namespace DemoGame
{
    // Broken fixture: whitespace tricks must not bypass the raw-input scan.
    public class SpacedInput : MonoBehaviour
    {
        void Update()
        {
            if (Input . GetKeyDown(KeyCode.A))
            {
                var any = UnityEngine . Input.anyKey;
            }
        }
    }
}
