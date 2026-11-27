using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class TorchLight : MonoBehaviour
{
    [SerializeField] private InputActionReference toggleAction;
    [SerializeField] private Light torchLight;
    [SerializeField] private ParticleSystem flameParticles;

    [Header("Transición")]
    [SerializeField] private float fadeOutTime = 0.15f; // tiempo de apagado de la luz
    [SerializeField] private float fadeInTime = 0.1f;   // tiempo de encendido de la luz

    private bool isOn = true;
    private float maxIntensity;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (torchLight != null)
            maxIntensity = torchLight.intensity;
    }

    private void OnEnable()
    {
        toggleAction.action.performed += ToggleTorchLight;
        toggleAction.action.Enable();
    }

    private void OnDisable()
    {
        toggleAction.action.performed -= ToggleTorchLight;
    }

    private void ToggleTorchLight(InputAction.CallbackContext context)
    {
        isOn = !isOn;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(Fade(isOn));
    }

    private IEnumerator Fade(bool turnOn)
    {
        float duration = turnOn ? fadeInTime : fadeOutTime;
        float startIntensity = torchLight.enabled ? torchLight.intensity : 0f;
        float targetIntensity = turnOn ? maxIntensity : 0f;
        if (turnOn)
        {
            torchLight.enabled = true;
            flameParticles.Clear();
            flameParticles.Simulate(0.3f, true, true); // adelanta 0.3 s de simulación
            flameParticles.Play();
        }
        else
        {
            // Corta la llama de golpe
            flameParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            torchLight.intensity = Mathf.Lerp(startIntensity, targetIntensity, t / duration);
            yield return null;
        }

        torchLight.intensity = targetIntensity;

        if (!turnOn)
            torchLight.enabled = false;
    }
}