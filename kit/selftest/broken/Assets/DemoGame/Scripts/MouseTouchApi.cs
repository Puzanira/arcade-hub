using UnityEngine;

namespace DemoGame
{
    // Broken fixture: non-GetKey members of UnityEngine.Input must be caught too.
    public class MouseTouchApi : MonoBehaviour
    {
        void Update()
        {
            Vector3 p = Input.mousePosition;
            int touches = Input.touchCount;
            Vector3 tilt = Input.acceleration;
            if (Input.GetMouseButtonDown(0))
            {
                transform.position = p + tilt * touches;
            }
        }
    }
}
