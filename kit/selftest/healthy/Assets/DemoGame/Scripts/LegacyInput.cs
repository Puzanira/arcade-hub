using UnityEngine;

namespace DemoGame
{
    // Healthy fixture: legacy raw input, frozen at baselineCount 1 in
    // arcade-kit.json. Exactly at baseline -> stays green.
    public class LegacyInput : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKey(KeyCode.Space))
            {
                transform.Translate(Vector3.up);
            }
        }
    }
}
