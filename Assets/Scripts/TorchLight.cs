using UnityEngine;
using UnityEngine.InputSystem;

public class TorchLight : MonoBehaviour
{
    [SerializeField] private InputActionReference toggleAction;
    [SerializeField] private Light torchLight;
    private void OnEnable()
    {
        toggleAction.action.performed += ToggleTorchLight;
    }
    private void OnDisable()
    {
        toggleAction.action.performed -= ToggleTorchLight;
    }
    private void ToggleTorchLight(InputAction.CallbackContext context)
    {
        if (torchLight != null)
        {
            torchLight.enabled = !torchLight.enabled;
        }

    }

}
