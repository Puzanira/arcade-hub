using UnityEngine;

namespace DemoGame
{
    // Broken fixture: file IS in rawInputAllowlist with baselineCount 1, but has
    // TWO raw-input lines — someone added new raw input to a legacy file.
    public class LegacyOverBaseline : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKey(KeyCode.C))
            {
                transform.Translate(Vector3.left);
            }
            if (Input.GetKeyDown(KeyCode.D)) // the "new" line beyond baseline
            {
                transform.Translate(Vector3.right);
            }
        }
    }
}
