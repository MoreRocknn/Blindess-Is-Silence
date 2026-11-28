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

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip turnOnClip;
    [SerializeField] private AudioClip turnOffClip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    private bool isOn = true;
    private float maxIntensity;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (torchLight != null)
            maxIntensity = torchLight.intensity;

        // Si no se asigna un AudioSource, intenta cogerlo del mismo objeto
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
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

        PlaySound(isOn ? turnOnClip : turnOffClip);

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(Fade(isOn));
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, volume);
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