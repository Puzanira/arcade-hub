using UnityEngine;
using UInput = UnityEngine.Input;

namespace DemoGame
{
    // Broken fixture: aliasing UnityEngine.Input must be flagged by itself.
    public class AliasInput : MonoBehaviour
    {
        void Update()
        {
            if (UInput.GetKey(KeyCode.B))
            {
                transform.Rotate(Vector3.up);
            }
        }
    }
}
